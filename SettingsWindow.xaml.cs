using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsNotch
{
    public partial class SettingsWindow : Window
    {
        private static readonly Brush MutedBrush = FrozenBrush(0x6E, 0x6E, 0x73);
        private static readonly Brush ErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);

        private readonly AppSettings _settings;
        private bool _isReady;
        private bool _isClosing;
        private bool _initializing = true;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();

            _settings = settings;

            ShowCpuToggle.IsChecked = settings.ShowCpu;
            ShowRamToggle.IsChecked = settings.ShowRam;
            ShowDiskToggle.IsChecked = settings.ShowDisk;
            ShowBatteryToggle.IsChecked = settings.ShowBattery;

            AutoStartToggle.IsChecked = StartupService.IsEnabled();
            _settings.AutoStart = AutoStartToggle.IsChecked == true;

            ShowCpuToggle.Click += OnMetricToggleChanged;
            ShowRamToggle.Click += OnMetricToggleChanged;
            ShowDiskToggle.Click += OnMetricToggleChanged;
            ShowBatteryToggle.Click += OnMetricToggleChanged;
            AutoStartToggle.Click += OnAutoStartToggleChanged;
            CloseButton.Click += (s, e) => CloseFromUser();

            Activated += (s, e) => _isReady = true;
            Deactivated += (s, e) =>
            {
                if (_isReady) RequestClose();
            };

            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;

            Loaded += (s, e) => Activate();

            _initializing = false;
        }

        public event EventHandler? SettingsChanged;

        public bool ClosedByUser { get; private set; }

        private void OnMetricToggleChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            _settings.ShowCpu = ShowCpuToggle.IsChecked == true;
            _settings.ShowRam = ShowRamToggle.IsChecked == true;
            _settings.ShowDisk = ShowDiskToggle.IsChecked == true;
            _settings.ShowBattery = ShowBatteryToggle.IsChecked == true;

            SaveSettings();
        }

        private void OnAutoStartToggleChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            bool enabled = AutoStartToggle.IsChecked == true;

            if (!StartupService.SetEnabled(enabled))
            {
                _initializing = true;
                AutoStartToggle.IsChecked = !enabled;
                _initializing = false;
                SetStatus("Gagal mengubah autostart", isError: true);
                return;
            }

            _settings.AutoStart = enabled;
            SaveSettings();
        }

        private void SaveSettings()
        {
            bool saved = SettingsStore.Save(_settings);
            SetStatus(saved ? "Tersimpan" : "Gagal menyimpan", isError: !saved);

            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? ErrorBrush : MutedBrush;
        }

        private void CloseFromUser()
        {
            ClosedByUser = true;
            RequestClose();
        }

        private void RequestClose()
        {
            if (_isClosing) return;

            _isClosing = true;
            Close();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            CloseFromUser();
            e.Handled = true;
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
        }

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
