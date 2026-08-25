using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Migration;
using DeployAssistant.Migration.Steps;
using DeployAssistant.Model;
using DeployAssistant.Model.V2;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeployAssistant.Utils
{
    /// <summary>
    /// Why a persisted store file (<c>ProjectMetaData.bin</c>) could not be loaded.
    /// Carried by <see cref="MetaDataLoadResult"/> so the GUI and the CLI can tell the
    /// user *why* a project refused to open instead of showing a silent no-op.
    /// </summary>
    public enum MetaDataLoadFailure
    {
        /// <summary>Load succeeded; no failure.</summary>
        None = 0,
        /// <summary>No <c>ProjectMetaData.bin</c> at the given path (folder is not a managed project).</summary>
        FileNotFound,
        /// <summary>The file exists but its text is not valid Base64 — usually truncated or overwritten.</summary>
        NotBase64,
        /// <summary>The Base64 decoded, but the bytes underneath are not a well-formed JSON document.</summary>
        MalformedJson,
        /// <summary>The document declares a schema version this build does not understand yet.</summary>
        SchemaTooNew,
        /// <summary>A schema migration step was required and it failed or produced nothing.</summary>
        MigrationFailed,
        /// <summary>The file (or the decoded JSON) is empty, or the document is a bare <c>null</c>.</summary>
        EmptyDocument,
        /// <summary>Anything else — I/O errors, permission errors, unexpected exceptions.</summary>
        Unknown
    }

    /// <summary>
    /// Outcome of a store load.  <see cref="Success"/> mirrors the historical <c>bool</c>
    /// return of the <c>TryDeserialize*</c> methods; <see cref="Reason"/> and
    /// <see cref="Message"/> add the diagnosis those methods used to throw away into a
    /// <see cref="Trace"/> warning.
    /// </summary>
    public sealed class MetaDataLoadResult
    {
        public bool Success { get; }
        public MetaDataLoadFailure Reason { get; }
        /// <summary>User-facing, single-sentence explanation. Never null.</summary>
        public string Message { get; }
        /// <summary>Path the load was attempted from. Never null.</summary>
        public string FilePath { get; }
        /// <summary>
        /// Schema version read from the document, already normalised: a file written
        /// before the <c>SchemaVersion</c> field existed reports <c>1</c>, not <c>0</c>.
        /// </summary>
        public int SchemaVersion { get; }

        private MetaDataLoadResult(bool success, MetaDataLoadFailure reason, string message, string filePath, int schemaVersion)
        {
            Success = success;
            Reason = reason;
            Message = message;
            FilePath = filePath;
            SchemaVersion = schemaVersion;
        }

        public static MetaDataLoadResult Ok(string filePath, int schemaVersion)
            => new MetaDataLoadResult(true, MetaDataLoadFailure.None, "Loaded.", filePath ?? "", schemaVersion);

        public static MetaDataLoadResult Failed(MetaDataLoadFailure reason, string message, string filePath, int schemaVersion = 0)
            => new MetaDataLoadResult(false, reason, message, filePath ?? "", schemaVersion);
    }

    public class FileHandlerTool
    {
        // ------------------------------------------------------------------ injectable migration pipeline

        private readonly IMigrationPipeline<ProjectStore> _storeMigration;

        /// <summary>Current V2 schema version understood by this tool.</summary>
        public const int CurrentStoreSchemaVersion = 2;

        /// <summary>
        /// Parameterless constructor.  Uses a pre-configured
        /// <see cref="MigrationPipeline{T}"/> that can migrate
        /// V1 <c>ProjectMetaData</c> files to V2 <see cref="ProjectStore"/>.
        /// </summary>
        public FileHandlerTool()
            : this(BuildDefaultStorePipeline()) { }

        /// <summary>
        /// Injectable constructor.  Pass a custom <see cref="IMigrationPipeline{T}"/>
        /// to control schema migration behaviour (e.g. for testing or future version
        /// additions without modifying this class — Open/Closed).
        /// </summary>
        public FileHandlerTool(IMigrationPipeline<ProjectStore> storeMigration)
        {
            _storeMigration = storeMigration
                ?? throw new ArgumentNullException(nameof(storeMigration));
        }

        private static IMigrationPipeline<ProjectStore> BuildDefaultStorePipeline()
        {
            var step    = new ProjectMetaDataMigrationStep_1to2();
            var adapter = new MigrationStepAdapter<ProjectMetaData, ProjectStore>(step);
            return new MigrationPipeline<ProjectStore>(new[] { (IMigrationStepAdapter)adapter });
        }

        // ------------------------------------------------------------------ V2 ProjectStore serialize/deserialize

        /// <summary>
        /// Serialises a <see cref="ProjectStore"/> (V2 schema) to a Base64-encoded
        /// JSON file at <paramref name="filePath"/>.
        /// Before writing, if the file already exists it is copied to
        /// <c>{filePath}.bak</c> as a safety net so the previous content can be
        /// recovered if the write fails or if a rollback is later requested.
        /// </summary>
        public bool TrySerializeProjectStore(ProjectStore store, string filePath)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            try
            {
                if (File.Exists(filePath))
                {
                    try { File.Copy(filePath, filePath + ".bak", overwrite: true); }
                    catch (Exception bakEx)
                    {
                        Trace.TraceWarning($"Could not create .bak file for {filePath}: {bakEx.Message}");
                    }
                }

                string jsonData = JsonSerializer.Serialize(store);
                string base64   = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));
                File.WriteAllText(filePath, base64);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error serializing ProjectStore: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deserialises a <see cref="ProjectStore"/> from a Base64-encoded JSON file.
        /// <para>
        /// If the file contains a V1 <c>ProjectMetaData</c> document
        /// (<c>SchemaVersion</c> &lt;= 1 or absent), the method automatically:
        /// <list type="number">
        ///   <item>Copies the original file to <c>{filePath}.bak</c>.</item>
        ///   <item>Runs the injected <see cref="IMigrationPipeline{T}"/> to produce
        ///         a V2 <see cref="ProjectStore"/>.</item>
        ///   <item>Returns the migrated store; the caller may optionally persist it
        ///         by calling <see cref="TrySerializeProjectStore"/>.</item>
        /// </list>
        /// If migration fails the original <c>.bak</c> file is still available,
        /// the method returns <c>false</c>, and <paramref name="projectStore"/>
        /// is set to <c>null</c>.
        /// </para>
        /// </summary>
        public bool TryDeserializeProjectStore(string filePath, out ProjectStore? projectStore)
            => TryLoadProjectStore(filePath, out projectStore).Success;

        /// <summary>
        /// Same contract as <see cref="TryDeserializeProjectStore"/> but reports *why* the
        /// load failed instead of collapsing every cause into <c>false</c>.
        /// </summary>
        public MetaDataLoadResult TryLoadProjectStore(string filePath, out ProjectStore? projectStore)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            projectStore = null;

            MetaDataLoadResult read = TryReadStoreDocument(filePath, out string jsonString);
            if (!read.Success)
            {
                Trace.TraceError($"Error deserializing ProjectStore: {read.Message}");
                return read;
            }

            if (!TryPeekSchemaVersion(jsonString, out int schemaVersion, out string? peekError))
            {
                string msg = $"'{filePath}' does not contain a well-formed JSON document ({peekError}).";
                Trace.TraceError($"Error deserializing ProjectStore: {msg}");
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.MalformedJson, msg, filePath);
            }

            try
            {
                if (schemaVersion == CurrentStoreSchemaVersion)
                {
                    // Exact current version — deserialise directly.
                    projectStore = JsonSerializer.Deserialize<ProjectStore>(jsonString, LegacyReadOptions);
                    if (projectStore == null)
                        return MetaDataLoadResult.Failed(MetaDataLoadFailure.EmptyDocument,
                            $"'{filePath}' decoded to an empty JSON document.", filePath, schemaVersion);
                    return MetaDataLoadResult.Ok(filePath, schemaVersion);
                }

                if (schemaVersion > CurrentStoreSchemaVersion)
                {
                    // File was written by a newer version of the application.
                    // Attempting to deserialise it would risk silent data loss.
                    string msg =
                        $"'{filePath}' was written by a newer version of DeployAssistant " +
                        $"(schema version {schemaVersion}; this build supports up to {CurrentStoreSchemaVersion}). " +
                        "Please upgrade DeployAssistant to open this project.";
                    Trace.TraceError($"Cannot deserialize ProjectStore: {msg}");
                    return MetaDataLoadResult.Failed(MetaDataLoadFailure.SchemaTooNew, msg, filePath, schemaVersion);
                }

                // V1 file — save .bak then migrate.
                try { File.Copy(filePath, filePath + ".bak", overwrite: true); }
                catch (Exception bakEx)
                {
                    Trace.TraceWarning($"Could not create .bak before migration for {filePath}: {bakEx.Message}");
                }

                // Treat version 0 (field absent) as version 1 for migration routing.
                int fromVersion = NormalizeLegacySchemaVersion(schemaVersion);

#pragma warning disable CS0618
                ProjectMetaData? v1Meta = JsonSerializer.Deserialize<ProjectMetaData>(jsonString, LegacyReadOptions);
#pragma warning restore CS0618
                if (v1Meta == null)
                    return MetaDataLoadResult.Failed(MetaDataLoadFailure.EmptyDocument,
                        $"'{filePath}' decoded to an empty JSON document.", filePath, fromVersion);

                projectStore = _storeMigration.MigrateTo(v1Meta, fromVersion, CurrentStoreSchemaVersion);
                if (projectStore == null)
                    return MetaDataLoadResult.Failed(MetaDataLoadFailure.MigrationFailed,
                        $"Migrating '{filePath}' from schema version {fromVersion} to {CurrentStoreSchemaVersion} produced no data.",
                        filePath, fromVersion);
                return MetaDataLoadResult.Ok(filePath, CurrentStoreSchemaVersion);
            }
            catch (JsonException jsonEx)
            {
                projectStore = null;
                Trace.TraceError($"Error deserializing ProjectStore: {jsonEx.Message}");
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.MalformedJson,
                    $"'{filePath}' contains JSON this build cannot read ({jsonEx.Message}).", filePath, schemaVersion);
            }
            catch (Exception ex)
            {
                projectStore = null;
                Trace.TraceError($"Error deserializing ProjectStore: {ex.Message}");
                // The only work left in the try block after deserialisation is the migration pipeline.
                return MetaDataLoadResult.Failed(
                    schemaVersion < CurrentStoreSchemaVersion ? MetaDataLoadFailure.MigrationFailed : MetaDataLoadFailure.Unknown,
                    $"Could not read '{filePath}': {ex.Message}", filePath, schemaVersion);
            }
        }

        /// <summary>
        /// Rolls back a <c>ProjectMetaData.bin</c> (or any store file) to its
        /// previous version by copying the <c>.bak</c> file back over it.
        /// Returns <c>false</c> if no <c>.bak</c> file exists.
        /// </summary>
        public bool TryRollbackProjectStore(string filePath)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            try
            {
                string bakPath = filePath + ".bak";
                if (File.Exists(bakPath))
                {
                    File.Copy(bakPath, filePath, overwrite: true);
                    Trace.TraceInformation($"Restored {filePath} from {bakPath}");
                    return true;
                }
                Trace.TraceWarning($"No .bak file found for {filePath}; rollback skipped.");
                return false;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error rolling back ProjectStore: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Read options used for every store/metadata deserialisation.  They exist so a
        /// document written by an older build survives incidental drift: property-name
        /// casing, numbers that were emitted as strings, and trailing commas.  Unknown /
        /// extra members are ignored — that is System.Text.Json's default and it holds for
        /// the <c>[JsonConstructor]</c> path too, which is what lets a genuine 3.6.1 file
        /// (it serialises the computed <c>ProjectData.ProjectFilesObs</c> array because that
        /// property lacks <c>[JsonIgnore]</c>) load without complaint.
        /// </summary>
        private static readonly JsonSerializerOptions LegacyReadOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            NumberHandling = JsonNumberHandling.AllowReadingFromString,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip
        };

        /// <summary>
        /// Reads a Base64-wrapped store file and hands back the decoded JSON text,
        /// distinguishing "missing", "not Base64" and "empty" from each other.
        /// </summary>
        private static MetaDataLoadResult TryReadStoreDocument(string filePath, out string jsonString)
        {
            jsonString = "";
            if (string.IsNullOrWhiteSpace(filePath))
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.FileNotFound,
                    "No project metadata path was supplied.", filePath);

            if (!File.Exists(filePath))
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.FileNotFound,
                    $"'{filePath}' does not exist — this folder is not a DeployAssistant project yet.", filePath);

            string base64Str;
            try
            {
                base64Str = File.ReadAllText(filePath);
            }
            catch (Exception ex)
            {
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.Unknown,
                    $"Could not read '{filePath}': {ex.Message}", filePath);
            }

            if (string.IsNullOrWhiteSpace(base64Str))
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.EmptyDocument,
                    $"'{filePath}' is empty.", filePath);

            byte[] jsonBytes;
            try
            {
                jsonBytes = Convert.FromBase64String(base64Str);
            }
            catch (FormatException ex)
            {
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.NotBase64,
                    $"'{filePath}' is not valid Base64 — the file is truncated or was overwritten ({ex.Message}).", filePath);
            }

            jsonString = Encoding.UTF8.GetString(jsonBytes);
            if (string.IsNullOrWhiteSpace(jsonString))
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.EmptyDocument,
                    $"'{filePath}' decoded to an empty document.", filePath);

            return MetaDataLoadResult.Ok(filePath, 0);
        }

        private static int PeekSchemaVersion(string jsonString)
            => TryPeekSchemaVersion(jsonString, out int version, out _) ? version : 0;

        /// <summary>
        /// Reads only the <c>SchemaVersion</c> field of a document without deserialising it.
        /// Returns <c>false</c> when the text is not a well-formed JSON object (that is a
        /// malformed file, not a version-0 file).  A well-formed document that simply has no
        /// <c>SchemaVersion</c> field — every file written before the field existed, e.g.
        /// 3.6.1 — returns <c>true</c> with version <c>0</c>; callers normalise that to V1
        /// via <see cref="NormalizeLegacySchemaVersion"/>.
        /// </summary>
        private static bool TryPeekSchemaVersion(string jsonString, out int schemaVersion, out string? parseError)
        {
            schemaVersion = 0;
            parseError = null;
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonString);
                // A bare `null` document is well-formed JSON; it is empty, not malformed, so
                // let the deserializer report it as EmptyDocument rather than failing here.
                if (doc.RootElement.ValueKind == JsonValueKind.Null) return true;
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                {
                    parseError = $"root element is {doc.RootElement.ValueKind}, expected an object";
                    return false;
                }
                if (doc.RootElement.TryGetProperty("SchemaVersion", out JsonElement el)
                    && el.TryGetInt32(out int version))
                {
                    schemaVersion = version;
                    return true;
                }
                // Tolerate casing drift the same way LegacyReadOptions does.
                foreach (JsonProperty prop in doc.RootElement.EnumerateObject())
                {
                    if (!string.Equals(prop.Name, "SchemaVersion", StringComparison.OrdinalIgnoreCase)) continue;
                    if (prop.Value.TryGetInt32(out int looseVersion)) schemaVersion = looseVersion;
                    break;
                }
                return true;
            }
            catch (JsonException ex)
            {
                parseError = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// Maps a raw schema version read from a file to the canonical V1 version
        /// number used for migration routing.  Old files written before the
        /// <c>SchemaVersion</c> field was introduced will have version 0 (field
        /// absent); they are treated identically to an explicit version 1.
        /// </summary>
        private static int NormalizeLegacySchemaVersion(int rawVersion)
            => rawVersion < 1 ? 1 : rawVersion;

        // ------------------------------------------------------------------ V1 legacy methods (preserved for backward compatibility)

        public bool TrySerializeProjectData(ProjectData data, string filePath)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            try
            {
                var jsonData = JsonSerializer.Serialize(data);
                var base64EncodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));
                File.WriteAllText(filePath, base64EncodedData);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Error serializing ProjectData: " + ex.Message);
                return false;
            }
        }
        public bool TryDeserializeProjectData(string filePath, out ProjectData? projectData)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            try
            {
                var jsonDataBase64 = File.ReadAllText(filePath);
                var jsonDataBytes = Convert.FromBase64String(jsonDataBase64);
                string jsonString = System.Text.Encoding.UTF8.GetString(jsonDataBytes);
                ProjectData? data = JsonSerializer.Deserialize<ProjectData>(jsonString);
                if (data != null)
                {
                    projectData = data;
                    return true;
                }
                projectData = null;
                return false;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Error deserializing ProjectData: " + ex.Message);
                projectData = null;
                return false;
            }
        }
        /// <summary>
        /// Existing overload.  Writes atomically and keeps no backup copy, so the on-disk
        /// footprint is exactly what callers produced before — only the "half-written file
        /// after a crash" failure mode is gone.
        /// </summary>
        public bool TrySerializeProjectMetaData(ProjectMetaData data, string filePath)
            => TrySerializeProjectMetaData(data, filePath, backupFilePath: null);

        /// <summary>
        /// Serialises <paramref name="data"/> to <paramref name="filePath"/> through a
        /// temp file + <see cref="File.Replace(string,string,string)"/> so a crash or a
        /// full disk can never leave a half-written <c>ProjectMetaData.bin</c>.
        /// <para>
        /// When <paramref name="backupFilePath"/> is supplied the previous content is moved
        /// there as part of the same Replace call, so the backup is produced atomically too.
        /// <c>BackupManager.DeleteVersion</c> relies on it to roll the store back when a
        /// mutation has to be undone.  The caller chooses the location deliberately: a
        /// sibling <c>.bak</c> in the project root would be picked up by the integrity scan
        /// as a brand-new project file.
        /// </para>
        /// The mirror of <see cref="TrySerializeProjectStore"/>'s .bak safety net.
        /// </summary>
        public bool TrySerializeProjectMetaData(ProjectMetaData data, string filePath, string? backupFilePath)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            if (backupFilePath != null) backupFilePath = PathCompat.ToNetFrameworkLongPath(backupFilePath);
            // Keep the temp beside the backup when one was requested (that folder is excluded
            // from project scans), otherwise beside the destination. Either way it is on the
            // same volume, which File.Replace requires.
            string tempDir = Path.GetDirectoryName(backupFilePath ?? filePath) ?? "";
            string tempPath = Path.Combine(tempDir, "ProjectMetaData.bin.tmp");
            try
            {
                var jsonData = JsonSerializer.Serialize(data);
                var base64EncodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));

                if (!string.IsNullOrEmpty(tempDir) && !Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                File.WriteAllText(tempPath, base64EncodedData);

                if (File.Exists(filePath))
                {
                    File.Replace(tempPath, filePath, backupFilePath);
                }
                else
                {
                    // First write — nothing to back up and nothing to replace.
                    File.Move(tempPath, filePath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Error serializing ProjectMetaData: " + ex.Message);
                return false;
            }
            finally
            {
                try { if (File.Exists(tempPath)) File.Delete(tempPath); }
                catch (Exception cleanupEx)
                {
                    Trace.TraceWarning($"Could not remove temp file {tempPath}: {cleanupEx.Message}");
                }
            }
        }

        /// <summary>
        /// Restores <paramref name="filePath"/> from the backup written by
        /// <see cref="TrySerializeProjectMetaData(ProjectMetaData,string,string?)"/>.
        /// Returns false when the backup is absent or the copy fails.
        /// </summary>
        public bool TryRestoreProjectMetaData(string backupFilePath, string filePath)
        {
            backupFilePath = PathCompat.ToNetFrameworkLongPath(backupFilePath);
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            try
            {
                if (string.IsNullOrEmpty(backupFilePath) || !File.Exists(backupFilePath)) return false;
                File.Copy(backupFilePath, filePath, overwrite: true);
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Could not restore {filePath} from {backupFilePath}: {ex.Message}");
                return false;
            }
        }
        /// <summary>
        /// Highest <c>ProjectMetaData</c> schema version this build can read.
        /// <para>
        /// V1 is deliberately still the runtime on-disk format.  <c>DeployAssistant.Core/Migration/</c>
        /// and <see cref="TryLoadProjectStore"/> can already turn a V1 document into a V2
        /// <see cref="ProjectStore"/>, but <b>nothing on the runtime path calls them</b> —
        /// <c>MetaDataManager.RequestProjectRetrieval</c> loads V1 through
        /// <see cref="TryLoadProjectMetaData"/> and everything downstream (FileManager,
        /// BackupManager, UpdateManager, the ViewModels) is V1-shaped.  Do not assume the
        /// migration framework is live; flipping the runtime to V2 is its own change.
        /// <c>MetaDataBackwardCompatibilityTests</c> pins this fact.
        /// </para>
        /// </summary>
        public const int MaxSupportedMetaDataSchemaVersion = 1;

        public bool TryDeserializeProjectMetaData(string filePath, out ProjectMetaData? projectMetaData)
            => TryLoadProjectMetaData(filePath, out projectMetaData).Success;

        /// <summary>
        /// Loads a V1 <see cref="ProjectMetaData"/> document, reporting a structured reason
        /// when it cannot.  Same success semantics as
        /// <see cref="TryDeserializeProjectMetaData(string, out ProjectMetaData?)"/>, which
        /// simply forwards here — the <c>bool</c> contract of existing callers is unchanged.
        /// <para>
        /// The read is deliberately forgiving of drift (see <see cref="LegacyReadOptions"/>)
        /// and treats an absent <c>SchemaVersion</c> as version 1, because a file written by
        /// 3.6.1 has no such field at all.  It is *not* forgiving in the other direction: a
        /// document declaring a newer schema is refused rather than partially read, so a
        /// newer-format project can never be silently downgraded by an older build.
        /// </para>
        /// </summary>
#pragma warning disable CS0618 // ProjectMetaData is the V1 on-disk type; reading it is this method's whole job.
        public MetaDataLoadResult TryLoadProjectMetaData(string filePath, out ProjectMetaData? projectMetaData)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            projectMetaData = null;

            MetaDataLoadResult read = TryReadStoreDocument(filePath, out string jsonData);
            if (!read.Success)
            {
                Trace.TraceWarning("Error deserializing ProjectMetaData: " + read.Message);
                return read;
            }

            if (!TryPeekSchemaVersion(jsonData, out int rawSchemaVersion, out string? peekError))
            {
                string msg = $"'{filePath}' does not contain a well-formed JSON document ({peekError}).";
                Trace.TraceWarning("Error deserializing ProjectMetaData: " + msg);
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.MalformedJson, msg, filePath);
            }

            if (rawSchemaVersion > MaxSupportedMetaDataSchemaVersion)
            {
                string msg =
                    $"'{filePath}' was written by a newer version of DeployAssistant " +
                    $"(schema version {rawSchemaVersion}; this build supports up to {MaxSupportedMetaDataSchemaVersion}). " +
                    "Please upgrade DeployAssistant to open this project.";
                Trace.TraceWarning("Error deserializing ProjectMetaData: " + msg);
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.SchemaTooNew, msg, filePath, rawSchemaVersion);
            }

            // Absent field (0) means "written before the field existed" — i.e. V1.
            int schemaVersion = NormalizeLegacySchemaVersion(rawSchemaVersion);

            ProjectMetaData? data;
            try
            {
                data = JsonSerializer.Deserialize<ProjectMetaData>(jsonData, LegacyReadOptions);
            }
            catch (JsonException jsonEx)
            {
                Trace.TraceWarning("Error deserializing ProjectMetaData: " + jsonEx.Message);
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.MalformedJson,
                    $"'{filePath}' contains JSON this build cannot read ({jsonEx.Message}).", filePath, schemaVersion);
            }
            catch (NotSupportedException notSupportedEx)
            {
                Trace.TraceWarning("Error deserializing ProjectMetaData: " + notSupportedEx.Message);
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.MalformedJson,
                    $"'{filePath}' contains a value this build cannot map onto the project model ({notSupportedEx.Message}).",
                    filePath, schemaVersion);
            }
            catch (Exception ex)
            {
                Trace.TraceWarning("Error deserializing ProjectMetaData: " + ex.Message);
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.Unknown,
                    $"Could not read '{filePath}': {ex.Message}", filePath, schemaVersion);
            }

            if (data == null)
                return MetaDataLoadResult.Failed(MetaDataLoadFailure.EmptyDocument,
                    $"'{filePath}' decoded to an empty JSON document.", filePath, schemaVersion);

            // A pre-SchemaVersion file leaves the field at whatever the constructor set;
            // stamp the normalised value so anything re-serialised from here is explicit.
            data.SchemaVersion = schemaVersion;
            projectMetaData = data;
            return MetaDataLoadResult.Ok(filePath, schemaVersion);
        }
