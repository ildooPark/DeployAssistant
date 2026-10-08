using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace DeployAssistant.Services.Wpf
{
    /// <summary>
    /// Monitor work areas in device-independent pixels. <see cref="SystemParameters.WorkArea"/>
    /// is not usable in a per-monitor-DPI process: it divides the primary work area by the
    /// <em>system</em> DPI, which differs from the monitor's DPI under Remote Desktop, Windows
    /// Sandbox and after a scale change without sign-out — a 1624 px screen at 125% came back
    /// as 1624 DIP, so a 1600 DIP window opened 2000 px wide. Each monitor here is converted
    /// with its own effective DPI.
    /// </summary>
    internal static class MonitorWorkAreas
    {
        /// <summary>Every monitor's work area, the primary first. Empty if the Win32 calls fail.</summary>
        public static IReadOnlyList<Rect> AllInDips()
        {
            var areas = new List<Rect>();
            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr monitor, IntPtr hdc, ref RECT bounds, IntPtr data) =>
                {
                    if (TryWorkArea(monitor, out RECT work, out bool primary))
                    {
                        double scale = DpiOf(monitor) / 96.0;
                        var rect = ToRect(work, scale);
                        if (primary) areas.Insert(0, rect);
                        else areas.Add(rect);
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception) { areas.Clear(); }
            return areas;
        }

        /// <summary>Work area of the monitor nearest the window, in the window's own DIPs; null before it has a handle.</summary>
        public static Rect? ForWindow(Window window)
        {
            try
            {
                IntPtr hwnd = new WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return null;
                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (!TryWorkArea(monitor, out RECT work, out _)) return null;
                // The window's own DPI from Win32: WPF's VisualTreeHelper.GetDpi still reports
                // the startup value during SourceInitialized.
                uint dpi = 0;
                try { dpi = GetDpiForWindow(hwnd); } catch (EntryPointNotFoundException) { }
                double scale = dpi > 0 ? dpi / 96.0 : VisualTreeHelper.GetDpi(window).DpiScaleX;
                return ToRect(work, scale);
            }
            catch (Exception) { return null; }
        }

        private static Rect ToRect(RECT r, double scale) =>
            new Rect(r.Left / scale, r.Top / scale, (r.Right - r.Left) / scale, (r.Bottom - r.Top) / scale);

        private static bool TryWorkArea(IntPtr monitor, out RECT work, out bool primary)
        {
            var info = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            bool ok = GetMonitorInfo(monitor, ref info);
            work = info.rcWork;
            primary = (info.dwFlags & MONITORINFOF_PRIMARY) != 0;
            return ok;
        }

        private static uint DpiOf(IntPtr monitor)
        {
            try { return GetDpiForMonitor(monitor, MDT_EFFECTIVE_DPI, out uint x, out _) == 0 && x > 0 ? x : 96; }
            catch (Exception) { return 96; } // shcore missing (pre-8.1): treat as 100%
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;
        private const uint MONITORINFOF_PRIMARY = 1;
        private const int MDT_EFFECTIVE_DPI = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO { public int cbSize; public RECT rcMonitor; public RECT rcWork; public uint dwFlags; }

        private delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, ref RECT bounds, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

        [DllImport("user32.dll")]
        private static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);
    }
}
