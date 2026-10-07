using DeployAssistant.DataComponent;
using DeployAssistant.Model;
using DeployAssistant.ViewModel;
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
            Rect work = SystemParameters.WorkArea;
            Size min = compact
                ? WindowPlacement.CompactMinSize
                : new Size(System.Math.Min(WindowPlacement.FullMinSize.Width, work.Width),
                           System.Math.Min(WindowPlacement.FullMinSize.Height, work.Height));
            MinWidth = min.Width;
            MinHeight = min.Height;

            WindowBounds? saved = compact ? _layout.Compact : _layout.Full;
            Rect virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                                          SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
            Rect target = WindowPlacement.Fit(saved,
                compact ? WindowPlacement.CompactDefaultSize : WindowPlacement.FullDefaultSize,
                min, work, virtualScreen);
            Left = target.Left;
            Top = target.Top;
            Width = target.Width;
            Height = target.Height;

            if (!compact && saved?.Maximized == true) WindowState = WindowState.Maximized;
            Topmost = compact && pinned;
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

        /// <summary>
        /// The saved bounds when they are still on a connected screen with the title bar
        /// reachable; otherwise the default size centred in the primary work area. Either way the
        /// size is clamped between <paramref name="min"/> and the work area, so a window saved on
        /// a large monitor never opens bigger than a small laptop screen.
        /// </summary>
        public static Rect Fit(WindowBounds? saved, Size defaultSize, Size min, Rect workArea, Rect virtualScreen)
        {
            bool hasSaved = saved != null && saved.Width > 0 && saved.Height > 0;
            double w = Clamp(hasSaved ? saved!.Width : defaultSize.Width, min.Width, System.Math.Max(min.Width, workArea.Width));
            double h = Clamp(hasSaved ? saved!.Height : defaultSize.Height, min.Height, System.Math.Max(min.Height, workArea.Height));

            if (hasSaved)
            {
                var rect = new Rect(saved!.Left, saved.Top, w, h);
                Rect visible = Rect.Intersect(rect, virtualScreen);
                bool titleBarReachable = saved.Top >= virtualScreen.Top && saved.Top <= virtualScreen.Bottom - 40;
                if (!visible.IsEmpty && visible.Width >= 120 && visible.Height >= 80 && titleBarReachable)
                    return rect;
            }
            return new Rect(workArea.Left + (workArea.Width - w) / 2, workArea.Top + (workArea.Height - h) / 2, w, h);
        }

        private static double Clamp(double value, double min, double max) =>
            value < min ? min : value > max ? max : value;
    }
}
