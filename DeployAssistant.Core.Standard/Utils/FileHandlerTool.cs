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

namespace DeployAssistant.Utils
{
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
        {
            try
            {
                string base64Str   = File.ReadAllText(filePath);
                byte[] jsonBytes   = Convert.FromBase64String(base64Str);
                string jsonString  = Encoding.UTF8.GetString(jsonBytes);

                int schemaVersion  = PeekSchemaVersion(jsonString);

                if (schemaVersion == CurrentStoreSchemaVersion)
                {
                    // Exact current version — deserialise directly.
                    projectStore = JsonSerializer.Deserialize<ProjectStore>(jsonString);
                    return projectStore != null;
                }

                if (schemaVersion > CurrentStoreSchemaVersion)
                {
                    // File was written by a newer version of the application.
                    // Attempting to deserialise it would risk silent data loss.
                    Trace.TraceError(
                        $"Cannot deserialize ProjectStore from '{filePath}': " +
                        $"file schema version {schemaVersion} is newer than the " +
                        $"supported version {CurrentStoreSchemaVersion}. " +
                        "Please upgrade the application.");
                    projectStore = null;
                    return false;
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
                ProjectMetaData? v1Meta = JsonSerializer.Deserialize<ProjectMetaData>(jsonString);
#pragma warning restore CS0618
                if (v1Meta == null)
                {
                    projectStore = null;
                    return false;
                }

                projectStore = _storeMigration.MigrateTo(v1Meta, fromVersion, CurrentStoreSchemaVersion);
                return projectStore != null;
            }
            catch (Exception ex)
            {
                Trace.TraceError($"Error deserializing ProjectStore: {ex.Message}");
                projectStore = null;
                return false;
            }
        }

        /// <summary>
        /// Rolls back a <c>ProjectMetaData.bin</c> (or any store file) to its
        /// previous version by copying the <c>.bak</c> file back over it.
        /// Returns <c>false</c> if no <c>.bak</c> file exists.
        /// </summary>
        public bool TryRollbackProjectStore(string filePath)
        {
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

        private static int PeekSchemaVersion(string jsonString)
        {
            try
            {
                using JsonDocument doc = JsonDocument.Parse(jsonString);
                if (doc.RootElement.TryGetProperty("SchemaVersion", out JsonElement el)
                    && el.TryGetInt32(out int version))
                    return version;
            }
            catch { /* malformed JSON — let the full deserialisation surface the error */ }
            return 0; // absent → treat as legacy
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
            try
            {
                var jsonData = JsonSerializer.Serialize(data);
                var base64EncodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));
                File.WriteAllText(filePath, base64EncodedData);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error serializing ProjectData: " + ex.Message);
                return false;
            }
        }
        public bool TryDeserializeProjectData(string filePath, out ProjectData? projectData)
        {
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
                Console.WriteLine("Error deserializing ProjectData: " + ex.Message);
                projectData = null;
                return false;
            }
        }
        public bool TrySerializeProjectMetaData(ProjectMetaData data, string filePath)
        {
            try
            {
                var jsonData = JsonSerializer.Serialize(data);
                var base64EncodedData = Convert.ToBase64String(Encoding.UTF8.GetBytes(jsonData));
                File.WriteAllText(filePath, base64EncodedData);
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error serializing ProjectMetaData: " + ex.Message);
                return false;
            }
        }
        public bool TryDeserializeProjectMetaData(string filePath, out ProjectMetaData? projectMetaData)
        {
            try
            {
                var jsonDataBase64 = File.ReadAllText(filePath);
                var jsonDataBytes = Convert.FromBase64String(jsonDataBase64);
                string jsonData = System.Text.Encoding.UTF8.GetString(jsonDataBytes);
                ProjectMetaData? data = JsonSerializer.Deserialize<ProjectMetaData>(jsonData);
                if (data != null)
                {
                    projectMetaData = data;
                    return true;
                }
                else
                    projectMetaData = null;
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error deserializing ProjectMetaData: " + ex.Message);
                projectMetaData = null;
                return false;
            }
        }
        public bool TrySerializeJsonData<T>(string filePath, in T? serializingObject)
        {
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
