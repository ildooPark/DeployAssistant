#pragma warning disable CS0618  // ProjectFile is a V1 type the integrity event still carries
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.View;
using DeployAssistant.ViewModel;
using Xunit;

namespace DeployAssistant.Tests.ViewModel
{
    /// <summary>
    /// Opt-in layout preview: builds the real <see cref="MainWindow"/> (XAML, resources,
    /// bindings) and renders the full and compact layouts off-screen to PNG files in
    /// DA_GUI_PREVIEW_DIR. There is no other UI harness, so this is the way to look at a layout
    /// change headlessly:
    /// <code>DA_GUI_PREVIEW_DIR=&lt;dir&gt; dotnet test DeployAssistant.Tests --filter MainWindowRenderTests</code>
    /// It does nothing without the variable: creating a WPF Application inside the shared test
    /// process makes every other test's Loc.T read live resources from another thread, which
    /// changes their fallback strings and can deadlock the dispatcher this test pumps.
    /// </summary>
    public class MainWindowRenderTests
    {
        [Fact]
        public void MainWindow_RendersFullAndCompactLayouts()
        {
            string? previewDir = Environment.GetEnvironmentVariable("DA_GUI_PREVIEW_DIR");
            if (string.IsNullOrEmpty(previewDir)) return;
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try { Run(previewDir); }
                catch (Exception e) { error = e; }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            Assert.True(error == null, error?.ToString());
        }

        private static ProjectFile F(string rel, DataState s) =>
            new ProjectFile { DataRelPath = rel, DataName = Path.GetFileName(rel), DataState = s };

        private static void Run(string? dir)
        {
            // One Application per AppDomain; the test host has no entry assembly for pack URIs.
            Application.ResourceAssembly ??= typeof(App).Assembly;
            var app = Application.Current as App ?? new App();
            app.InitializeComponent();
            typeof(App).GetProperty(nameof(App.Services))!.SetValue(app, Headless(new AppServices()));

            var window = new MainWindow();
            var vm = (MainViewModel)window.DataContext;
            vm.MetaDataVM.ProjectName = "CleVisionSystems";
            vm.MetaDataVM.CurrentVersion = "1.0.59";
            var at = new DateTime(2026, 10, 7, 10, 42, 0);

            vm.IsCompact = true;
            Render(window, 364, 561, dir, "compact-nocheck.png");

            vm.CompactVM.ApplyResult("", new List<ProjectFile>(), 25, null, at);
            Render(window, 364, 561, dir, "compact-clean.png");

            vm.CompactVM.ApplyResult("", new List<ProjectFile>
            {
                F(@"Vision\Recipes\Line12\recipe_12.xml", DataState.Modified | DataState.IntegrityChecked),
                F(@"bin\CleVision.Core.dll", DataState.Modified | DataState.IntegrityChecked),
                F(@"bin\plugins\NewInspect.dll", DataState.Added | DataState.IntegrityChecked),
                F(@"config\old_line.json", DataState.Deleted | DataState.IntegrityChecked),
                F(@"bin\CleVision.exe", DataState.Modified | DataState.IntegrityChecked),
            }, 100, null, at);
            Render(window, 364, 561, dir, "compact-changes.png");

            vm.CompactVM.IsRevisionsOpen = true;
            Render(window, 364, 561, dir, "compact-revisions.png");
            vm.CompactVM.IsRevisionsOpen = false;

            app.ApplyLanguage("en-US");
            vm.CompactVM.RefreshLocalizedText();
            Render(window, 364, 561, dir, "compact-changes-en.png");
            app.ApplyLanguage("ko-KR");

            vm.IsCompact = false;
            // 1366 x 768 at 125%: the window's client area is about 1077 x 535 DIP.
            Render(window, 1584, 861, dir, "full-default.png");
            Render(window, 1077, 535, dir, "full-small-laptop.png");
        }

        /// <summary>
        /// Swaps the WPF dialog service for the fake everywhere AppServices handed it out. A real
        /// MessageBox (e.g. the reopen-last-project prompt once other tests have recorded a path
        /// in the shared test config) would block this STA thread with nobody to click it.
        /// </summary>
        private static AppServices Headless(AppServices services)
        {
            var fake = new DeployAssistant.Tests.Fakes.FakeDialogService();
            const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
            typeof(AppServices).GetField("<DialogService>k__BackingField", Instance)!.SetValue(services, fake);
            MetaDataManager mgr = services.MetaDataManager;
            typeof(MetaDataManager).GetField("_dialogService", Instance)!.SetValue(mgr, fake);
            var settings = (SettingManager)typeof(MetaDataManager).GetField("_settingManager", Instance)!.GetValue(mgr)!;
            settings.DialogService = fake;
            return services;
        }

        private static void Render(Window window, int width, int height, string? dir, string name)
        {
            var root = (FrameworkElement)window.Content;
            root.Measure(new Size(width, height));
            root.Arrange(new Rect(0, 0, width, height));
            root.UpdateLayout();
            // DataGrid sizes star columns in a deferred dispatcher callback; let it run, then lay out again.
            System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
            root.UpdateLayout();

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            var background = new DrawingVisual();
            using (var dc = background.RenderOpen()) dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, width, height));
            bitmap.Render(background);
            bitmap.Render(root);

            if (string.IsNullOrEmpty(dir)) return;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(dir, name));
            encoder.Save(file);
        }
    }
}
