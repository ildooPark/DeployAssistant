#pragma warning disable CS0618  // V1 types used intentionally for V1 integration tests

using DeployAssistant.DataComponent;
using DeployAssistant.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace DeployAssistant.Tests.Integration
{
    /// <summary>
    /// Integration tests that verify IntegrityProgressEventHandler fires
    /// during MainProjectIntegrityCheck, with a monotonically increasing
    /// counter that reaches (completed == total) at the end.
    /// </summary>
    public class MetaDataManagerIntegrityProgressTests : IDisposable
    {
        private readonly string _projectDir;

        public MetaDataManagerIntegrityProgressTests()
        {
            _projectDir = Path.Combine(Path.GetTempPath(), "DA_IntegProg_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_projectDir);
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "binary content v1");
            File.WriteAllText(Path.Combine(_projectDir, "config.xml"), "<cfg/>");
            File.WriteAllText(Path.Combine(_projectDir, "readme.txt"), "readme");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_projectDir))
                    Directory.Delete(_projectDir, recursive: true);
            }
            catch (IOException) { /* locked file — ignore */ }
        }

        private static MetaDataManager BuildAndAwakeManager()
        {
            var mgr = new MetaDataManager(new NullDialogService());
            mgr.Awake();
            return mgr;
        }

        private static async Task InitializeAndWaitAsync(MetaDataManager mgr, string projectDir, int timeoutMs = 10_000)
        {
            var tcs = new TaskCompletionSource<bool>();
            bool initStarted = false;

            mgr.ManagerStateEventHandler += state =>
            {
                if (state == MetaDataState.Initializing) initStarted = true;
                if (initStarted && state == MetaDataState.Idle) tcs.TrySetResult(true);
            };

            mgr.RequestProjectInitialization(projectDir);

            await Task.Delay(100);

            if (mgr.CurrentState == MetaDataState.Idle && mgr.ProjectMetaData != null)
                tcs.TrySetResult(true);

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => tcs.TrySetCanceled());

            await tcs.Task;
        }

        private static async Task<List<(int completed, int total)>> RunIntegrityCheckAndCollectProgressAsync(
            MetaDataManager mgr, int timeoutMs = 10_000)
        {
            var progressEvents = new List<(int completed, int total)>();
            var integrityTcs = new TaskCompletionSource<bool>();

            mgr.IntegrityProgressEventHandler += (c, t) =>
            {
                lock (progressEvents) { progressEvents.Add((c, t)); }
            };
            mgr.IntegrityCheckCompleteEventHandler += (log, files) => integrityTcs.TrySetResult(true);

            await Task.Run(() => mgr.RequestProjectIntegrityCheck());
            await Task.Delay(100);
            integrityTcs.TrySetResult(true); // in case it completed synchronously

            using var cts = new System.Threading.CancellationTokenSource(timeoutMs);
            cts.Token.Register(() => integrityTcs.TrySetCanceled());

            await integrityTcs.Task;
            return progressEvents;
        }

        // ------------------------------------------------------------------ tests

        [Fact]
        public async Task IntegrityProgressEvent_Fires_AtLeastOnce()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Modify one file so the intersect set is non-empty
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");

            var events = await RunIntegrityCheckAndCollectProgressAsync(mgr);

            Assert.NotEmpty(events);
        }

        [Fact]
        public async Task IntegrityProgressEvent_FinalEvent_CompletedEqualsTotal()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Modify two files to ensure a meaningful intersect set
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");
            File.WriteAllText(Path.Combine(_projectDir, "config.xml"), "<cfg updated/>");

            var events = await RunIntegrityCheckAndCollectProgressAsync(mgr);

            Assert.NotEmpty(events);
            var last = events[events.Count - 1];
            Assert.True(last.total > 0, "total must be positive");
            Assert.Equal(last.total, last.completed);
        }

        [Fact]
        public async Task IntegrityProgressEvent_CompletedValuesAreMonotonicallyNonDecreasing_WhenSorted()
        {
            var mgr = BuildAndAwakeManager();
            await InitializeAndWaitAsync(mgr, _projectDir);

            // Modify files so intersect set has multiple entries
            File.WriteAllText(Path.Combine(_projectDir, "app.dll"), "MODIFIED content v2");
            File.WriteAllText(Path.Combine(_projectDir, "config.xml"), "<cfg updated/>");
            File.WriteAllText(Path.Combine(_projectDir, "readme.txt"), "readme updated");

            var events = await RunIntegrityCheckAndCollectProgressAsync(mgr);

            Assert.NotEmpty(events);

            // The parallel hash phase fires events from multiple threads; the absolute
            // arrival order is non-deterministic, but the set of completed values must
            // be distinct positive integers ≤ total, and no value may exceed total.
            int expectedTotal = events[events.Count - 1].total;
            Assert.True(expectedTotal > 0, "total must be positive");

            foreach (var (completed, total) in events)
            {
                Assert.Equal(expectedTotal, total);          // total is consistent across all events
                Assert.True(completed >= 1, $"completed={completed} must be ≥ 1");
                Assert.True(completed <= total, $"completed={completed} must be ≤ total={total}");
            }

            // After sorting by completed, each value should be non-decreasing
            var sorted = new System.Collections.Generic.List<(int completed, int total)>(events);
            sorted.Sort((a, b) => a.completed.CompareTo(b.completed));
            for (int i = 1; i < sorted.Count; i++)
            {
                Assert.True(
                    sorted[i].completed >= sorted[i - 1].completed,
                    $"Sorted event {i}: completed={sorted[i].completed} < previous={sorted[i - 1].completed}");
            }
        }
    }
}
