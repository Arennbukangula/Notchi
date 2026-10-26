using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using WindowsNotch.Conversion;

namespace WindowsNotch
{
    public partial class ConversionWindow : Window
    {
        public const int MaxItems = 50;

        private static readonly Brush MutedBrush = FrozenBrush(0x6E, 0x6E, 0x73);
        private static readonly Brush ErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);

        private readonly ObservableCollection<ConversionQueueItem> _items = new();
        private bool _isRunning;
        private bool _isClosing;
        private CancellationTokenSource? _conversionCts;
        private string? _statusKey;
        private object[] _statusArgs = Array.Empty<object>();
        private bool _statusIsError;

        public ConversionWindow()
        {
            InitializeComponent();

            QueueList.ItemsSource = _items;

            CloseButton.Click += (s, e) => CloseFromUser();
            ChooseFilesButton.Click += (s, e) => ChooseFiles();
            ConvertSelectedButton.Click += async (s, e) => await ConvertSelectedAsync();
            ConvertAllButton.Click += async (s, e) => await ConvertAllAsync();
            CancelButton.Click += (s, e) => _conversionCts?.Cancel();
            ClearButton.Click += (s, e) => ClearItems();

            PreviewKeyDown += OnPreviewKeyDown;
            Closing += (s, e) =>
            {
                _isClosing = true;
                _conversionCts?.Cancel();
            };
            Closed += (s, e) =>
            {
                _isClosing = true;
                _conversionCts?.Cancel();
                Localization.LanguageChanged -= OnLanguageChanged;

                foreach (var item in _items)
                {
                    item.PropertyChanged -= OnItemPropertyChanged;
                }
            };

            Localization.LanguageChanged += OnLanguageChanged;
            Loaded += (s, e) => Activate();

            RefreshState();
        }

        public bool ClosedByUser { get; private set; }

        private void CloseFromUser()
        {
            ClosedByUser = true;
            RequestClose();
        }

        public void AddDroppedFiles(IEnumerable<string> paths)
        {
            AddFiles(paths);
        }

        private void ChooseFiles()
        {
            if (_isRunning) return;

            var dialog = new OpenFileDialog
            {
                Multiselect = true,
                Filter = Localization.Get("ConvertFileFilter"),
                Title = Localization.Get("ConvertChooseFiles")
            };

            if (dialog.ShowDialog(this) == true)
            {
                AddFiles(dialog.FileNames);
            }
        }

        private void AddFiles(IEnumerable<string> paths)
        {
            if (_isRunning)
            {
                SetStatus("ConvertBusy", false);
                return;
            }

            int added = AddPaths(paths);

            if (added > 0)
            {
                SetStatus("ConvertAddedFiles", false, added);
            }
            else
            {
                SetStatus("ConvertNoSupportedFiles", true);
            }

            RefreshState();
        }

