#pragma warning disable CS0618  // ProjectFile/ChangedFile are V1 types the manager events still carry

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Tests.Fakes;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// Regression: on a 4.1.1 site the integrity check listed 4 changes but Deploy committed 1.
    /// The check verifies modified files on up to 12 concurrent tasks, and each task added its
    /// finding to the plain Dictionaries behind the staged and pre-staged lists. Concurrent
    /// Dictionary writes lose entries, so the list Deploy uses (staged) could hold fewer
    /// changes than the list the result window shows (pre-staged).
    /// </summary>
    public class IntegrityConcurrencyTests : IDisposable
    {
        private readonly string _projectDir = Path.Combine(Path.GetTempPath(), "DA_IntegrityRace_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            try { if (Directory.Exists(_projectDir)) Directory.Delete(_projectDir, recursive: true); } catch { }
        }

        private async Task<MetaDataManager> InitializedProjectAsync(int files)
        {
            for (int i = 0; i < files; i++)
            {
                string dir = Path.Combine(_projectDir, $"sub{i % 10}");
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, $"f{i:D3}.dll"), $"original {i}");
            }
            var mgr = new MetaDataManager(new FakeDialogService());
            mgr.Awake();
            var idle = new TaskCompletionSource<bool>();
            bool started = false;
            void OnState(MetaDataState s)
            {
                if (s == MetaDataState.Initializing) started = true;
                if (started && s == MetaDataState.Idle) idle.TrySetResult(true);
            }
            mgr.ManagerStateEventHandler += OnState;
            mgr.RequestProjectInitialization(_projectDir);
            Assert.True(await Task.WhenAny(idle.Task, Task.Delay(30_000)) == idle.Task, "initialization timed out");
            mgr.ManagerStateEventHandler -= OnState;
            return mgr;
        }

        private static async Task<(List<ChangedFile> Staged, List<ProjectFile> Shown)> RunCheckAsync(MetaDataManager mgr)
        {
            List<ChangedFile>? staged = null;
            var shown = new TaskCompletionSource<List<ProjectFile>>();
            void OnStaged(object o) { if (o is List<ChangedFile> list) staged = new List<ChangedFile>(list); }
            void OnComplete(string log, ObservableCollection<ProjectFile> files) => shown.TrySetResult(files.ToList());
            mgr.StagedChangesEventHandler += OnStaged;
            mgr.IntegrityCheckCompleteEventHandler += OnComplete;
            try
            {
                mgr.RequestProjectIntegrityCheck(forceFullHash: true);
                Assert.True(await Task.WhenAny(shown.Task, Task.Delay(60_000)) == shown.Task, "integrity check timed out");
                return (staged ?? new List<ChangedFile>(), await shown.Task);
            }
            finally
            {
                mgr.StagedChangesEventHandler -= OnStaged;
                mgr.IntegrityCheckCompleteEventHandler -= OnComplete;
            }
        }

        [Fact]
        public async Task ManyModifiedFiles_DeployListMatchesTheShownResult()
        {
            const int files = 400;
            var mgr = await InitializedProjectAsync(files);
            var modified = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < files; i += 2)
            {
                string rel = Path.Combine($"sub{i % 10}", $"f{i:D3}.dll");
                File.WriteAllText(Path.Combine(_projectDir, rel), $"changed {i}");
                modified.Add(rel);
            }

            // A lost write is timing-dependent; a few rounds make the race all but certain to show.
            for (int round = 0; round < 5; round++)
            {
                var (staged, shown) = await RunCheckAsync(mgr);

                var stagedPaths = staged.Where(c => c.DstFile != null).Select(c => c.DstFile!.DataRelPath).ToList();
                var shownPaths = shown.Select(f => f.DataRelPath).ToList();
                Assert.True(modified.SetEquals(shownPaths), $"round {round}: shown {shownPaths.Count} of {modified.Count}");
                Assert.True(modified.SetEquals(stagedPaths), $"round {round}: staged for deploy {stagedPaths.Count} of {modified.Count}");
            }
        }

        [Fact]
        public async Task DeployRightAfterTheCheck_CommitsEveryFinding()
        {
            // The site flow: integrity check, then Deploy (no Stage in between).
            var mgr = await InitializedProjectAsync(60);
            File.WriteAllText(Path.Combine(_projectDir, "sub1", "f001.dll"), "changed a");
            File.WriteAllText(Path.Combine(_projectDir, "sub2", "f002.dll"), "changed b");
            File.Delete(Path.Combine(_projectDir, "sub3", "f003.dll"));
            File.WriteAllText(Path.Combine(_projectDir, "sub4", "new_plugin.dll"), "new");
            var (_, shown) = await RunCheckAsync(mgr);
            Assert.Equal(4, shown.Count);

            bool ok = mgr.RequestProjectUpdate("tester", "commit integrity findings", mgr.ProjectMetaData!.ProjectPath);

            Assert.True(ok);
            Assert.Equal(4, mgr.MainProjectData!.NumberOfChanges);
            Assert.Equal(shown.Select(f => f.DataRelPath).OrderBy(p => p),
                         mgr.MainProjectData.ChangedFiles.Select(c => (c.DstFile ?? c.SrcFile)!.DataRelPath).OrderBy(p => p));
        }

        [Fact]
        public async Task MixedChanges_EveryShownChangeIsStagedForDeploy()
        {
            // The site report: 4 changes found, 1 deployed. Added, deleted and modified files
            // together, the modified ones verified concurrently.
            var mgr = await InitializedProjectAsync(60);
            File.WriteAllText(Path.Combine(_projectDir, "sub1", "f001.dll"), "changed a");
            File.WriteAllText(Path.Combine(_projectDir, "sub2", "f002.dll"), "changed b");
            File.Delete(Path.Combine(_projectDir, "sub3", "f003.dll"));
            File.WriteAllText(Path.Combine(_projectDir, "sub4", "new_plugin.dll"), "new");

            for (int round = 0; round < 10; round++)
            {
                var (staged, shown) = await RunCheckAsync(mgr);

                Assert.Equal(4, shown.Count);
                Assert.Equal(shown.Select(f => f.DataRelPath).OrderBy(p => p),
                             staged.Where(c => c.DstFile != null).Select(c => c.DstFile!.DataRelPath).OrderBy(p => p));
            }
        }
    }
}
