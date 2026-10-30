using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsNotch
{
    public partial class SettingsWindow : Window
    {
        private static readonly Brush MutedBrush = FrozenBrush(0x6E, 0x6E, 0x73);
        private static readonly Brush ErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);
        private static readonly Brush AccentBrush = FrozenBrush(0x3D, 0x7E, 0xFF);

        private readonly AppSettings _settings;
        private bool _isReady;
        private bool _isClosing;
        private bool _initializing = true;
        private bool _isRecordingHotkey;

        public SettingsWindow(AppSettings settings)
        {
            InitializeComponent();

            _settings = settings;

            AutoStartToggle.IsChecked = StartupService.IsEnabled();
            _settings.AutoStart = AutoStartToggle.IsChecked == true;

            string language = Localization.Normalize(settings.Language);
            _settings.Language = language;
            IndonesianOption.IsChecked = language == "id";
            EnglishOption.IsChecked = language == "en";

            // Tampilkan hotkey saat ini
            HotkeyLabel.Text = FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);

            AutoStartToggle.Click += OnAutoStartToggleChanged;
            IndonesianOption.Checked += OnLanguageChanged;
            EnglishOption.Checked += OnLanguageChanged;
            CloseButton.Click += (s, e) => CloseFromUser();

            HeaderRow.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton == MouseButton.Left)
                {
                    try { DragMove(); } catch (InvalidOperationException) { }
                }
            };
            CardBorder.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ChangedButton != MouseButton.Left) return;
                if (IsInteractive(e.OriginalSource as DependencyObject)) return;

                try { DragMove(); } catch (InvalidOperationException) { }
            };

            HotkeyRecorder.MouseLeftButtonDown += OnHotkeyRecorderClick;

            Activated += (s, e) => _isReady = true;
            Deactivated += OnDeactivated;
            Closed += (s, e) =>
            {
                _isClosing = true;
                _isReady = false;
            };

            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;

            Loaded += (s, e) => Activate();

            _initializing = false;
        }

        public event EventHandler? SettingsChanged;

        public bool ClosedByUser { get; private set; }

        private void OnHotkeyRecorderClick(object sender, MouseButtonEventArgs e)
        {
            if (_isRecordingHotkey) return;

            _isRecordingHotkey = true;
            HotkeyLabel.Text = Localization.Get("HotkeyRecording");
            HotkeyRecorder.BorderBrush = AccentBrush;
            e.Handled = true;
        }

        private static bool IsInteractive(DependencyObject? obj)
        {
            while (obj != null)
            {
                if (obj is System.Windows.Controls.Primitives.ButtonBase ||
                    obj is System.Windows.Controls.TextBox ||
                    obj is System.Windows.Controls.Primitives.ScrollBar ||
                    obj is System.Windows.Controls.Primitives.Thumb)
                {
                    return true;
                }
                obj = VisualTreeHelper.GetParent(obj);
            }
            return false;
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
                SetStatus(Localization.Get("AutostartFailed"), isError: true);
                return;
            }

            _settings.AutoStart = enabled;
            SaveSettings();
        }

        private void OnLanguageChanged(object? sender, RoutedEventArgs e)
        {
            if (_initializing) return;

            string language = EnglishOption.IsChecked == true ? "en" : "id";
            if (language == _settings.Language) return;

            _settings.Language = language;
            Localization.Apply(language);
            SaveSettings();
        }

        private void SaveSettings()
        {
            bool saved = SettingsStore.Save(_settings);
            SetStatus(
                Localization.Get(saved ? "Saved" : "SaveFailed"),
                isError: !saved);

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

        private void OnDeactivated(object? sender, EventArgs e)
        {
            if (!_isReady || _isClosing || !IsVisible) return;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

            // Batalkan recording saat window kehilangan fokus
            if (_isRecordingHotkey)
            {
                _isRecordingHotkey = false;
                HotkeyLabel.Text = FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
                HotkeyRecorder.BorderBrush = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3E));
            }

            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
            {
                if (_isClosing || !IsVisible || IsActive) return;
                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

                RequestClose();
            }));
        }

        private void RequestClose()
        {
            if (_isClosing) return;

            _isClosing = true;

            if (!IsLoaded ||
                Dispatcher.HasShutdownStarted ||
                Dispatcher.HasShutdownFinished)
            {
                return;
            }

            try
            {
                Close();
            }
            catch (InvalidOperationException)
            {
                // The window is already in its closing sequence.
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_isRecordingHotkey)
            {
                HandleHotkeyRecording(e);
                return;
            }

            if (e.Key != Key.Escape) return;

            CloseFromUser();
            e.Handled = true;
        }

        private void HandleHotkeyRecording(KeyEventArgs e)
        {
            // Escape = batalkan
            if (e.Key == Key.Escape)
            {
                _isRecordingHotkey = false;
                HotkeyLabel.Text = FormatHotkey(_settings.HotkeyModifiers, _settings.HotkeyKey);
                HotkeyRecorder.BorderBrush = new SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3E));
                e.Handled = true;
                return;
            }

            // Abaikan jika hanya modifier saja yang ditekan
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key is Key.LeftCtrl or Key.RightCtrl or
                       Key.LeftShift or Key.RightShift or
                       Key.LeftAlt or Key.RightAlt or
                       Key.LWin or Key.RWin)
            {
                return;
            }

            // Hitung modifier
            uint mods = 0;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) mods |= 2;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))   mods |= 4;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))     mods |= 1;
            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Windows)) mods |= 8;

            // Butuh setidaknya satu modifier
            if (mods == 0)
            {
                e.Handled = true;
                return;
            }

            uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);

            _settings.HotkeyModifiers = mods;
            _settings.HotkeyKey = vk;

            _isRecordingHotkey = false;
            HotkeyLabel.Text = FormatHotkey(mods, vk);
            HotkeyRecorder.BorderBrush = new SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x3A, 0x3A, 0x3E));

            SaveSettings();
            e.Handled = true;
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
        }

        /// <summary>
        /// Format kombinasi hotkey menjadi string yang mudah dibaca.
        /// </summary>
        internal static string FormatHotkey(uint modifiers, uint vk)
        {
            var parts = new List<string>();
            if ((modifiers & 8) != 0) parts.Add("Win");
            if ((modifiers & 2) != 0) parts.Add("Ctrl");
            if ((modifiers & 4) != 0) parts.Add("Shift");
            if ((modifiers & 1) != 0) parts.Add("Alt");

            if (vk != 0)
            {
                var key = KeyInterop.KeyFromVirtualKey((int)vk);
                // Sederhanakan nama key agar lebih mudah dibaca
                string keyName = key switch
                {
                    Key.Space => "Space",
                    Key.Return => "Enter",
                    Key.Back => "Backspace",
                    _ => key.ToString()
                };
                parts.Add(keyName);
            }

            return parts.Count > 0 ? string.Join("+", parts) : "—";
        }

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