        private int AddPaths(IEnumerable<string> paths)
        {
            int added = 0;

            foreach (string? path in paths)
            {
                if (_items.Count >= MaxItems) break;
                if (!TryCreateItem(path, out var item)) continue;
                if (_items.Any(existing =>
                        string.Equals(existing.InputPath, item.InputPath,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                item.PropertyChanged += OnItemPropertyChanged;
                _items.Add(item);
                added++;
            }

            return added;
        }

        private static bool TryCreateItem(
            string? path,
            out ConversionQueueItem item)
        {
            item = null!;

            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                string fullPath = Path.GetFullPath(path);
                if (!File.Exists(fullPath)) return false;
                if (!ConversionFormats.TryGetTarget(fullPath, out var target)) return false;

                item = new ConversionQueueItem(fullPath, target);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private async Task ConvertSelectedAsync()
        {
            if (_isRunning) return;

            var selected = _items
                .Where(item => item.IsSelected && item.CanConvert)
                .ToList();

            if (selected.Count == 0)
            {
                SetStatus("ConvertNoSelection", true);
                return;
            }

            await ConvertItemsAsync(selected);
        }

        private async Task ConvertAllAsync()
        {
            if (_isRunning) return;

            var pending = _items.Where(item => item.CanConvert).ToList();

            if (pending.Count == 0)
            {
                SetStatus("ConvertNothingToDo", false);
                return;
            }

            await ConvertItemsAsync(pending);
        }

        private async Task ConvertItemsAsync(
            IReadOnlyList<ConversionQueueItem> pending)
        {
            using var cts = new CancellationTokenSource();
            _isRunning = true;
            _conversionCts = cts;
            bool cancelled = false;

            try
            {
                RefreshState();
                SetStatus("ConvertStarted", false);

                foreach (var item in pending)
                {
                    if (cts.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    if (!ConversionPaths.TryCreateOutputPath(
                            item.InputPath,
                            item.Target,
                            out string outputPath))
                    {
                        item.SetStatus(
                            ConversionQueueStatus.Failed,
                            null,
                            Localization.Get("ConvertFailed"));
                        RefreshState();
                        continue;
                    }

                    item.SetStatus(ConversionQueueStatus.Converting);
                    RefreshState();
                    SetStatus("ConvertingTo", false, item.TargetLabel);

                    ConversionResult result;

                    try
                    {
                        var client = new ConversionClient();
                        result = await client.ConvertAsync(
                            item.InputPath,
                            item.Target,
                            outputPath,
                            cts.Token);
                    }
                    catch (OperationCanceledException)
                    {
                        result = new ConversionResult(
                            false,
                            null,
                            "Cancelled.",
                            Array.Empty<string>(),
                            false,
                            true,
                            false);
                    }
                    catch (Exception exception)
                    {
                        CrashLogger.Log("ConversionWindow", exception);
                        result = new ConversionResult(
                            false,
                            null,
                            exception.Message,
                            Array.Empty<string>(),
                            false,
                            false,
                            false);
                    }

                    if (_isClosing ||
                        Dispatcher.HasShutdownStarted ||
                        Dispatcher.HasShutdownFinished)
                    {
                        return;
                    }

                    if (result.Cancelled)
                    {
                        item.SetStatus(ConversionQueueStatus.Cancelled);
                        cancelled = true;
                        break;
                    }

                    if (result.TimedOut)
                    {
                        item.SetStatus(
                            ConversionQueueStatus.Failed,
                            null,
                            Localization.Get("ConvertTimeout"),
                            result.Warnings);
                        RefreshState();
                        continue;
                    }

                    if (result.IsScannedPdf)
                    {
                        item.SetStatus(
                            ConversionQueueStatus.Scanned,
                            null,
                            null,
                            result.Warnings);
                        RefreshState();
                        continue;
                    }

                    if (result.Success && result.OutputPath is { } produced)
                    {
                        bool hasLegacyWarning = result.Warnings.Any(warning =>
                            warning.StartsWith(
                                "legacy-doc-",
                                StringComparison.OrdinalIgnoreCase));

                        item.SetStatus(
                            hasLegacyWarning
                                ? ConversionQueueStatus.DoneWithWarnings
                                : ConversionQueueStatus.Done,
                            produced,
                            null,
                            result.Warnings);

                        SetStatus(
                            "ConvertedTo",
                            false,
                            Path.GetFileName(produced));

                        RefreshState();
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(result.Error))
                    {
                        CrashLogger.Log(
                            "ConversionWindow",
                            new InvalidOperationException(result.Error));
                    }

                    item.SetStatus(
                        ConversionQueueStatus.Failed,
                        null,
                        result.Error ?? Localization.Get("ConvertFailed"),
                        result.Warnings);

                    RefreshState();
                }
            }
            catch (Exception exception)
            {
                CrashLogger.Log("ConversionWindow.ConvertItems", exception);

                foreach (var item in _items.Where(item =>
                             item.Status == ConversionQueueStatus.Converting))
                {
                    item.SetStatus(
                        ConversionQueueStatus.Failed,
                        null,
                        Localization.Get("ConvertFailed"));
                }
            }
            finally
            {
                _conversionCts = null;
                _isRunning = false;

                if (!_isClosing)
                {
                    RefreshState();

                    if (cancelled)
                    {
                        SetStatus("ConvertCancelled", false);
                    }
                    else
                    {
                        int done = _items.Count(item =>
                            item.Status is ConversionQueueStatus.Done or
                                ConversionQueueStatus.DoneWithWarnings);
                        int failed = _items.Count(item =>
                            item.Status is ConversionQueueStatus.Failed or
                                ConversionQueueStatus.Scanned);

                        SetStatus("ConvertSummary", failed > 0, done, failed);
                    }
                }
            }
        }

        private void ClearItems()
        {
            if (_isRunning) return;

            foreach (var item in _items)
            {
                item.PropertyChanged -= OnItemPropertyChanged;
            }

            _items.Clear();
            SetStatus(null, false);
            RefreshState();
        }

        private void OnOpenOutputClick(object sender, RoutedEventArgs e)
        {
            if (GetItem(sender) is not { } item) return;
            if (!item.CanOpenOutput) return;

            try
            {
                Process.Start(new ProcessStartInfo(item.OutputPath!)
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                SetStatus("OpenFailed", true);
            }
        }

        private void OnShowOutputClick(object sender, RoutedEventArgs e)
        {
            if (GetItem(sender) is not { } item) return;
            if (!item.CanOpenOutput) return;

            try
            {
                Process.Start(new ProcessStartInfo(
                    "explorer.exe",
                    $"/select,\"{item.OutputPath}\"")
                {
                    UseShellExecute = true
                });
            }
            catch
            {
                SetStatus("RevealFailed", true);
            }
        }

        private void OnRemoveItemClick(object sender, RoutedEventArgs e)
        {
            if (_isRunning) return;
            if (GetItem(sender) is not { } item) return;

            item.PropertyChanged -= OnItemPropertyChanged;
            _items.Remove(item);
            RefreshState();
        }

        private void OnItemPropertyChanged(
            object? sender,
            PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ConversionQueueItem.IsSelected))
            {
                RefreshState();
            }
        }

        private static ConversionQueueItem? GetItem(object sender)
        {
            return (sender as FrameworkElement)?.Tag as ConversionQueueItem;
        }

        private void RefreshState()
        {
            bool hasItems = _items.Count > 0;

            QueueScroll.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            EmptyState.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;

            QueueCountText.Text = _items.Count == 1
                ? Localization.Get("FileCountOne")
                : Localization.Format("FileCountMany", _items.Count);

            ChooseFilesButton.IsEnabled = !_isRunning;
            ClearButton.IsEnabled = !_isRunning && hasItems;
            CancelButton.IsEnabled = _isRunning;
            ConvertSelectedButton.IsEnabled = !_isRunning &&
                _items.Any(item => item.IsSelected && item.CanConvert);
            ConvertAllButton.IsEnabled = !_isRunning &&
                _items.Any(item => item.CanConvert);

            DropZone.Opacity = _isRunning ? 0.5 : 1;

            foreach (var item in _items)
            {
                item.SetRemovable(!_isRunning);
                item.SetSelectionEnabled(!_isRunning);
            }
        }

        private void OnLanguageChanged(object? sender, EventArgs e)
        {
            foreach (var item in _items)
            {
                item.RefreshLocalization();
            }

            ApplyStatus();
            RefreshState();
        }

        private void SetStatus(
            string? key,
            bool isError,
            params object[] args)
        {
            _statusKey = key;
            _statusArgs = args;
            _statusIsError = isError;
            ApplyStatus();
        }

        private void ApplyStatus()
        {
            StatusText.Text = _statusKey is null
                ? string.Empty
                : _statusArgs.Length == 0
                    ? Localization.Get(_statusKey!)
                    : Localization.Format(_statusKey!, _statusArgs);

            StatusText.Foreground = _statusIsError ? ErrorBrush : MutedBrush;
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
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;

            CloseFromUser();
            e.Handled = true;
        }

        private void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = !_isRunning && e.Data.GetDataPresent(DataFormats.FileDrop)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
            e.Handled = true;
        }

        private void OnDrop(object sender, DragEventArgs e)
        {
            if (_isRunning)
            {
                e.Effects = DragDropEffects.None;
                e.Handled = true;
                return;
            }

            if (e.Data.GetData(DataFormats.FileDrop) is string[] paths)
            {
                AddFiles(paths);
            }

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
