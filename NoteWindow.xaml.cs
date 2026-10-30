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
        private bool _isResizing;
        private Point _resizeStart;
        private Size _resizeStartSize;

        public NoteWindow()
        {
            InitializeComponent();

            NotePath = ResolveNotePath();
            MigrateLegacyNote(NotePath);
            HeaderTitle.ToolTip = NotePath;

            _saveTimer.Tick += OnSaveTimerTick;
            NoteTextBox.TextChanged += OnNoteTextChanged;
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

            ResizeGrip.MouseLeftButtonDown += OnResizeGripMouseLeftButtonDown;
            ResizeGrip.MouseMove += OnResizeGripMouseMove;
            ResizeGrip.MouseLeftButtonUp += OnResizeGripMouseLeftButtonUp;
            ResizeGrip.LostMouseCapture += OnResizeGripLostMouseCapture;

            Activated += (s, e) => _isReady = true;
            Deactivated += OnDeactivated;
            Closed += (s, e) =>
            {
                _isClosing = true;
                _isReady = false;
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

        private void OnResizeGripMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left) return;

            _isResizing = true;
            _resizeStart = e.GetPosition(this);
            _resizeStartSize = new Size(ActualWidth, ActualHeight);
            ResizeGrip.CaptureMouse();
            e.Handled = true;
        }

        private void OnResizeGripMouseMove(object sender, MouseEventArgs e)
        {
            if (!_isResizing || !ResizeGrip.IsMouseCaptured) return;

            Point current = e.GetPosition(this);
            double width = _resizeStartSize.Width + (current.X - _resizeStart.X);
            double height = _resizeStartSize.Height + (current.Y - _resizeStart.Y);

            Width = Math.Max(MinWidth, width);
            Height = Math.Max(MinHeight, height);
        }

        private void OnResizeGripMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isResizing) return;

            _isResizing = false;
            ResizeGrip.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnResizeGripLostMouseCapture(object sender, MouseEventArgs e)
        {
            _isResizing = false;
        }

        private void OnDeactivated(object? sender, EventArgs e)
        {
            if (!_isReady || _isClosing || !IsVisible) return;
            if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;

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

        private void LoadNote()
        {
            _isLoading = true;

            try
            {
                if (File.Exists(NotePath))
                {
                    NoteTextBox.Text = File.ReadAllText(NotePath, Encoding.UTF8);
                    SetStatus(Localization.Get("Saved"), isError: false);
                }
                else
                {
                    SetStatus(Localization.Get("NewNote"), isError: false);
                }
            }
            catch
            {
                SetStatus(Localization.Get("LoadFailed"), isError: true);
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
            SetStatus(Localization.Get("Saving"), isError: false);

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
                SetStatus(Localization.Get("Saved"), isError: false);
            }
            catch
            {
                SetStatus(Localization.Get("SaveFailed"), isError: true);
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

        private static string ResolveNotePath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Notchi",
                NoteFileName);
        }

        private static void MigrateLegacyNote(string notePath)
        {
            try
            {
                if (File.Exists(notePath)) return;

                string? legacyPath = FindLegacyNotePath();
                if (legacyPath == null) return;

                string? directory = Path.GetDirectoryName(notePath);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                try
                {
                    File.Move(legacyPath, notePath);
                }
                catch
                {
                    File.Copy(legacyPath, notePath);
                    try
                    {
                        File.Delete(legacyPath);
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
        }

        private static string? FindLegacyNotePath()
        {
            string downloadsPath = GetDownloadsPath();
            string legacyPath = Path.Combine(downloadsPath, NoteFileName);

            if (File.Exists(legacyPath)) return legacyPath;

            string fallbackPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Downloads",
                NoteFileName);

            if (!string.Equals(fallbackPath, legacyPath,
                    StringComparison.OrdinalIgnoreCase) &&
                File.Exists(fallbackPath))
            {
                return fallbackPath;
            }

            return null;
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
