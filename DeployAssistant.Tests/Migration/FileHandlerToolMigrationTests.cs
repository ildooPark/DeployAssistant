#pragma warning disable CS0618  // V1 types used intentionally

using DeployAssistant.DataComponent;
using DeployAssistant.Migration;
using DeployAssistant.Migration.Steps;
using DeployAssistant.Model;
using DeployAssistant.Model.V2;
using DeployAssistant.Utils;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Xunit;

namespace DeployAssistant.Tests.Migration
{
    public class FileHandlerToolMigrationTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly FileHandlerTool _tool = new FileHandlerTool();

        public FileHandlerToolMigrationTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DA_MigTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }

        private string TempPath(string relative) => Path.Combine(_tempDir, relative);

        private static ProjectStore BuildProjectStore(string name = "TestStore")
        {
            var store = new ProjectStore(name, @"C:\TestStore");
            store.Current.SnapshotId  = "1.0";
            store.Current.ProjectName = name;
            store.Current.MachineId   = "PC01";
            store.History.Add(new SnapshotData { SnapshotId = "0.9", ProjectName = name });
            return store;
        }

        // ------------------------------------------------------------------ TrySerializeProjectStore

        [Fact]
        public void TrySerializeProjectStore_WritesFileAndReturnsTrue()
        {
            var store  = BuildProjectStore();
            string path = TempPath("store.bin");

            bool result = _tool.TrySerializeProjectStore(store, path);

            Assert.True(result);
            Assert.True(File.Exists(path));
            Assert.True(new FileInfo(path).Length > 0);
        }

        [Fact]
        public void TrySerializeProjectStore_CreatesBackupWhenFileExists()
        {
            var store  = BuildProjectStore();
            string path = TempPath("store_bak.bin");

            // First write
            _tool.TrySerializeProjectStore(store, path);
            string firstContent = File.ReadAllText(path);

            // Second write — should create .bak
            _tool.TrySerializeProjectStore(store, path);

            Assert.True(File.Exists(path + ".bak"));
            Assert.Equal(firstContent, File.ReadAllText(path + ".bak"));
        }

        // ------------------------------------------------------------------ TryDeserializeProjectStore  (V2 roundtrip)

        [Fact]
        public void TryDeserializeProjectStore_V2RoundTrip_ReturnsTrue()
        {
            var store  = BuildProjectStore("RoundTripProj");
            string path = TempPath("rt_store.bin");
            _tool.TrySerializeProjectStore(store, path);

            bool result = _tool.TryDeserializeProjectStore(path, out ProjectStore? restored);

            Assert.True(result);
            Assert.NotNull(restored);
            Assert.Equal("RoundTripProj", restored!.ProjectName);
        }

        [Fact]
        public void TryDeserializeProjectStore_V2RoundTrip_SchemaVersionIs2()
        {
            var store  = BuildProjectStore();
            string path = TempPath("sv2_store.bin");
            _tool.TrySerializeProjectStore(store, path);

            _tool.TryDeserializeProjectStore(path, out ProjectStore? restored);

            Assert.Equal(2, restored!.SchemaVersion);
        }

        [Fact]
        public void TryDeserializeProjectStore_CorruptFile_ReturnsFalse()
        {
            string path = TempPath("corrupt_store.bin");
            File.WriteAllText(path, "this is not valid base64");

            bool result = _tool.TryDeserializeProjectStore(path, out var store);

            Assert.False(result);
            Assert.Null(store);
        }

        [Fact]
        public void TryDeserializeProjectStore_MissingFile_ReturnsFalse()
        {
            bool result = _tool.TryDeserializeProjectStore(TempPath("ghost.bin"), out var store);

            Assert.False(result);
            Assert.Null(store);
        }

        // ------------------------------------------------------------------ TryDeserializeProjectStore  (V1 migration)

        private string WriteV1MetaFile(string name = "V1Proj")
        {
            var meta = new ProjectMetaData(name, @"C:\V1")
            {
                LocalUpdateCount = 2
            };
            var pd = new ProjectData(@"C:\V1")
            {
                ProjectName    = name,
                UpdaterName    = "Tester",
                ConductedPC    = "PCTEST",
                UpdatedVersion = "0.5",
                UpdateLog      = "v1 log",
                ChangeLog      = "v1 cl",
                NumberOfChanges = 0,
            };
            meta.ProjectDataList.AddLast(pd);
            meta.SetProjectMain(pd);

            string json   = JsonSerializer.Serialize(meta);
            string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
            string path   = TempPath($"v1_{name}.bin");
            File.WriteAllText(path, base64);
            return path;
        }

        [Fact]
        public void TryDeserializeProjectStore_V1File_ReturnsTrue()
        {
            string path  = WriteV1MetaFile("OldProj");
            bool   result = _tool.TryDeserializeProjectStore(path, out ProjectStore? store);

            Assert.True(result);
            Assert.NotNull(store);
        }

        [Fact]
        public void TryDeserializeProjectStore_V1File_SchemaVersionIs2AfterMigration()
        {
            string path = WriteV1MetaFile();
            _tool.TryDeserializeProjectStore(path, out ProjectStore? store);

            Assert.Equal(2, store!.SchemaVersion);
        }

        [Fact]
        public void TryDeserializeProjectStore_V1File_ProjectNamePreserved()
        {
            string path = WriteV1MetaFile("MigratedProject");
            _tool.TryDeserializeProjectStore(path, out ProjectStore? store);

            Assert.Equal("MigratedProject", store!.ProjectName);
        }

        [Fact]
        public void TryDeserializeProjectStore_V1File_CreatesBakFile()
        {
            string path = WriteV1MetaFile("BakTest");
            _tool.TryDeserializeProjectStore(path, out _);

            Assert.True(File.Exists(path + ".bak"),
                "A .bak safety copy should be created when migrating a V1 file.");
        }

        // ------------------------------------------------------------------ TryRollbackProjectStore

        [Fact]
        public void TryRollbackProjectStore_BakExists_RestoresAndReturnsTrue()
        {
            string path = TempPath("to_rollback.bin");
            File.WriteAllText(path, "v2 content");
            File.WriteAllText(path + ".bak", "v1 backup content");

            bool result = _tool.TryRollbackProjectStore(path);

            Assert.True(result);
            Assert.Equal("v1 backup content", File.ReadAllText(path));
        }

        [Fact]
        public void TryRollbackProjectStore_NoBakFile_ReturnsFalse()
        {
            string path = TempPath("no_bak.bin");
            File.WriteAllText(path, "content");

            bool result = _tool.TryRollbackProjectStore(path);

            Assert.False(result);
        }

        // ------------------------------------------------------------------ injectable pipeline

        [Fact]
        public void Constructor_InjectNullPipeline_ThrowsArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() =>
                new FileHandlerTool(null!));
        }

        [Fact]
        public void Constructor_InjectNullMigrationPipeline_V2FilesDeserialiseCorrectly()
        {
            // NullMigrationPipeline is valid as long as only V2 files are read.
            var tool  = new FileHandlerTool(new NullMigrationPipeline<ProjectStore>());
            var store = BuildProjectStore("NullPipelineProj");
            string path = TempPath("null_pipeline.bin");
            tool.TrySerializeProjectStore(store, path);

            bool result = tool.TryDeserializeProjectStore(path, out ProjectStore? restored);

            Assert.True(result);
            Assert.Equal("NullPipelineProj", restored!.ProjectName);
        }
    }
}
