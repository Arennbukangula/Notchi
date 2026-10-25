using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace WindowsNotch
{
    /// <summary>
    /// Mendaftarkan dan membatalkan global hotkey sistem menggunakan Win32 API.
    /// </summary>
    public sealed class GlobalHotkeyService : IDisposable
    {
        private const int WmHotkey = 0x0312;
        private const int HotkeyId = 0xBEEF;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        private HwndSource? _source;
        private bool _registered;
        private bool _disposed;

        public event EventHandler? HotkeyPressed;

        /// <summary>
        /// Inisialisasi service dan mulai mendengarkan pesan WM_HOTKEY.
        /// </summary>
        public void Initialize(Window window)
        {
            if (_disposed) return;

            var helper = new WindowInteropHelper(window);
            _source = HwndSource.FromHwnd(helper.Handle);
            _source?.AddHook(WndProc);
        }

        /// <summary>
        /// Daftarkan hotkey baru. Jika sudah ada hotkey sebelumnya, batalkan dulu.
        /// </summary>
        /// <param name="modifiers">Kombinasi modifier (MOD_ALT=1, MOD_CTRL=2, MOD_SHIFT=4, MOD_WIN=8)</param>
        /// <param name="virtualKey">Virtual-key code</param>
        /// <returns>True jika berhasil didaftarkan.</returns>
        public bool Register(uint modifiers, uint virtualKey)
        {
            if (_disposed || _source == null) return false;

            Unregister();

            if (virtualKey == 0) return false;

            // MOD_NOREPEAT (0x4000) mencegah hotkey terpicu berulang saat ditahan
            bool ok = RegisterHotKey(_source.Handle, HotkeyId, modifiers | 0x4000, virtualKey);
            _registered = ok;
            return ok;
        }

        /// <summary>
        /// Batalkan registrasi hotkey aktif.
        /// </summary>
        public void Unregister()
        {
            if (_registered && _source != null)
            {
                UnregisterHotKey(_source.Handle, HotkeyId);
                _registered = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Unregister();
            _source?.RemoveHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WmHotkey && wParam.ToInt32() == HotkeyId)
            {
                HotkeyPressed?.Invoke(this, EventArgs.Empty);
                handled = true;
            }

            return IntPtr.Zero;
        }
    }
}
