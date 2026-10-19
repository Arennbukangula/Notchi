using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace WindowsNotch
{
    public partial class MainWindow : Window
    {
        private DispatcherTimer _timer;

        public MainWindow()
        {
            InitializeComponent();
            PositionAtTopCenter();
            StartClock();

            // Event saat kursor mouse masuk/keluar area Notch
            NotchBorder.MouseEnter += (s, e) => ExpandNotch();
            NotchBorder.MouseLeave += (s, e) => CollapseNotch();

            // KLIK KANAN di area Notch untuk menutup aplikasi
            NotchBorder.MouseRightButtonDown += (s, e) => Application.Current.Shutdown();
        }

        // Memosisikan jendela tepat di tengah atas layar
        private void PositionAtTopCenter()
        {
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            this.Left = (screenWidth / 2) - (this.Width / 2);
            this.Top = 0;
        }

        // Menjalankan jam real-time
        private void StartClock()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromSeconds(1);
            _timer.Tick += (s, e) =>
            {
                NotchText.Text = DateTime.Now.ToString("HH:mm:ss");
            };
            _timer.Start();
        }

        // Saat kursor mouse masuk ke area Notch (Mekar)
        private void ExpandNotch()
        {
            this.Height = 100;
            this.Width = 320;
            PositionAtTopCenter();
        }

        // Saat kursor mouse keluar dari area Notch (Mengecil)
        private void CollapseNotch()
        {
            this.Height = 35;
            this.Width = 180;
            PositionAtTopCenter();
        }
    }
}