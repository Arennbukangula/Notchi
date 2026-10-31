using System;
using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace WindowsNotch
{
    public partial class CalendarNoteWindow : Window
    {
        public event EventHandler<string>? Saved;
        public event EventHandler? Deleted;

        private readonly DateTime _date;

        public CalendarNoteWindow(DateTime date, string? initialNote)
        {
            InitializeComponent();
            _date = date;

            CalendarNotePopupDateText.Text = date.ToString("dd MMMM yyyy", CultureInfo.CurrentCulture);
            CalendarNoteInput.Text = initialNote ?? string.Empty;
            CalendarNoteDeleteButton.Visibility = string.IsNullOrEmpty(initialNote) ? Visibility.Collapsed : Visibility.Visible;

            CalendarNoteCloseButton.Click += (s, e) => Close();

            CalendarNoteSaveButton.Click += (s, e) => DoSave();
            CalendarNoteDeleteButton.Click += (s, e) => DoDelete();

            CalendarNoteInput.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter)
                {
                    DoSave();
                    e.Handled = true;
                }
                else if (e.Key == Key.Escape)
                {
                    Close();
                    e.Handled = true;
                }
            };

            Loaded += (s, e) =>
            {
                CalendarNoteInput.Focus();
                CalendarNoteInput.SelectAll();
            };
        }

        private void DoSave()
        {
            Saved?.Invoke(this, CalendarNoteInput.Text.Trim());
            Close();
        }

        private void DoDelete()
        {
            Deleted?.Invoke(this, EventArgs.Empty);
            Close();
        }
    }
}
