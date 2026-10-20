using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace WindowsNotch
{
    public sealed record DroppedFileItem(
        string Path,
        string Name,
        string Location,
        bool IsDirectory);

    public partial class DropShelfWindow : Window
    {
        public const int MaxItems = 50;
        private const int MaxOpenAllItems = 10;

        private static readonly Brush MutedBrush = FrozenBrush(0x6E, 0x6E, 0x73);
        private static readonly Brush ErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);

        private readonly List<DroppedFileItem> _items = new();
        private bool _isClosing;

        public DropShelfWindow(IEnumerable<string> paths)
        {
            InitializeComponent();

            AddPaths(paths);

            CloseButton.Click += (s, e) => RequestClose();
            OpenAllButton.Click += (s, e) => OpenAll();
            ClearButton.Click += (s, e) => ClearItems();

            Closed += (s, e) => _isClosing = true;

            PreviewKeyDown += OnPreviewKeyDown;
            Closing += (s, e) => _isClosing = true;

            Loaded += (s, e) => Activate();
        }

        public event EventHandler? ItemsChanged;

        public IReadOnlyList<string> Paths =>
            _items.Select(item => item.Path).ToList();

        public int AddPaths(IEnumerable<string> paths)
        {
            int added = 0;

            foreach (string? path in paths)
            {
                if (_items.Count >= MaxItems) break;
                if (!TryCreateItem(path, out var item)) continue;
                if (_items.Any(existing =>
                        string.Equals(existing.Path, item.Path,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                _items.Add(item);
                added++;
            }

            if (added > 0 || _items.Count == 0)
            {
                RefreshList();
            }

            if (added > 0)
            {
                ItemsChanged?.Invoke(this, EventArgs.Empty);
            }

            return added;
        }

        private void RefreshList()
        {
            ItemsList.ItemsSource = null;
            ItemsList.ItemsSource = _items;

            FileCountText.Text = _items.Count == 1
                ? "1 item"
                : $"{_items.Count} item";

            bool hasItems = _items.Count > 0;
            ItemsScroll.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            OpenAllButton.IsEnabled = hasItems;
            ClearButton.IsEnabled = hasItems;

            if (hasItems && string.IsNullOrEmpty(StatusText.Text))
            {
                SetStatus("Lepas file lagi ke notch untuk menambah", isError: false);
            }

            Height = Math.Clamp(130 + _items.Count * 42, 170, 340);
        }

        private static bool TryCreateItem(string? path, out DroppedFileItem item)
        {
            item = null!;

            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string fullPath = Path.GetFullPath(path);
                bool isDirectory = Directory.Exists(fullPath);

                if (!isDirectory && !File.Exists(fullPath)) return false;

                string name = isDirectory
                    ? new DirectoryInfo(fullPath).Name
                    : Path.GetFileName(fullPath);

                if (string.IsNullOrWhiteSpace(name)) name = fullPath;

                string location = isDirectory
                    ? Directory.GetParent(fullPath)?.FullName ?? fullPath
                    : Path.GetDirectoryName(fullPath) ?? fullPath;

                item = new DroppedFileItem(fullPath, name, location, isDirectory);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void OnOpenItemClick(object sender, RoutedEventArgs e)
        {
            if (GetItem(sender) is not { } item) return;

            if (!TryOpen(item.Path))
            {
                SetStatus("Gagal membuka file", isError: true);
            }
        }

        private void OnRevealItemClick(object sender, RoutedEventArgs e)
        {
            if (GetItem(sender) is not { } item) return;

            Reveal(item);
        }

        private void OnCopyItemClick(object sender, RoutedEventArgs e)
        {
            if (GetItem(sender) is not { } item) return;

            try
            {
                Clipboard.SetText(item.Path);
                SetStatus("Path disalin", isError: false);
            }
            catch
            {
                SetStatus("Gagal menyalin path", isError: true);
            }
        }

        private void OpenAll()
        {
            int opened = 0;
            int failed = 0;

            foreach (var item in _items.ToList())
            {
                if (opened >= MaxOpenAllItems) break;

                if (TryOpen(item.Path)) opened++;
                else failed++;
            }

            if (opened == 0)
            {
                SetStatus("Tidak ada file yang bisa dibuka", isError: true);
            }
            else if (failed > 0)
            {
                SetStatus("Sebagian file gagal dibuka", isError: true);
            }
            else if (_items.Count > MaxOpenAllItems)
            {
                SetStatus($"Maksimal {MaxOpenAllItems} file dibuka sekaligus",
                    isError: true);
            }
            else
            {
                SetStatus("", isError: false);
            }
        }

        private void ClearItems()
        {
            _items.Clear();
            RefreshList();
            SetStatus("", isError: false);
            ItemsChanged?.Invoke(this, EventArgs.Empty);
        }

        private static DroppedFileItem? GetItem(object sender)
        {
            return (sender as FrameworkElement)?.Tag as DroppedFileItem;
        }

        private static bool TryOpen(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }

        private void Reveal(DroppedFileItem item)
        {
            try
            {
                var info = new ProcessStartInfo("explorer.exe",
                    item.IsDirectory
                        ? $"\"{item.Path}\""
                        : $"/select,\"{item.Path}\"")
                {
                    UseShellExecute = true
                };

                Process.Start(info);
            }
            catch
            {
                SetStatus("Gagal membuka Explorer", isError: true);
            }
        }

        private void SetStatus(string text, bool isError)
        {
            StatusText.Text = text;
            StatusText.Foreground = isError ? ErrorBrush : MutedBrush;
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
            if (e.Key != Key.Escape) return;

            RequestClose();
            e.Handled = true;
        }

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
