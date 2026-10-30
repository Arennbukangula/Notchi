namespace WindowsNotch
{
    public sealed class AppSettings
    {
        public string? Anchor { get; set; }
        public bool AutoStart { get; set; }
        public string Language { get; set; } = Localization.DefaultLanguage;

        /// <summary>
        /// Modifier keys untuk global hotkey.
        /// MOD_ALT=1, MOD_CTRL=2, MOD_SHIFT=4, MOD_WIN=8
        /// Default: Ctrl+Shift (6)
        /// </summary>
        public uint HotkeyModifiers { get; set; } = 6; // Ctrl + Shift

        /// <summary>
        /// Virtual-key code untuk global hotkey.
        /// Default: N (0x4E)
        /// </summary>
        public uint HotkeyKey { get; set; } = 0x4E; // N
    }
}
