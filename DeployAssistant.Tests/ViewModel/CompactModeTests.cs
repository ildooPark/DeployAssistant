#pragma warning disable CS0618  // ProjectFile/ProjectData are V1 types the integrity event still carries

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using DeployAssistant.DataComponent;

using DeployAssistant.Model;
using DeployAssistant.Tests.Fakes;
using DeployAssistant.Utils;
using DeployAssistant.View;
using DeployAssistant.ViewModel;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    public class CompactViewModelTests
    {
        private static CompactViewModel Build()
        {
            var manager = new MetaDataManager(new FakeDialogService());
            manager.Awake();
            return new CompactViewModel(manager, new ImmediateUiDispatcher());
        }

        // The parameterless ProjectFile is DataType.File (enum default).
        private static ProjectFile File(string relPath, DataState state) => new ProjectFile
        {
            DataRelPath = relPath,
            DataName = Path.GetFileName(relPath),
            DataState = state,
        };

        private static readonly DateTime At = new DateTime(2026, 10, 7, 10, 42, 0);

        [Fact]
        public void BeforeAnyCheck_ShowsNoResult()
        {
            var vm = Build();

            Assert.False(vm.HasResult);
            Assert.False(vm.IsClean);
            Assert.False(vm.HasChanges);
            Assert.False(vm.ShowAll.CanExecute(null));
        }

        [Fact]
        public void CleanResult_IsCleanWithSampleDetail()
        {
            var vm = Build();

            vm.ApplyResult("", new List<ProjectFile>(), samplePercent: 25, project: null, finishedAt: At);

            Assert.True(vm.IsClean);
            Assert.False(vm.HasChanges);
            Assert.Equal("Fast check, 25% sample · 10:42", vm.ResultDetail);
        }

        [Fact]
        public void FullHash_SaysSo()
        {
            var vm = Build();

            vm.ApplyResult("", new List<ProjectFile>(), samplePercent: 100, project: null, finishedAt: At);

            Assert.Equal("Full hash · 10:42", vm.ResultDetail);
        }

        [Fact]
        public void Changes_AreCountedByKind_AndCappedToMaxRows()
        {
            var vm = Build();
            var files = new List<ProjectFile>();
            for (int i = 0; i < 9; i++) files.Add(File($@"bin\m{i}.dll", DataState.Modified | DataState.IntegrityChecked));
            files.Add(File(@"bin\plugins\New.dll", DataState.Added | DataState.IntegrityChecked));
            files.Add(File(@"config\old.json", DataState.Deleted | DataState.IntegrityChecked));

            vm.ApplyResult("log", files, 100, null, At);

            Assert.True(vm.HasChanges);
            Assert.Equal(11, vm.ChangedCount);
            Assert.Equal(9, vm.ModifiedCount);
            Assert.Equal(1, vm.AddedCount);
            Assert.Equal(1, vm.DeletedCount);
            Assert.Equal(CompactViewModel.MaxRows, vm.TopChanges.Count);
            Assert.Equal(11 - CompactViewModel.MaxRows, vm.MoreCount);
            Assert.True(vm.ShowAll.CanExecute(null));
            var first = vm.TopChanges.First();
            Assert.Equal("MOD", first.Badge);
            Assert.Equal("m0.dll", first.Name);
            Assert.Equal("bin", first.Folder);
        }

        [Fact]
        public void DroppedFilesWaitingInTheQueue_AreNotCountedAsFindings()
        {
            var vm = Build();
            var files = new List<ProjectFile>
            {
                File(@"bin\engine.dll", DataState.PreStaged),
                File(@"bin\vision.dll", DataState.Modified | DataState.IntegrityChecked),
            };

            vm.ApplyResult("", files, 100, null, At);

            Assert.Equal(1, vm.ChangedCount);
            Assert.Equal(2, vm.LastFiles.Count); // "Show all" still hands the full list to the dashboard
        }

        [Fact]
        public void Reset_ClearsTheResultAndClosesRevisions()
        {
            var vm = Build();
            vm.ApplyResult("", new List<ProjectFile> { File("a.dll", DataState.Added | DataState.IntegrityChecked) }, 100, null, At);
            vm.IsRevisionsOpen = true;

            vm.Reset();

            Assert.False(vm.HasResult);
            Assert.False(vm.IsRevisionsOpen);
            Assert.Empty(vm.TopChanges);
        }

        [Fact]
        public void ShowAll_RaisesTheRequest()
        {
            var vm = Build();
            vm.ApplyResult("", new List<ProjectFile> { File("a.dll", DataState.Added | DataState.IntegrityChecked) }, 100, null, At);
            int raised = 0;
            vm.ShowAllRequested += () => raised++;

            vm.ShowAll.Execute(null);

            Assert.Equal(1, raised);
        }
    }

    public class WindowPlacementTests
    {
        // 1366 x 768 at 125% scaling: about 1093 x 614 DIP, minus a 40 DIP taskbar.
        private static readonly Rect SmallLaptop = new Rect(0, 0, 1093, 574);
        private static readonly Rect FullHd = new Rect(0, 0, 1920, 1040);
        private static readonly Rect FullHdScreen = new Rect(0, 0, 1920, 1080);

        [Fact]
        public void NoSavedBounds_FullMode_FitsASmallLaptop()
        {
            Size min = new Size(Math.Min(WindowPlacement.FullMinSize.Width, SmallLaptop.Width),
                                Math.Min(WindowPlacement.FullMinSize.Height, SmallLaptop.Height));

            Rect r = WindowPlacement.Fit(null, WindowPlacement.FullDefaultSize, min, SmallLaptop, SmallLaptop);

            Assert.True(r.Width <= SmallLaptop.Width && r.Height <= SmallLaptop.Height, r.ToString());
            Assert.True(r.Left >= 0 && r.Top >= 0, r.ToString());
        }

        [Fact]
        public void NoSavedBounds_CompactMode_IsCentredAtDefaultSize()
        {
            Rect r = WindowPlacement.Fit(null, WindowPlacement.CompactDefaultSize, WindowPlacement.CompactMinSize, FullHd, FullHdScreen);

            Assert.Equal(380, r.Width);
            Assert.Equal(600, r.Height);
            Assert.Equal((1920 - 380) / 2.0, r.Left);
        }

        [Fact]
        public void SavedBoundsOnAConnectedScreen_AreKept()
        {
            var saved = new WindowBounds { Left = 1500, Top = 300, Width = 400, Height = 620 };

            Rect r = WindowPlacement.Fit(saved, WindowPlacement.CompactDefaultSize, WindowPlacement.CompactMinSize, FullHd, FullHdScreen);

            Assert.Equal(new Rect(1500, 300, 400, 620), r);
        }

        [Fact]
        public void SavedBoundsOnADisconnectedMonitor_AreRecentred()
        {
            // Saved on a second monitor to the right that is no longer plugged in.
            var saved = new WindowBounds { Left = 2600, Top = 200, Width = 380, Height = 600 };

            Rect r = WindowPlacement.Fit(saved, WindowPlacement.CompactDefaultSize, WindowPlacement.CompactMinSize, FullHd, FullHdScreen);

            Assert.True(FullHdScreen.Contains(r), r.ToString());
        }

        [Fact]
        public void SavedBoundsWithTheTitleBarOffScreen_AreRecentred()
        {
            var saved = new WindowBounds { Left = 100, Top = -300, Width = 380, Height = 600 };

            Rect r = WindowPlacement.Fit(saved, WindowPlacement.CompactDefaultSize, WindowPlacement.CompactMinSize, FullHd, FullHdScreen);

            Assert.True(r.Top >= 0, r.ToString());
        }

        [Fact]
        public void SavedSizeBelowTheMinimum_IsRaised()
        {
            var saved = new WindowBounds { Left = 10, Top = 10, Width = 100, Height = 100 };

            Rect r = WindowPlacement.Fit(saved, WindowPlacement.CompactDefaultSize, WindowPlacement.CompactMinSize, FullHd, FullHdScreen);

            Assert.Equal(WindowPlacement.CompactMinSize.Width, r.Width);
            Assert.Equal(WindowPlacement.CompactMinSize.Height, r.Height);
        }
    }

    public class WindowLayoutConfigTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"DA_LayoutCfg_{Guid.NewGuid():N}.config");

        public void Dispose()
        {
            try { if (System.IO.File.Exists(_path)) System.IO.File.Delete(_path); } catch { }
        }

        [Fact]
        public void PreWindowLayoutConfig_LoadsWithNoLayout()
        {
            // A 4.0.x config: the GUI must fall back to the full layout with default bounds.
            System.IO.File.WriteAllText(_path,
                "{\"LastOpenedDstPath\":\"C:\\\\CLE\",\"Language\":\"ko-KR\",\"RecentProjects\":[\"C:\\\\CLE\"],\"FastIntegritySamplePercent\":25}");

            Assert.True(new FileHandlerTool().TryDeserializeJsonData(_path, out LocalConfigData? cfg));

            Assert.NotNull(cfg);
            Assert.Null(cfg!.WindowLayout);
            Assert.Equal(25, cfg.FastIntegritySamplePercent);
        }

        [Fact]
        public void WindowLayout_RoundTrips_WithoutTheComputedFlag()
        {
            var cfg = new LocalConfigData(@"C:\CLE")
            {
                WindowLayout = new WindowLayoutData
                {
                    Mode = WindowLayoutData.CompactMode,
                    CompactPinned = true,
                    Compact = new WindowBounds { Left = 1500, Top = 300, Width = 380, Height = 600 },
                    Full = new WindowBounds { Left = 0, Top = 0, Width = 1600, Height = 900, Maximized = true },
                },
            };
            var tool = new FileHandlerTool();

            Assert.True(tool.TrySerializeJsonData(_path, cfg));
            Assert.DoesNotContain("IsCompact", System.IO.File.ReadAllText(_path));
            Assert.True(tool.TryDeserializeJsonData(_path, out LocalConfigData? back));

            Assert.True(back!.WindowLayout!.IsCompact);
            Assert.True(back.WindowLayout.CompactPinned);
            Assert.Equal(1500, back.WindowLayout.Compact!.Left);
            Assert.True(back.WindowLayout.Full!.Maximized);
        }
    }
}
