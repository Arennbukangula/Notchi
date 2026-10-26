using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using WindowsNotch.Conversion;

namespace WindowsNotch
{
    public enum ConversionQueueStatus
    {
        Pending,
        Converting,
        Done,
        DoneWithWarnings,
        Failed,
        Scanned,
        Cancelled
    }

    public sealed class ConversionQueueItem : INotifyPropertyChanged
    {
        private static readonly Brush PendingBrush = FrozenBrush(0x6E, 0x6E, 0x73);
        private static readonly Brush ActiveBrush = FrozenBrush(0x8F, 0xD3, 0xFF);
        private static readonly Brush SuccessBrush = FrozenBrush(0x4C, 0xD9, 0x64);
        private static readonly Brush WarningBrush = FrozenBrush(0xFF, 0xB0, 0x4C);
        private static readonly Brush ErrorBrush = FrozenBrush(0xFF, 0x6B, 0x6B);

        private ConversionQueueStatus _status = ConversionQueueStatus.Pending;
        private string? _outputPath;
        private string? _error;
        private IReadOnlyList<string> _warnings = Array.Empty<string>();
        private bool _isRemovable = true;
        private bool _isSelected;
        private bool _isSelectionEnabled = true;

        public ConversionQueueItem(string inputPath, ConversionTarget target)
        {
            InputPath = inputPath;
            Target = target;
            Name = Path.GetFileName(inputPath);
            Location = Path.GetDirectoryName(inputPath) ?? string.Empty;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string InputPath { get; }

        public string Name { get; }

        public string Location { get; }

        public ConversionTarget Target { get; }

        public string TargetLabel => ConversionFormats.GetDisplayName(Target);

        public string SourceLabel =>
            Path.GetExtension(InputPath).TrimStart('.').ToUpperInvariant();

        public string ConversionLabel => $"{SourceLabel} \u2192 {TargetLabel}";

        public bool IsLegacyDoc => ConversionFormats.IsLegacyDoc(InputPath);

        public ConversionQueueStatus Status => _status;

        public string? OutputPath => _outputPath;

        public bool CanOpenOutput =>
            !string.IsNullOrWhiteSpace(_outputPath) && File.Exists(_outputPath);

        public bool CanConvert =>
            _status is ConversionQueueStatus.Pending or
                ConversionQueueStatus.Failed or
                ConversionQueueStatus.Cancelled;

        public bool IsSelectable => CanConvert && _isSelectionEnabled;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                if (value && !IsSelectable) return;

                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public bool IsRemovable => _isRemovable;

        public string StatusText => _status switch
        {
            ConversionQueueStatus.Pending => Localization.Get("ConvertStatusPending"),
            ConversionQueueStatus.Converting =>
                Localization.Format("ConvertingTo", TargetLabel),
            ConversionQueueStatus.Done => Localization.Get("ConvertStatusDone"),
            ConversionQueueStatus.DoneWithWarnings =>
                Localization.Get("ConvertStatusDoneWithWarnings"),
            ConversionQueueStatus.Failed => Localization.Get("ConvertFailed"),
            ConversionQueueStatus.Scanned => Localization.Get("ConvertScannedPdf"),
            ConversionQueueStatus.Cancelled => Localization.Get("ConvertCancelled"),
            _ => string.Empty
        };

        public Brush StatusBrush => _status switch
        {
            ConversionQueueStatus.Converting => ActiveBrush,
            ConversionQueueStatus.Done => SuccessBrush,
            ConversionQueueStatus.DoneWithWarnings => WarningBrush,
            ConversionQueueStatus.Failed or ConversionQueueStatus.Scanned => ErrorBrush,
            _ => PendingBrush
        };

        public string? Detail => _status switch
        {
            ConversionQueueStatus.Done or ConversionQueueStatus.DoneWithWarnings
                when !string.IsNullOrWhiteSpace(_outputPath) =>
                Path.GetFileName(_outputPath!),
            ConversionQueueStatus.Failed when !string.IsNullOrWhiteSpace(_error) =>
                _error!,
            ConversionQueueStatus.Scanned => Localization.Get("ConvertScannedPdf"),
            ConversionQueueStatus.Cancelled => Localization.Get("ConvertCancelled"),
            _ => null
        };

        public string? WarningToolTip
        {
            get
            {
                if (_warnings.Count == 0) return null;

                var messages = new List<string>(_warnings.Count);

                foreach (string warning in _warnings)
                {
                    messages.Add(
                        warning.StartsWith(
                            "legacy-doc-loss:",
                            StringComparison.OrdinalIgnoreCase)
                            ? Localization.Get("ConvertLegacyDocWarning")
                            : warning);
                }

                return string.Join(Environment.NewLine, messages);
            }
        }

        public void SetStatus(
            ConversionQueueStatus status,
            string? outputPath = null,
            string? error = null,
            IReadOnlyList<string>? warnings = null)
        {
            _status = status;
            _outputPath = outputPath;
            _error = error;
            _warnings = warnings ?? Array.Empty<string>();

            if (_isSelected &&
                status is (ConversionQueueStatus.Done or
                    ConversionQueueStatus.DoneWithWarnings or
                    ConversionQueueStatus.Scanned))
            {
                _isSelected = false;
                OnPropertyChanged(nameof(IsSelected));
            }

            OnPropertyChanged(nameof(Status));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(StatusBrush));
            OnPropertyChanged(nameof(OutputPath));
            OnPropertyChanged(nameof(CanOpenOutput));
            OnPropertyChanged(nameof(CanConvert));
            OnPropertyChanged(nameof(IsSelectable));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(WarningToolTip));
        }

        public void SetRemovable(bool isRemovable)
        {
            if (_isRemovable == isRemovable) return;

            _isRemovable = isRemovable;
            OnPropertyChanged(nameof(IsRemovable));
        }

        public void SetSelectionEnabled(bool isEnabled)
        {
            if (_isSelectionEnabled == isEnabled) return;

            _isSelectionEnabled = isEnabled;
            OnPropertyChanged(nameof(IsSelectable));
        }

        public void RefreshLocalization()
        {
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(Detail));
            OnPropertyChanged(nameof(WarningToolTip));
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private static Brush FrozenBrush(byte r, byte g, byte b)
        {
            var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
            brush.Freeze();
            return brush;
        }
    }
}
