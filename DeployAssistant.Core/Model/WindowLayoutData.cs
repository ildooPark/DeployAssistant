using System.Text.Json.Serialization;

namespace DeployAssistant.Model
{
    /// <summary>
    /// GUI window layout persisted in DeployAssistant.config (GUI 4.1.0+): the mode the window
    /// was closed in, each mode's last bounds, and the compact-mode pin. Lives in Core only
    /// because SettingManager owns the config file; nothing in Core reads it.
    /// </summary>
    public class WindowLayoutData
    {
        public const string FullMode = "Full";
        public const string CompactMode = "Compact";

        /// <summary><see cref="FullMode"/> or <see cref="CompactMode"/>; anything else reads as Full.</summary>
        public string? Mode { get; set; }
        public WindowBounds? Full { get; set; }
        public WindowBounds? Compact { get; set; }
        /// <summary>Compact window stays on top of other windows.</summary>
        public bool CompactPinned { get; set; }

        [JsonIgnore]
        public bool IsCompact => Mode == CompactMode;
    }

    /// <summary>Window position and size in device-independent pixels (1/96 inch).</summary>
    public class WindowBounds
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Maximized { get; set; }
    }
}
