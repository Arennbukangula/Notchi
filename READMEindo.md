<p align="center">
  <img src="assets/banner.png" alt="Banner Notchi" width="420" />
</p>

<p align="center">
  <a href="README.md">English</a> | <a href="READMEindo.md">Bahasa Indonesia</a>
</p>

## Ringkasan

Notchi adalah overlay notch native untuk Windows 10 dan Windows 11 yang terinspirasi oleh notch macOS dan Dynamic Island. Aplikasi tetap berada di atas desktop, tampil ringkas saat idle, dan mengembang saat kursor diarahkan untuk menampilkan kontrol media serta informasi sistem. Aplikasi dibangun dengan C#, WPF, dan .NET 8 tanpa Electron sehingga tetap ringan dan responsif.

## Spesifikasi Sistem

| Komponen | Minimum | Disarankan |
| :--- | :--- | :--- |
| Sistem operasi | Windows 10 versi 2004 (build 19041), 64-bit | Windows 11 versi 22H2 atau lebih baru, 64-bit |
| Arsitektur | x64 | x64 |
| Prosesor | Dual-core 1 GHz atau setara | Quad-core 2 GHz atau setara |
| Memori | RAM 4 GB | RAM 8 GB |
| Penyimpanan | Ruang kosong 150 MB | Ruang kosong 250 MB pada SSD |
| Layar | 1280 x 720 | 1920 x 1080 atau lebih tinggi |

Installer rilis bersifat per-user dan tidak memerlukan hak administrator.

## Instalasi

Unduh `NotchiSetup-x64.exe` versi terbaru dari halaman [Releases](https://github.com/risuunava/Notchi/releases/latest), jalankan installer, lalu buka Notchi dari Start menu. Checksum SHA-256 untuk setiap rilis tersedia di catatan rilis.

## Fitur

| Fitur | Deskripsi |
| :--- | :--- |
| Overlay selalu di atas | Menjaga notch tetap terlihat di atas jendela lain tanpa muncul di taskbar. |
| Mode ringkas dan mengembang | Tampil ringkas saat idle dan mengembang dengan animasi halus saat kursor diarahkan. |
| Kontrol media | Terintegrasi dengan Windows System Media Transport Controls untuk judul lagu, artis, album art, aplikasi sumber, waktu berjalan, durasi, seek, play/pause, serta previous/next. |
| Dashboard sistem | Menampilkan jam ringkas yang bersih dengan gradasi halus pada waktu, tanggal, status baterai, serta penggunaan CPU, RAM, dan disk secara langsung. |
| Catatan | Menyediakan jendela catatan khusus dengan penyimpanan otomatis. |
| File drop shelf | Menerima file yang ditarik ke notch dan mendukung membuka file, menampilkan di Explorer, menyalin path, membuka semua item, menghapus item satu per satu, serta membersihkan daftar. |
| Penempatan di layar | Notch dapat digeser ke atas tengah, kiri tengah, atau kanan tengah layar. |
| Posisi tersimpan | Menyimpan anchor terakhir dan memulihkannya saat aplikasi dibuka kembali. |
| Pintasan global | Menyembunyikan atau menampilkan notch dari mana saja menggunakan pintasan sistem yang dapat disesuaikan (default `Ctrl+Shift+N`). |
| Pengaturan | Mengatur metrik sistem yang ditampilkan, bahasa antarmuka (Bahasa Indonesia atau English), pintasan sembunyikan/tampilkan, dan apakah aplikasi berjalan otomatis saat Windows menyala. |
| Desain ringan | Menggunakan rendering native WPF dengan pemakaian CPU idle rendah dan memori privat di bawah 100 MB. |

## Tampilan

Tangkapan layar berikut menunjukkan tampilan ringkas dan mengembang dari setiap bagian utama.

**Menu fitur**

<p align="center">
  <img src="assets/preview/ss-menu-close.png" alt="Menu fitur ringkas" width="380" />
  &nbsp;&nbsp;
  <img src="assets/preview/ss-menu-open.png" alt="Menu fitur mengembang" width="380" />
</p>
<p align="center"><sub>Ringkas dan mengembang</sub></p>

**Pemutar media**

<p align="center">
  <img src="assets/preview/ss-music-close.png" alt="Pemutar media ringkas" width="380" />
  &nbsp;&nbsp;
  <img src="assets/preview/ss-music-open.png" alt="Pemutar media mengembang" width="380" />
</p>
<p align="center"><sub>Ringkas dan mengembang</sub></p>

**Mode sisi**

<p align="center">
  <img src="assets/preview/ss-side-music-close.png" alt="Mode sisi ringkas" height="200" />
  &nbsp;&nbsp;
  <img src="assets/preview/ss-side-music-open.png" alt="Mode sisi mengembang" height="200" />
</p>
<p align="center"><sub>Ringkas dan mengembang</sub></p>

## Pembaruan Terakhir

| Versi | Tanggal Rilis | Status |
| :--- | :--- | :--- |
| 1.1.0 | 25 Oktober 2026 | Stabil terbaru |

Versi 1.1.0 adalah rilis stabil saat ini. Rilis ini menambahkan pintasan global yang dapat disesuaikan untuk menyembunyikan atau menampilkan Notchi (default `Ctrl+Shift+N`), tombol hapus per item pada file drop shelf, dan opsi bahasa Inggris di Pengaturan, serta tampilan jam ringkas yang lebih bersih dengan gradasi halus pada waktu. Catatan rilis selengkapnya tersedia di halaman [Releases](https://github.com/risuunava/Notchi/releases).

## Kontribusi

Kontribusi sangat diterima, termasuk laporan bug, usulan fitur, perbaikan dokumentasi, dan pull request. Sebelum berkontribusi, baca [CONTRIBUTORS.md](CONTRIBUTORS.md) untuk melihat daftar kontributor, peran, dan alur kontribusi. Untuk perubahan besar, buka issue terlebih dahulu agar pendekatannya dapat didiskusikan.

<p align="center">
  <a href="CONTRIBUTORS.md"><img src="https://img.shields.io/badge/Baca-CONTRIBUTORS.md-3D7EFF?style=for-the-badge" alt="Baca CONTRIBUTORS.md" /></a>
  <a href="https://github.com/risuunava/Notchi/issues"><img src="https://img.shields.io/badge/Buka-Issue-181717?style=for-the-badge&logo=github&logoColor=white" alt="Buka issue" /></a>
</p>

## Kontak

Developer: **risuunava**

<p align="center">
  <a href="https://github.com/risuunava"><img src="https://img.shields.io/badge/GitHub-risuunava-181717?style=for-the-badge&logo=github&logoColor=white" alt="GitHub" /></a>
  <a href="mailto:risuunava@gmail.com"><img src="https://img.shields.io/badge/Email-risuunava%40gmail.com-D14836?style=for-the-badge&logo=gmail&logoColor=white" alt="Email" /></a>
  <a href="https://github.com/risuunava/Notchi"><img src="https://img.shields.io/badge/Repository-Notchi-3D7EFF?style=for-the-badge&logo=github&logoColor=white" alt="Repository" /></a>
</p>

## Lisensi

Notchi dirilis di bawah Apache License 2.0. Lihat [LICENSE](LICENSE) untuk detailnya.
