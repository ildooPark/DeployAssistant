#pragma warning disable CS0618  // V1 types are the on-disk format; these tests exist to keep them readable.

using DeployAssistant.DataComponent;
using DeployAssistant.Interfaces;
using DeployAssistant.Model;
using DeployAssistant.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DeployAssistant.Tests.Utils
{
    /// <summary>
    /// Golden-fixture regression tests for <c>ProjectMetaData.bin</c> backward compatibility.
    /// <para>
    /// <b>Provenance of the fixture.</b>  <c>Fixtures/v1_3_6_1_ProjectMetaData.bin.txt</c> is a
    /// Base64-wrapped V1 JSON document hand-built to the model shape at git commit
    /// <c>7935ba4</c> (2024-04-04, namespace <c>SimpleBinaryVCS</c>) — the shape the 3.6.1
    /// build (2024-07-23) wrote.  It is <i>reconstructed from that source</i>, not captured
    /// from a real 3.6.1 file; no genuine 3.6.1 <c>ProjectMetaData.bin</c> was available.
    /// Verified against <c>7935ba4</c>: the serialized property names on
    /// <c>ProjectMetaData</c> / <c>ProjectData</c> / <c>ProjectFile</c> / <c>ChangedFile</c>,
    /// the <c>[JsonConstructor]</c> parameter lists, and the <c>DataState</c> /
    /// <c>ProjectDataType</c> ordinals are all identical to today's.
    /// </para>
    /// <para>
    /// The fixture deliberately contains the redundant <c>ProjectFilesObs</c> array, because
    /// <c>ProjectData.ProjectFilesObs</c> is a computed property that lacks
    /// <c>[JsonIgnore]</c> — in 3.6.1 and still today — so every real file has it.  Loading
    /// must ignore it rather than choke on it.
    /// </para>
    /// </summary>
    public class MetaDataBackwardCompatibilityTests : IDisposable
    {
        private const string FixtureFileName = "v1_3_6_1_ProjectMetaData.bin.txt";
        /// <summary>
        /// The TRUE shipped-3.6.1 store shape (the diverged production build, not the
        /// 7935ba4 repo lineage the main fixture reconstructs): <c>BackupFiles</c> is keyed
        /// by each file's MD5 <c>DataHash</c> — not by rel-path — with backup
        /// <c>DataSrcPath</c> values of the form
        /// <c>&lt;ProjectPath&gt;\Backup_&lt;ProjectName&gt;\Backup_&lt;UpdatedVersion&gt;</c>,
        /// and every PE-file entry carries <c>ProductVersion</c>.  One entry carries
        /// <c>DataState</c> 264 (Modified | Integrate) as the 3.6.1 integration flow wrote it.
        /// </summary>
        private const string HashKeyedFixtureFileName = "v1_3_6_1_hashKeyed_ProjectMetaData.bin.txt";

        private readonly string _tempDir;
        private readonly FileHandlerTool _tool = new FileHandlerTool();

        public MetaDataBackwardCompatibilityTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DA_BackCompat_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        // ------------------------------------------------------------------ fixture helpers

        /// <summary>
        /// Locates the checked-in Fixtures folder.  The fixtures are not copied to the output
        /// directory (that would need a .csproj change), so resolve them from the test source
        /// location instead.
        /// </summary>
        private static string FixtureDir([CallerFilePath] string callerFilePath = "")
        {
            // .../DeployAssistant.Tests/Utils/ThisFile.cs -> .../DeployAssistant.Tests/Fixtures
            string? utilsDir = Path.GetDirectoryName(callerFilePath);
            string? testsDir = utilsDir == null ? null : Path.GetDirectoryName(utilsDir);
            if (testsDir != null)
            {
                string fromSource = Path.Combine(testsDir, "Fixtures");
                if (Directory.Exists(fromSource)) return fromSource;
            }

            // Fallback: walk up from the test binaries until a Fixtures folder appears.
            DirectoryInfo? probe = new DirectoryInfo(AppContext.BaseDirectory);
            while (probe != null)
            {
                string candidate = Path.Combine(probe.FullName, "DeployAssistant.Tests", "Fixtures");
                if (Directory.Exists(candidate)) return candidate;
                probe = probe.Parent;
            }

            throw new DirectoryNotFoundException(
                "Could not locate the DeployAssistant.Tests/Fixtures folder from either the test " +
                "source path or the output directory.");
        }

        private static string ReadFixtureBase64() => ReadFixtureBase64(FixtureFileName);

        private static string ReadFixtureBase64(string fixtureFileName)
            => File.ReadAllText(Path.Combine(FixtureDir(), fixtureFileName)).Trim();

        private static string ReadFixtureJson()
            => Encoding.UTF8.GetString(Convert.FromBase64String(ReadFixtureBase64()));

        /// <summary>Writes arbitrary JSON to a Base64-wrapped store file inside the temp dir.</summary>
        private string WriteStoreFile(string fileName, string json)
        {
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, Convert.ToBase64String(Encoding.UTF8.GetBytes(json)));
            return path;
        }

        /// <summary>Copies the golden fixture verbatim to a loadable path.</summary>
        private string WriteFixtureFile(string fileName = "ProjectMetaData.bin")
        {
            string path = Path.Combine(_tempDir, fileName);
            File.WriteAllText(path, ReadFixtureBase64());
            return path;
        }

        /// <summary>Inserts a top-level property into the fixture JSON, keeping everything else intact.</summary>
        private static string WithTopLevelProperty(string json, string rawProperty)
            => "{" + rawProperty + "," + json.Substring(1);

        // ------------------------------------------------------------------ 3.6.1-shaped file round-trips

        [Fact]
        public void ThreeSixOneShapedFile_LoadsSuccessfully()
        {
            string path = WriteFixtureFile();

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Assert.Equal(MetaDataLoadFailure.None, result.Reason);
            Assert.NotNull(meta);
        }

        [Fact]
        public void ThreeSixOneShapedFile_TopLevelFieldsRoundTrip()
        {
            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);

            Assert.NotNull(meta);
            Assert.Equal("GlassInspector", meta!.ProjectName);
            Assert.Equal(@"D:\Deploy\GlassInspector", meta.ProjectPath);
            Assert.Equal(3, meta.LocalUpdateCount);
            Assert.NotNull(meta.ProjectMain);
            Assert.NotNull(meta.ProjectDataList);
            Assert.NotNull(meta.BackupFiles);
        }

        [Fact]
        public void ThreeSixOneShapedFile_ProjectMainRoundTrips()
        {
            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);
            ProjectData main = meta!.ProjectMain;

            Assert.Equal("GlassInspector", main.ProjectName);
            Assert.Equal(@"D:\Deploy\GlassInspector", main.ProjectPath);
            Assert.Equal("idpark", main.UpdaterName);
            Assert.Equal("CLE-WS-07", main.ConductedPC);
            Assert.Equal(new DateTime(2024, 7, 23, 9, 12, 44).AddTicks(1234567), main.UpdatedTime);
            Assert.Equal("v1.0.2", main.UpdatedVersion);
            Assert.Equal("3.6.1 deploy", main.UpdateLog);
            Assert.Equal("settings.ini modified", main.ChangeLog);
            Assert.Equal(2, main.RevisionNumber);
            Assert.Equal(1, main.NumberOfChanges);
            Assert.Equal(3, main.ProjectFiles.Count);
            Assert.Equal(2, main.ChangedFiles.Count);
        }

        [Fact]
        public void ThreeSixOneShapedFile_ProjectDataListRoundTrips()
        {
            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);
            List<ProjectData> versions = meta!.ProjectDataList.ToList();

            Assert.Equal(2, versions.Count);
            Assert.Equal("v1.0.2", versions[0].UpdatedVersion);
            Assert.Equal("v1.0.1", versions[1].UpdatedVersion);
            Assert.Equal(2, versions[0].RevisionNumber);
            Assert.Equal(1, versions[1].RevisionNumber);
            Assert.Equal("initial deploy", versions[1].UpdateLog);
            Assert.Equal("5D41402ABC4B2A76B9719D911017C592",
                versions[1].ProjectFiles["GlassInspector.exe"].DataHash);
        }

        [Fact]
        public void ThreeSixOneShapedFile_EveryProjectFileFieldRoundTrips()
        {
            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);
            ProjectFile exe = meta!.ProjectMain.ProjectFiles["GlassInspector.exe"];

            Assert.Equal(ProjectDataType.File, exe.DataType);
            Assert.Equal(4823040L, exe.DataSize);
            Assert.Equal("3.6.1.0", exe.BuildVersion);
            Assert.Equal("v1.0.2", exe.DeployedProjectVersion);
            Assert.Equal(new DateTime(2024, 7, 23, 9, 12, 44).AddTicks(1234567), exe.UpdatedTime);
            Assert.True(exe.IsDstFile);
            Assert.Equal(DataState.Added | DataState.IntegrityChecked, exe.DataState);
            Assert.Equal("GlassInspector.exe", exe.DataName);
            Assert.Equal(@"D:\Deploy\GlassInspector", exe.DataSrcPath);
            Assert.Equal("GlassInspector.exe", exe.DataRelPath);
            Assert.Equal("9E107D9D372BB6826BD81D3542A419D6", exe.DataHash);
            // Computed, never persisted — must still be derivable after a load.
            Assert.Equal(@"D:\Deploy\GlassInspector\GlassInspector.exe", exe.DataAbsPath);
        }

        [Fact]
        public void ThreeSixOneShapedFile_BackupFilesRoundTrip()
        {
            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);

            Assert.Single(meta!.BackupFiles);
            ProjectFile backup = meta.BackupFiles[@"Config\settings.ini"];
            Assert.Equal("settings.ini", backup.DataName);
            Assert.Equal(DataState.Backup, backup.DataState);
            Assert.Equal(2000L, backup.DataSize);
            Assert.Equal("v1.0.1", backup.DeployedProjectVersion);
            Assert.Equal(@"D:\Deploy\GlassInspector\Backup_GlassInspector\v1.0.1", backup.DataSrcPath);
            Assert.False(backup.IsDstFile);
        }

        [Fact]
        public void ThreeSixOneShapedFile_ChangedFilesRoundTrip()
        {
            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);
            List<ChangedFile> changes = meta!.ProjectMain.ChangedFiles;

            ChangedFile modified = changes[0];
            Assert.Equal(DataState.Modified, modified.DataState);
            Assert.NotNull(modified.SrcFile);
            Assert.NotNull(modified.DstFile);
            Assert.Equal(@"E:\Build\GlassInspector", modified.SrcFile!.DataSrcPath);
            Assert.Equal(2048L, modified.DstFile!.DataSize);

            // A null SrcFile is the historical encoding for "added"; it must stay null.
            ChangedFile added = changes[1];
            Assert.Equal(DataState.Added, added.DataState);
            Assert.Null(added.SrcFile);
            Assert.Equal(ProjectDataType.Directory, added.DstFile!.DataType);
        }

        [Fact]
        public void ThreeSixOneShapedFile_EnumMembersSurviveAsCorrectMembers()
        {
            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);
            Dictionary<string, ProjectFile> files = meta!.ProjectMain.ProjectFiles;

            Assert.Equal(ProjectDataType.File, files["GlassInspector.exe"].DataType);
            Assert.Equal(ProjectDataType.Directory, files["Config"].DataType);
            Assert.Equal(ProjectDataType.File, files[@"Config\settings.ini"].DataType);

            Assert.Equal(DataState.Added | DataState.IntegrityChecked, files["GlassInspector.exe"].DataState);
            Assert.Equal(DataState.None, files["Config"].DataState);
            Assert.Equal(DataState.Modified, files[@"Config\settings.ini"].DataState);
            Assert.Equal(DataState.Backup, meta.BackupFiles[@"Config\settings.ini"].DataState);
        }

        [Fact]
        public void ThreeSixOneShapedFile_RedundantProjectFilesObsArrayIsIgnored()
        {
            // The fixture carries the computed ProjectFilesObs array that the old (and current)
            // serializer emits. It must not be read back as extra state.
            Assert.Contains("\"ProjectFilesObs\"", ReadFixtureJson());

            _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);

            Assert.Equal(3, meta!.ProjectMain.ProjectFiles.Count);
            Assert.Equal(3, meta.ProjectMain.ProjectFilesObs.Count);
        }

        // ------------------------------------------------------------------ tolerance to drift

        [Fact]
        public void UnknownExtraProperty_StillLoads()
        {
            // The risky path is [JsonConstructor]-based binding (ProjectData, ProjectFile,
            // ChangedFile all use one). Unknown members must be skipped, not rejected.
            string json = ReadFixtureJson()
                .Replace("\"LocalUpdateCount\":3", "\"LocalUpdateCount\":3,\"FutureOnlyField\":{\"nested\":[1,2,3]}")
                .Replace("\"BuildVersion\":\"3.6.1.0\"", "\"BuildVersion\":\"3.6.1.0\",\"FutureFileField\":\"whatever\"");
            string path = WriteStoreFile("extra.bin", json);

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Assert.Equal("GlassInspector", meta!.ProjectName);
            Assert.Equal("3.6.1.0", meta.ProjectMain.ProjectFiles["GlassInspector.exe"].BuildVersion);
        }

        [Fact]
        public void AbsentSchemaVersion_IsTreatedAsV1()
        {
            Assert.DoesNotContain("\"SchemaVersion\"", ReadFixtureJson());

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(WriteFixtureFile(), out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.SchemaVersion);
            Assert.Equal(1, meta!.SchemaVersion);
        }

        [Fact]
        public void ExplicitSchemaVersionOne_LoadsAsV1()
        {
            string path = WriteStoreFile("v1explicit.bin",
                WithTopLevelProperty(ReadFixtureJson(), "\"SchemaVersion\":1"));

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Assert.Equal(1, result.SchemaVersion);
            Assert.Equal("GlassInspector", meta!.ProjectName);
        }

        [Fact]
        public void PropertyNameCasingDrift_StillLoads()
        {
            string json = ReadFixtureJson()
                .Replace("\"ProjectName\":\"GlassInspector\"", "\"projectName\":\"GlassInspector\"");
            string path = WriteStoreFile("casing.bin", json);

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Assert.Equal("GlassInspector", meta!.ProjectName);
        }

        [Fact]
        public void NumberWrittenAsString_StillLoads()
        {
            string json = ReadFixtureJson().Replace("\"DataSize\":4823040", "\"DataSize\":\"4823040\"");
            string path = WriteStoreFile("numstring.bin", json);

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Assert.Equal(4823040L, meta!.ProjectMain.ProjectFiles["GlassInspector.exe"].DataSize);
        }

        // ------------------------------------------------------------------ refusal / diagnosis

        [Fact]
        public void SchemaVersionNewerThanSupported_IsRefusedWithSchemaTooNew()
        {
            string path = WriteStoreFile("v999.bin",
                WithTopLevelProperty(ReadFixtureJson(), "\"SchemaVersion\":999"));

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.False(result.Success);
            Assert.Equal(MetaDataLoadFailure.SchemaTooNew, result.Reason);
            Assert.Equal(999, result.SchemaVersion);
            Assert.Null(meta);
            Assert.Contains("newer version of DeployAssistant", result.Message);
        }

        [Fact]
        public void CorruptBase64_ReportsNotBase64()
        {
            string path = Path.Combine(_tempDir, "corrupt.bin");
            File.WriteAllText(path, "this-is-not-base64!!!");

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.False(result.Success);
            Assert.Equal(MetaDataLoadFailure.NotBase64, result.Reason);
            Assert.Null(meta);
        }

        [Fact]
        public void MalformedJson_ReportsMalformedJson()
        {
            string path = WriteStoreFile("malformed.bin", "{\"ProjectName\":\"GlassInspector\",");

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.False(result.Success);
            Assert.Equal(MetaDataLoadFailure.MalformedJson, result.Reason);
            Assert.Null(meta);
        }

        [Fact]
        public void MissingFile_ReportsFileNotFound()
        {
            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(
                Path.Combine(_tempDir, "nope", "ProjectMetaData.bin"), out ProjectMetaData? meta);

            Assert.False(result.Success);
            Assert.Equal(MetaDataLoadFailure.FileNotFound, result.Reason);
            Assert.Null(meta);
        }

        [Fact]
        public void EmptyFile_ReportsEmptyDocument()
        {
            string path = Path.Combine(_tempDir, "empty.bin");
            File.WriteAllText(path, "");

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.False(result.Success);
            Assert.Equal(MetaDataLoadFailure.EmptyDocument, result.Reason);
            Assert.Null(meta);
        }

        [Fact]
        public void JsonNullDocument_ReportsEmptyDocument()
        {
            string path = WriteStoreFile("null.bin", "null");

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.False(result.Success);
            Assert.Equal(MetaDataLoadFailure.EmptyDocument, result.Reason);
            Assert.Null(meta);
        }

        [Fact]
        public void TryDeserializeProjectMetaData_BoolContractIsUnchanged()
        {
            Assert.True(_tool.TryDeserializeProjectMetaData(WriteFixtureFile(), out ProjectMetaData? ok));
            Assert.NotNull(ok);

            Assert.False(_tool.TryDeserializeProjectMetaData(
                Path.Combine(_tempDir, "absent.bin"), out ProjectMetaData? missing));
            Assert.Null(missing);
        }

        // ------------------------------------------------------------------ MetaDataManager surfaces the reason

        [Fact]
        public void RequestProjectRetrieval_RaisesLoadFailedEventWithReason()
        {
            var mgr = new MetaDataManager();
            mgr.Awake();
            MetaDataLoadResult? captured = null;
            mgr.MetaDataLoadFailedEventHandler += r => captured = r;

            string emptyProject = Path.Combine(_tempDir, "unmanaged");
            Directory.CreateDirectory(emptyProject);

            bool retrieved = mgr.RequestProjectRetrieval(emptyProject);

            Assert.False(retrieved);
            Assert.NotNull(captured);
            Assert.Equal(MetaDataLoadFailure.FileNotFound, captured!.Reason);
            Assert.False(string.IsNullOrWhiteSpace(captured.Message));
            Assert.Equal(MetaDataState.Idle, mgr.CurrentState);
        }

        [Fact]
        public void RequestProjectRetrieval_RaisesSchemaTooNewRatherThanMangling()
        {
            var mgr = new MetaDataManager();
            mgr.Awake();
            MetaDataLoadResult? captured = null;
            mgr.MetaDataLoadFailedEventHandler += r => captured = r;

            string projectDir = Path.Combine(_tempDir, "future");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "ProjectMetaData.bin"),
                Convert.ToBase64String(Encoding.UTF8.GetBytes(
                    WithTopLevelProperty(ReadFixtureJson(), "\"SchemaVersion\":999"))));

            bool retrieved = mgr.RequestProjectRetrieval(projectDir);

            Assert.False(retrieved);
            Assert.NotNull(captured);
            Assert.Equal(MetaDataLoadFailure.SchemaTooNew, captured!.Reason);
            Assert.Null(mgr.ProjectMetaData);
        }

        [Fact]
        public void RequestProjectRetrieval_LoadsThreeSixOneShapedProjectAndRebasesPaths()
        {
            var mgr = new MetaDataManager();
            mgr.Awake();

            string projectDir = Path.Combine(_tempDir, "moved");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "ProjectMetaData.bin"), ReadFixtureBase64());

            bool retrieved = mgr.RequestProjectRetrieval(projectDir);

            Assert.True(retrieved);
            Assert.NotNull(mgr.ProjectMetaData);
            // The fixture was written for D:\Deploy\GlassInspector; opening it elsewhere must rebase.
            Assert.Equal(projectDir, mgr.ProjectMetaData!.ProjectPath);
            Assert.Equal("GlassInspector", mgr.ProjectMetaData.ProjectName);
            Assert.Equal("v1.0.2", mgr.MainProjectData!.UpdatedVersion);
        }

        // ------------------------------------------------------------------ the wire-format guard

        /// <summary>
        /// Pins the serialized property names of every persisted V1 type.  A rename or a
        /// dropped property here would make every existing <c>ProjectMetaData.bin</c>
        /// unreadable, so this test failing is the signal to write a migration instead of
        /// shipping the rename.  Adding a property is allowed only after adding it to the
        /// expected set below <i>and</i> confirming old files still load (they will: unknown
        /// members are ignored on read, and a missing member falls back to the constructor
        /// default).
        /// </summary>
        [Theory]
        [MemberData(nameof(WireFormatCases))]
        public void WireFormat_PropertyNamesArePinned(string typeName, string[] expected, object instance)
        {
            string json = JsonSerializer.Serialize(instance, instance.GetType());
            using JsonDocument doc = JsonDocument.Parse(json);

            string[] actual = doc.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray();
            string[] wanted = expected.OrderBy(n => n, StringComparer.Ordinal).ToArray();

            Assert.True(wanted.SequenceEqual(actual),
                $"{typeName} wire format changed. Expected [{string.Join(", ", wanted)}] " +
                $"but serialized [{string.Join(", ", actual)}]. " +
                "Old ProjectMetaData.bin files depend on these exact names.");
        }

        public static IEnumerable<object[]> WireFormatCases()
        {
            yield return new object[]
            {
                nameof(ProjectMetaData),
                new[] { "SchemaVersion", "LocalUpdateCount", "ProjectName", "ProjectPath", "ProjectMain", "ProjectDataList", "BackupFiles" },
                new ProjectMetaData("Pinned", @"C:\Pinned")
            };
            yield return new object[]
            {
                nameof(ProjectData),
                new[] { "ProjectName", "ProjectPath", "UpdaterName", "ConductedPC", "UpdatedTime", "UpdatedVersion",
                        "UpdateLog", "ChangeLog", "RevisionNumber", "NumberOfChanges", "ChangedFiles", "ProjectFiles",
                        // Computed but not [JsonIgnore]d — present in every file ever written, including 3.6.1.
                        "ProjectFilesObs" },
                new ProjectData()
            };
            yield return new object[]
            {
                nameof(ProjectFile),
                // ProductVersion was serialized by the shipped 3.6.1 (it carries the build's
                // commit id) and was restored 2026-08-25; it is additive and 3.6.1-readable.
                new[] { "DataType", "DataSize", "BuildVersion", "ProductVersion", "DeployedProjectVersion", "UpdatedTime",
                        "IsDstFile", "DataState", "DataName", "DataSrcPath", "DataRelPath", "DataHash" },
                new ProjectFile(ProjectDataType.File)
            };
            yield return new object[]
            {
                nameof(ChangedFile),
                new[] { "SrcFile", "DstFile", "DataState" },
                new ChangedFile()
            };
        }

        /// <summary>
        /// Pins the numeric values of the two enums that are persisted as numbers.  Reordering
        /// either enum would silently re-interpret every stored file.
        /// </summary>
        [Fact]
        public void WireFormat_PersistedEnumOrdinalsArePinned()
        {
            Assert.Equal(0, (int)ProjectDataType.File);
            Assert.Equal(1, (int)ProjectDataType.Directory);

            Assert.Equal(0, (int)DataState.None);
            Assert.Equal(1, (int)DataState.Added);
            Assert.Equal(2, (int)DataState.Deleted);
            Assert.Equal(4, (int)DataState.Restored);
            Assert.Equal(8, (int)DataState.Modified);
            Assert.Equal(16, (int)DataState.PreStaged);
            Assert.Equal(32, (int)DataState.IntegrityChecked);
            Assert.Equal(64, (int)DataState.Backup);
            Assert.Equal(128, (int)DataState.Overlapped);
            // shipped 3.6.1 integration flow
            Assert.Equal(256, (int)DataState.Integrate);
        }

        /// <summary>
        /// Documents — deliberately — that the V1→V2 migration framework under
        /// <c>DeployAssistant.Core/Migration/</c> is NOT on the runtime load path.
        /// <c>RequestProjectRetrieval</c> reads V1 and hands V1 to every downstream manager.
        /// If a future change wires <c>TryLoadProjectStore</c> into the runtime, this test
        /// is the place that has to change, on purpose, with eyes open.
        /// </summary>
        [Fact]
        public void MigrationFramework_IsNotYetOnTheRuntimePath()
        {
            Assert.Equal(1, FileHandlerTool.MaxSupportedMetaDataSchemaVersion);
            Assert.Equal(2, FileHandlerTool.CurrentStoreSchemaVersion);

            var mgr = new MetaDataManager();
            mgr.Awake();

            string projectDir = Path.Combine(_tempDir, "runtimeV1");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "ProjectMetaData.bin"), ReadFixtureBase64());

            Assert.True(mgr.RequestProjectRetrieval(projectDir));

            // The loaded runtime state is the V1 type, and no .bak was produced — the store
            // path (which copies a .bak before migrating) was never entered.
            Assert.IsType<ProjectMetaData>(mgr.ProjectMetaData);
            Assert.False(File.Exists(Path.Combine(projectDir, "ProjectMetaData.bin.bak")));
        }

        // ------------------------------------------------------------------ shipped-3.6.1 wire details

        [Fact]
        public void ThreeSixOneShapedFile_ProductVersion_RoundTripsThroughLoadAndRewrite()
        {
            string json = ReadFixtureJson().Replace(
                "\"BuildVersion\":\"3.6.1.0\"",
                "\"BuildVersion\":\"3.6.1.0\",\"ProductVersion\":\"0.0.1682+HEAD.f05eda3\"");
            string path = WriteStoreFile("productversion.bin", json);

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Assert.Equal("0.0.1682+HEAD.f05eda3", meta!.ProjectMain.ProjectFiles["GlassInspector.exe"].ProductVersion);

            string rewrittenPath = Path.Combine(_tempDir, "productversion_rewritten.bin");
            Assert.True(_tool.TrySerializeProjectMetaData(meta, rewrittenPath));
            Assert.True(_tool.TryLoadProjectMetaData(rewrittenPath, out ProjectMetaData? reloaded).Success);
            Assert.Equal("0.0.1682+HEAD.f05eda3", reloaded!.ProjectMain.ProjectFiles["GlassInspector.exe"].ProductVersion);
        }

        [Fact]
        public void ThreeSixOneShapedFile_CaseDuplicateKeys_LoadWithLastWinsDedupe()
        {
            // The shipped 3.6.1 kept these dictionaries case-insensitive; a case-duplicate
            // pair (writable by an ordinal-keyed build) must collapse last-wins on load
            // instead of failing the whole document.
            const string upperDuplicate =
                "\"CONFIG\\\\SETTINGS.INI\":{\"DataType\":0,\"DataSize\":4096,\"BuildVersion\":\"\"," +
                "\"DeployedProjectVersion\":\"v1.0.2\",\"UpdatedTime\":\"2024-07-23T09:12:44.1234567\"," +
                "\"IsDstFile\":true,\"DataState\":8,\"DataName\":\"SETTINGS.INI\"," +
                "\"DataSrcPath\":\"D:\\\\Deploy\\\\GlassInspector\",\"DataRelPath\":\"CONFIG\\\\SETTINGS.INI\"," +
                "\"DataHash\":\"AB56B4D92B40713ACC5AF89985D4B786\"},";
            string json = ReadFixtureJson().Replace(
                "\"Config\\\\settings.ini\":{\"DataType\":0,\"DataSize\":2048",
                upperDuplicate + "\"Config\\\\settings.ini\":{\"DataType\":0,\"DataSize\":2048");
            string path = WriteStoreFile("casedup.bin", json);

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            Dictionary<string, ProjectFile> files = meta!.ProjectMain.ProjectFiles;
            Assert.Equal(3, files.Count);
            Assert.True(files.ContainsKey(@"Config\settings.ini"));
            Assert.True(files.ContainsKey(@"CONFIG\SETTINGS.INI"));
            // Last entry in document order wins: the 2048-byte lower-case one.
            Assert.Equal(2048L, files[@"Config\settings.ini"].DataSize);
            Assert.Equal(2048L, files[@"CONFIG\SETTINGS.INI"].DataSize);
        }

        [Fact]
        public void Serializer_NeverEmitsCaseDuplicateKeys()
        {
            var projectData = new ProjectData();
            projectData.ProjectFiles[@"Tools\Runner.exe"] = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 100, BuildVersion: "1.0",
                DeployedProjectVersion: "v1", UpdatedTime: new DateTime(2024, 7, 23),
                DataState: DataState.None, dataName: "Runner.exe", dataSrcPath: @"C:\Proj",
                dataRelPath: @"Tools\Runner.exe", dataHash: "AAAA", IsDstFile: true);
            projectData.ProjectFiles[@"TOOLS\RUNNER.EXE"] = new ProjectFile(
                DataType: ProjectDataType.File, DataSize: 200, BuildVersion: "1.1",
                DeployedProjectVersion: "v1", UpdatedTime: new DateTime(2024, 7, 24),
                DataState: DataState.None, dataName: "RUNNER.EXE", dataSrcPath: @"C:\Proj",
                dataRelPath: @"TOOLS\RUNNER.EXE", dataHash: "BBBB", IsDstFile: true);

            // The public surface must have collapsed the pair already.
            Assert.Single(projectData.ProjectFiles);
            Assert.Equal(200L, projectData.ProjectFiles[@"tools\runner.exe"].DataSize);

            var meta = new ProjectMetaData("NoDupes", @"C:\NoDupes");
            meta.ProjectMain = projectData;
            string path = Path.Combine(_tempDir, "nodupes.bin");
            Assert.True(_tool.TrySerializeProjectMetaData(meta, path));

            string json = Encoding.UTF8.GetString(Convert.FromBase64String(File.ReadAllText(path)));
            int keyOccurrences = System.Text.RegularExpressions.Regex.Matches(
                json, @"""Tools\\\\Runner\.exe"":",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase).Count;
            Assert.Equal(1, keyOccurrences);
        }

        [Fact]
        public void ThreeSixOneIntegrateFlag264_SurvivesLoadAndRewrite()
        {
            // 264 = Modified | Integrate, exactly as the shipped 3.6.1 integration flow wrote it.
            string json = ReadFixtureJson().Replace("\"DataState\":33", "\"DataState\":264");
            string path = WriteStoreFile("integrate264.bin", json);

            MetaDataLoadResult result = _tool.TryLoadProjectMetaData(path, out ProjectMetaData? meta);

            Assert.True(result.Success, result.Message);
            ProjectFile exe = meta!.ProjectMain.ProjectFiles["GlassInspector.exe"];
            Assert.Equal(264, (int)exe.DataState);
            Assert.Equal(DataState.Modified | DataState.Integrate, exe.DataState);

            string rewrittenPath = Path.Combine(_tempDir, "integrate264_rewritten.bin");
            Assert.True(_tool.TrySerializeProjectMetaData(meta, rewrittenPath));
            string rewrittenJson = Encoding.UTF8.GetString(Convert.FromBase64String(File.ReadAllText(rewrittenPath)));
            Assert.Contains("\"DataState\":264", rewrittenJson);
        }

        // ------------------------------------------------------------------ true shipped-3.6.1 runtime flows

        [Fact]
        public void ThreeSixOne_HashKeyedBackupFiles_RebaseAndCheckoutRestoresBytes()
        {
            string projectDir = Path.Combine(_tempDir, "hashKeyed");
            Directory.CreateDirectory(projectDir);
            File.WriteAllText(Path.Combine(projectDir, "ProjectMetaData.bin"),
                ReadFixtureBase64(HashKeyedFixtureFileName));

            // Working tree at the newest version's content.
            File.WriteAllText(Path.Combine(projectDir, "GlassInspector.exe"), "GlassInspector binary v1.0.2");
            Directory.CreateDirectory(Path.Combine(projectDir, "Config"));
            File.WriteAllText(Path.Combine(projectDir, "Config", "settings.ini"), "ini v2");

            // Real backup blobs in the 3.6.1 layout: Backup_<Project>\Backup_<Version>\<RelPath>.
            string backupRoot = Path.Combine(projectDir, "Backup_GlassInspector");
            Directory.CreateDirectory(Path.Combine(backupRoot, "Backup_v1.0.1"));
            Directory.CreateDirectory(Path.Combine(backupRoot, "Backup_v1.0.2", "Config"));
            File.WriteAllText(Path.Combine(backupRoot, "Backup_v1.0.1", "GlassInspector.exe"), "GlassInspector binary v1.0.1");
            File.WriteAllText(Path.Combine(backupRoot, "Backup_v1.0.2", "GlassInspector.exe"), "GlassInspector binary v1.0.2");
            File.WriteAllText(Path.Combine(backupRoot, "Backup_v1.0.2", "Config", "settings.ini"), "ini v2");

            var mgr = new MetaDataManager();
            mgr.Awake();

            // The fixture was written for D:\Deploy\GlassInspector; opening it here forces
            // ReconfigureProjectPath + SetBackupFilesPath over the hash-keyed dictionary.
            Assert.True(mgr.RequestProjectRetrieval(projectDir));

            ProjectFile rebasedBlob = mgr.ProjectMetaData!.BackupFiles["E5BAC1AF6F060FDAECF338FB2E5BB636"];
            Assert.Equal(Path.Combine(backupRoot, "Backup_v1.0.1"), rebasedBlob.DataSrcPath);
            Assert.True(File.Exists(rebasedBlob.DataAbsPath), $"rebased blob missing: {rebasedBlob.DataAbsPath}");

            ProjectData older = mgr.ProjectMetaData.ProjectDataList.First(p => p.UpdatedVersion == "v1.0.1");
            Assert.True(mgr.RequestCheckoutVersion(older, CheckoutMode.Fast));

            Assert.Equal(
                File.ReadAllBytes(Path.Combine(backupRoot, "Backup_v1.0.1", "GlassInspector.exe")),
                File.ReadAllBytes(Path.Combine(projectDir, "GlassInspector.exe")));
        }

        [Fact]
        public void ThreeSixOne_OrphanMainVersion_ReopenDoesNotDeleteExistingBackupFolders()
        {
            string projectDir = Path.Combine(_tempDir, "orphanMain");
            Directory.CreateDirectory(projectDir);

            // Orphan main: ProjectMain's version tag absent from ProjectDataList — a state
            // the shipped 3.6.1 could leave behind when an update was interrupted.
            string stagingPath = Path.Combine(_tempDir, "orphan_staging.bin");
            File.WriteAllText(stagingPath, ReadFixtureBase64(HashKeyedFixtureFileName));
            Assert.True(_tool.TryLoadProjectMetaData(stagingPath, out ProjectMetaData? meta).Success);
            meta!.ProjectMain.UpdatedVersion = "v1.0.3";
            Assert.True(_tool.TrySerializeProjectMetaData(meta, Path.Combine(projectDir, "ProjectMetaData.bin")));

            File.WriteAllText(Path.Combine(projectDir, "GlassInspector.exe"), "GlassInspector binary v1.0.2");

            string v1Folder = Path.Combine(projectDir, "Backup_GlassInspector", "Backup_v1.0.1");
            Directory.CreateDirectory(v1Folder);
            File.WriteAllText(Path.Combine(v1Folder, "GlassInspector.exe"), "GlassInspector binary v1.0.1");
            string sentinelPath = Path.Combine(v1Folder, "sentinel.keep");
            File.WriteAllText(sentinelPath, "must survive reopen");

            var first = new MetaDataManager();
            first.Awake();
            Assert.True(first.RequestProjectRetrieval(projectDir));

            var reopened = new MetaDataManager();
            reopened.Awake();
            Assert.True(reopened.RequestProjectRetrieval(projectDir));

            Assert.True(File.Exists(sentinelPath),
                "an existing backup folder was wiped while adopting the orphan project main");
            Assert.True(File.Exists(Path.Combine(v1Folder, "GlassInspector.exe")));
        }
    }
}
