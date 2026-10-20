<p align="center">
  <img src="assets/banner.png" alt="Notchi banner" width="100%" />
</p>

<h1 align="center">Notchi</h1>

<p align="center">
  <a href="README.md">Bahasa Indonesia</a> | <a href="README.en.md">English</a>
</p>

Notchi adalah overlay notch untuk Windows 10/11 yang terinspirasi dari macOS dan Dynamic Island. Aplikasi melayang di atas desktop, mengecil saat idle, dan terbuka saat kursor diarahkan untuk menampilkan kontrol media atau informasi sistem. Dibangun native dengan WPF tanpa Electron, sehingga tetap ringan.

## Fitur v1.0.0

- Kontrol media via Windows SMTC: judul, artis, album art, progres, play/pause, dan skip.
- Dashboard idle berisi jam, tanggal, baterai, serta pemakaian CPU, RAM, dan disk.
- Menu fitur berisi Catatan, Pengaturan, dan Keluar.
- Catatan dengan penyimpanan otomatis.
- File drop shelf untuk membuka file, menampilkan di Explorer, menyalin path, membuka semua, atau membersihkan daftar.
- Geser notch ke mana saja, lalu menempel otomatis ke atas tengah, kiri, atau kanan.
- Posisi terakhir disimpan dan dipulihkan saat aplikasi dibuka.
- Pengaturan untuk mengatur metrik yang tampil dan autostart Windows.
- Ringan, dengan idle CPU sekitar 0,07% dan memori privat di bawah 100 MB.
- Klik kanan untuk keluar.

## Stack

- Bahasa: C# dengan .NET 8.
- UI: WPF dan XAML, animasi QuinticEase.
- Media: Windows System Media Transport Controls.
- Metrik sistem: Win32 interop dan DriveInfo.
- Penyimpanan: System.Text.Json di `%LOCALAPPDATA%\Notchi\settings.json`.
- Autostart: registry HKCU.
- Installer: Inno Setup, per-user.

## Kontribusi

Project ini open for contributor. Laporan bug, ide fitur, perbaikan dokumentasi, dan pull request sangat diterima. Untuk perubahan besar, buka issue terlebih dahulu agar bisa didiskusikan.

## Kontak

- Pemilik: risuunava
- Email: [risuunava@gmail.com](mailto:risuunava@gmail.com)
- GitHub: [@risuunava](https://github.com/risuunava)
- Repository: [github.com/risuunava/Notchi](https://github.com/risuunava/Notchi)

