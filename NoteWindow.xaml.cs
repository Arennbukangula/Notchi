using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WindowsNotch
{
    public partial class NoteWindow : Window
    {
        private const string NoteFileName = "Notchi Note.txt";

        private static readonly Guid DownloadsFolderId =
            new("374DE290-123F-4565-9164-39C4925E467B");

        private static readonly Encoding Utf8NoBom = new UTF8Encoding(false);
        private static readonly Brush MutedBrush = FrozenBrush(0x6E, 0x6E, 0x73);
        private static readonly Brush ErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);

        private readonly DispatcherTimer _saveTimer = new()
        {
            Interval = TimeSpan.FromMilliseconds(400)
        };

        private bool _isLoading;
        private bool _isReady;
        private bool _isClosing;
        private bool _userEdited;

        public NoteWindow()
        {
            InitializeComponent();

            NotePath = ResolveNotePath();
            HeaderTitle.ToolTip = NotePath;

            _saveTimer.Tick += OnSaveTimerTick;
            NoteTextBox.TextChanged += OnNoteTextChanged;
            CloseButton.Click += (s, e) => CloseFromUser();

            Activated += (s, e) => _isReady = true;
            Deactivated += (s, e) =>
            {
                if (_isReady) RequestClose();
            };

            Loaded += (s, e) =>
            {
                Activate();
                NoteTextBox.Focus();
                Keyboard.Focus(NoteTextBox);
                NoteTextBox.CaretIndex = NoteTextBox.Text.Length;
            };

            PreviewKeyDown += OnPreviewKeyDown;
            Closing += OnClosing;

            LoadNote();
        }

        public string NotePath { get; }

        public bool ClosedByUser { get; private set; }

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

        private void LoadNote()
        {
            _isLoading = true;

            try
            {
                if (File.Exists(NotePath))
                {
                    NoteTextBox.Text = File.ReadAllText(NotePath, Encoding.UTF8);
                    SetStatus("Tersimpan", isError: false);
                }
                else
                {
                    SetStatus("Catatan baru", isError: false);
                }
            }
            catch
            {
                SetStatus("Gagal memuat", isError: true);
            }
            finally
            {
                _isLoading = false;
                UpdatePlaceholder();
            }
        }

        private void OnNoteTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (_isLoading) return;

            _userEdited = true;
            UpdatePlaceholder();
            SetStatus("Menyimpan...", isError: false);

            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void OnSaveTimerTick(object? sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveNote();
        }

        private void SaveNote()
        {
            if (!_userEdited) return;

            try
            {
                string? directory = Path.GetDirectoryName(NotePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(NotePath, NoteTextBox.Text, Utf8NoBom);
                _userEdited = false;
                SetStatus("Tersimpan", isError: false);
            }
            catch
            {
                SetStatus("Gagal menyimpan", isError: true);
            }
        }

        private void UpdatePlaceholder()
        {
            PlaceholderText.Visibility = string.IsNullOrEmpty(NoteTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? ErrorBrush : MutedBrush;
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseFromUser();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.S && (Keyboard.Modifiers & ModifierKeys.Control) != 0)
            {
                _saveTimer.Stop();
                SaveNote();
                e.Handled = true;
            }
        }

        private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
        {
            _isClosing = true;
            _saveTimer.Stop();
            SaveNote();
        }

        private static string ResolveNotePath()
        {
            return Path.Combine(GetDownloadsPath(), NoteFileName);
        }

        private static string GetDownloadsPath()
        {
            IntPtr pathPtr = IntPtr.Zero;

            try
            {
                var folderId = DownloadsFolderId;
                if (SHGetKnownFolderPath(ref folderId, 0, IntPtr.Zero, out pathPtr) == 0 &&
                    pathPtr != IntPtr.Zero)
                {
                    string? path = Marshal.PtrToStringUni(pathPtr);
                    if (!string.IsNullOrWhiteSpace(path))
                    {
                        return path;
                    }
                }
            }
            catch
            {
                // Fallback ke lokasi default di bawah
            }
            finally
            {
                if (pathPtr != IntPtr.Zero)
                {
                    Marshal.FreeCoTaskMem(pathPtr);
                }
            }

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads");
        }

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }

        [DllImport("shell32.dll")]
        private static extern int SHGetKnownFolderPath(
            ref Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
    }
}
