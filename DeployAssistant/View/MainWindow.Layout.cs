using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.Services.Wpf;
using DeployAssistant.ViewModel;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace DeployAssistant.View
{
    /// <summary>
    /// Full/compact window layout: each mode keeps its own bounds and minimum size, compact can
    /// be pinned on top, and the mode plus both bounds persist in DeployAssistant.config.
    /// </summary>
    public partial class MainWindow
    {
        private MetaDataManager? _layoutStore;
        private WindowLayoutData _layout = new WindowLayoutData();

        private void InitWindowLayout(MetaDataManager store, MainViewModel vm)
        {
            _layoutStore = store;
            // Null on a pre-4.1.0 config: start in the full layout with default bounds.
            _layout = store.RequestWindowLayout() ?? new WindowLayoutData();
            vm.IsCompactPinned = _layout.CompactPinned;
            vm.IsCompact = _layout.IsCompact;
            ApplyMode(vm.IsCompact, vm.IsCompactPinned);
            SourceInitialized += (_, _) => ClampToCurrentMonitor();
            Loaded += (_, _) => ClampToCurrentMonitor();   // after any startup DPI change has landed

            vm.PropertyChanged += OnLayoutPropertyChanged;
            vm.CompactVM.ShowAllRequested += () =>
            {
                vm.IsCompact = false;
                OpenIntegrityLogWindowFromFileTrack(vm.CompactVM.LastProject, vm.CompactVM.LastChangeLog, vm.CompactVM.LastFiles);
            };
            Closing += (_, _) => SaveWindowLayout(vm.IsCompact, vm.IsCompactPinned);
        }

        private void OnLayoutPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not MainViewModel vm) return;
            if (e.PropertyName == nameof(MainViewModel.IsCompact))
            {
                // Remember where the mode being left was, then lay out the other one.
                StoreBounds(compact: !vm.IsCompact);
                ApplyMode(vm.IsCompact, vm.IsCompactPinned);
                SaveWindowLayout(vm.IsCompact, vm.IsCompactPinned);
            }
            else if (e.PropertyName == nameof(MainViewModel.IsCompactPinned))
            {
                Topmost = vm.IsCompact && vm.IsCompactPinned;
                SaveWindowLayout(vm.IsCompact, vm.IsCompactPinned);
            }
        }

        private void ApplyMode(bool compact, bool pinned)
        {
            WindowState = WindowState.Normal;
            IReadOnlyList<Rect> areas = MonitorWorkAreas.AllInDips();
            if (areas.Count == 0) areas = new[] { SystemParameters.WorkArea };
            Rect primary = areas[0];
            Size min = WindowPlacement.MinSizeFor(compact, primary);
            MinWidth = min.Width;
            MinHeight = min.Height;

            WindowBounds? saved = compact ? _layout.Compact : _layout.Full;
            Rect target = WindowPlacement.Fit(saved,
                compact ? WindowPlacement.CompactDefaultSize : WindowPlacement.FullDefaultSize,
                min, areas);
            Left = target.Left;
            Top = target.Top;
            Width = target.Width;
            Height = target.Height;
            LayoutTrace($"apply {(compact ? "compact" : "full")}: areas=[{string.Join(" | ", areas)}] min={min} " +
                        $"saved={(saved == null ? "none" : $"{saved.Left},{saved.Top},{saved.Width},{saved.Height}")} target={target}");

            if (!compact && saved?.Maximized == true) WindowState = WindowState.Maximized;
            Topmost = compact && pinned;
            ClampToCurrentMonitor();
        }

        /// <summary>
        /// Opt-in layout diagnostics: with DA_LAYOUT_TRACE set to a file path, every placement
        /// decision is appended there. For "the window opens off-screen" reports from a PC we
        /// cannot reproduce on.
        /// </summary>
        private static void LayoutTrace(string message)
        {
            string? path = System.Environment.GetEnvironmentVariable("DA_LAYOUT_TRACE");
            if (string.IsNullOrEmpty(path)) return;
            try { System.IO.File.AppendAllText(path, $"{System.DateTime.Now:HH:mm:ss.fff} {message}{System.Environment.NewLine}"); }
            catch (System.Exception) { }
        }

        /// <summary>
        /// Once the window has a handle its real DPI is known: re-fit it into the work area of
        /// the monitor it actually landed on. The placement above runs before the window exists,
        /// so on a monitor whose scale differs from the one it was computed for it can still
        /// spill over an edge.
        /// </summary>
        private void ClampToCurrentMonitor()
        {
            if (WindowState != WindowState.Normal) return;
            Rect? found = MonitorWorkAreas.ForWindow(this);
            if (found is not Rect area) { LayoutTrace("clamp: no monitor yet"); return; }
            MinWidth = System.Math.Min(MinWidth, area.Width);
            MinHeight = System.Math.Min(MinHeight, area.Height);
            var before = new Rect(Left, Top, Width, Height);
            Rect fitted = WindowPlacement.KeepInside(before, area);
            Left = fitted.Left;
            Top = fitted.Top;
            Width = fitted.Width;
            Height = fitted.Height;
            LayoutTrace($"clamp: area={area} {before} -> {fitted}");
        }

        private void StoreBounds(bool compact)
        {
            bool maximized = WindowState == WindowState.Maximized;
            Rect r = maximized && !RestoreBounds.IsEmpty ? RestoreBounds : new Rect(Left, Top, Width, Height);
            var bounds = new WindowBounds { Left = r.Left, Top = r.Top, Width = r.Width, Height = r.Height, Maximized = maximized };
            if (compact) _layout.Compact = bounds;
            else _layout.Full = bounds;
        }

        private void SaveWindowLayout(bool compact, bool pinned)
        {
            StoreBounds(compact);
            _layout.Mode = compact ? WindowLayoutData.CompactMode : WindowLayoutData.FullMode;
            _layout.CompactPinned = pinned;
            _layoutStore?.RequestSaveWindowLayout(_layout);
        }
    }

    /// <summary>Where a window of a given mode goes, given what was saved and the screens that exist now.</summary>
    public static class WindowPlacement
    {
        public static readonly Size FullDefaultSize = new Size(1600, 900);
        public static readonly Size FullMinSize = new Size(960, 600);
        public static readonly Size CompactDefaultSize = new Size(380, 600);
        public static readonly Size CompactMinSize = new Size(340, 480);

        /// <summary>The mode's minimum size, never larger than the primary work area.</summary>
        public static Size MinSizeFor(bool compact, Rect primaryWorkArea)
        {
            Size min = compact ? CompactMinSize : FullMinSize;
            return new Size(System.Math.Min(min.Width, primaryWorkArea.Width), System.Math.Min(min.Height, primaryWorkArea.Height));
        }

        /// <summary>
        /// The saved bounds when they still overlap a monitor's work area with the title bar
        /// reachable — kept on that monitor and shrunk to fit it; otherwise the default size
        /// centred in the primary work area (<paramref name="workAreas"/>[0]). Work areas are in
        /// DIPs, each converted with its own monitor's DPI (see MonitorWorkAreas).
        /// </summary>
        public static Rect Fit(WindowBounds? saved, Size defaultSize, Size min, IReadOnlyList<Rect> workAreas)
        {
            Rect primary = workAreas[0];
            if (saved != null && saved.Width > 0 && saved.Height > 0)
            {
                var rect = new Rect(saved.Left, saved.Top, System.Math.Max(saved.Width, min.Width), System.Math.Max(saved.Height, min.Height));
                Rect? home = null;
                double best = 0;
                foreach (Rect area in workAreas)
                {
                    Rect overlap = Rect.Intersect(rect, area);
                    if (overlap.IsEmpty || overlap.Width < 120 || overlap.Height < 80) continue;
                    bool titleBarReachable = saved.Top >= area.Top && saved.Top <= area.Bottom - 40;
                    if (titleBarReachable && overlap.Width * overlap.Height > best)
                    {
                        best = overlap.Width * overlap.Height;
                        home = area;
                    }
                }
                if (home is Rect target) return KeepInside(rect, target);
            }

            double w = Clamp(defaultSize.Width, min.Width, System.Math.Max(min.Width, primary.Width));
            double h = Clamp(defaultSize.Height, min.Height, System.Math.Max(min.Height, primary.Height));
            return new Rect(primary.Left + (primary.Width - w) / 2, primary.Top + (primary.Height - h) / 2, w, h);
        }

        /// <summary>Shrinks <paramref name="rect"/> to the area if needed, then slides it inside.</summary>
        public static Rect KeepInside(Rect rect, Rect area)
        {
            double w = System.Math.Min(rect.Width, area.Width);
            double h = System.Math.Min(rect.Height, area.Height);
            double left = Clamp(rect.Left, area.Left, area.Right - w);
            double top = Clamp(rect.Top, area.Top, area.Bottom - h);
            return new Rect(left, top, w, h);
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;
    }
}
