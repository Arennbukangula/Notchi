namespace WindowsNotch
{
    public sealed class AppSettings
    {
        public string? Anchor { get; set; }
        public bool ShowCpu { get; set; } = true;
        public bool ShowRam { get; set; } = true;
        public bool ShowDisk { get; set; } = true;
        public bool ShowBattery { get; set; } = true;
        public bool AutoStart { get; set; }
        public string Language { get; set; } = Localization.DefaultLanguage;
    }
}