#pragma warning restore CS0618
        public bool TrySerializeJsonData<T>(string filePath, in T? serializingObject)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            try
            {
                var jsonOption = new JsonSerializerOptions { WriteIndented = true };
                var jsonData = JsonSerializer.Serialize(serializingObject, jsonOption);
                File.WriteAllText(filePath, jsonData);
                return true;
            }
            catch (Exception ex)
            {
                return false;
            }
        }
        public bool TryDeserializeJsonData<T>(string filePath, out T? serializingObject)
        {
            filePath = PathCompat.ToNetFrameworkLongPath(filePath);
            try
            {
                var jsonDataBytes = File.ReadAllBytes(filePath);
                T? serializingObj = JsonSerializer.Deserialize<T>(jsonDataBytes);
                if (serializingObj != null)
                {
                    serializingObject = serializingObj;
                    return true;
                }
                else
                {
                    serializingObject = default;
                    return false;
                }
            }
            catch (Exception ex)
            {
                serializingObject = default;
                return false;
            }
        }
        public bool TryApplyFileChanges(List<ChangedFile> Changes)
        {
            if (Changes == null) return false;
            try
            {
                foreach (ChangedFile file in Changes)
                {
                    if ((file.DataState & DataState.IntegrityChecked) != 0) continue;
                    bool result = HandleData(file.SrcFile, file.DstFile, file.DataState);
                    if (!result) return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Couldn't Process File Changes : {ex.Message}");
                return false;
            }
        }
        public bool HandleData(IProjectData dstData, DataState state)
        {
            bool result;
            if (dstData.DataType == ProjectDataType.File)
            {
                result = HandleFile(null, dstData.DataAbsPath, state);
            }
            else
            {
                result = HandleDirectory(null, dstData.DataAbsPath, state);
            }
            return result;
        }
        public bool HandleData(IProjectData? srcData, IProjectData dstData, DataState state)
        {
            bool result;
            if (dstData.DataType == ProjectDataType.File)
            {
                result = HandleFile(srcData?.DataAbsPath, dstData.DataAbsPath, state);
            }
            else
            {
                result = HandleDirectory(srcData?.DataAbsPath, dstData.DataAbsPath, state);
            }
            return result;
        }
        public bool HandleData(string? srcPath, string dstPath, ProjectDataType type, DataState state)
        {
            bool result;
            if (type == ProjectDataType.File)
            {
                result = HandleFile(srcPath, dstPath, state);
            }
            else
            {
                result = HandleDirectory(srcPath, dstPath, state);
            }
            return result;
        }
        public void HandleData(string? srcPath, IProjectData dstData, DataState state)
        {
            if (dstData.DataType == ProjectDataType.File)
            {
                HandleFile(srcPath, dstData.DataAbsPath, state);
            }
            else
            {
                HandleDirectory(srcPath, dstData.DataAbsPath, state);
            }
        }
        public bool HandleDirectory(string? srcPath, string dstPath, DataState state)
        {
            if (srcPath != null) srcPath = PathCompat.ToNetFrameworkLongPath(srcPath);
            dstPath = PathCompat.ToNetFrameworkLongPath(dstPath);
            try
            {
                if ((state & DataState.Deleted) != 0)
                {
                    if (Directory.Exists(dstPath))
                        Directory.Delete(dstPath, true);
                }
                else
                {
                    if (!Directory.Exists(dstPath))
                        Directory.CreateDirectory(dstPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message); return false;
            }
        }
        public bool HandleFile(string? srcPath, string dstPath, DataState state)
        {
            if (srcPath != null) srcPath = PathCompat.ToNetFrameworkLongPath(srcPath);
            dstPath = PathCompat.ToNetFrameworkLongPath(dstPath);
            try
            {
                if ((state & DataState.Deleted) != 0)
                {
                    if (File.Exists(dstPath))
                        File.Delete(dstPath);
                    return true;
                }
                if (srcPath == null)
                {
                    Trace.TraceWarning($"Source File is null while File Handle state is {state.ToString()}");
                    return false;
                }
                if ((state & DataState.Added) != 0)
                {
                    if (!Directory.Exists(Path.GetDirectoryName(dstPath)))
                        Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                    if (!File.Exists(dstPath))
                        File.Copy(srcPath, dstPath, true);
                }
                else
                {
                    if (!Directory.Exists(Path.GetDirectoryName(dstPath)))
                        Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                    if (srcPath == dstPath)
                    {
                        return false;
                    }
                    File.Copy(srcPath, dstPath, true);
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceError(ex.Message); return false;
            }
        }

        public bool MoveFile(string? srcPath, string dstPath)
        {
            if (srcPath != null) srcPath = PathCompat.ToNetFrameworkLongPath(srcPath);
            dstPath = PathCompat.ToNetFrameworkLongPath(dstPath);
            try
            {
                if (!Directory.Exists(Path.GetDirectoryName(dstPath)))
                    Directory.CreateDirectory(Path.GetDirectoryName(dstPath));
                if (srcPath != dstPath)
                {
                    if (File.Exists(dstPath)) File.Delete(dstPath);
                    File.Move(srcPath, dstPath);
                }
                return true;
            }
            catch (Exception ex)
            {
                Trace.TraceWarning($"Couldn't Move File {ex.Message}");
                return false;
            }
        }
    }
}
