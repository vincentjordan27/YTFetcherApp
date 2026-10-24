# YT Fetcher Workspace

Workspace ini menyatukan dua repository untuk **YT Fetcher**: aplikasi yang menemukan, melacak, dan memperbarui video YouTube paling banyak ditonton berdasarkan niche. Admin dapat mengelola keyword dan channel, menjalankan atau meninjau backfill, memantau penggunaan kuota YouTube API, serta melihat peringkat dan riwayat snapshot video.

![Diagram alur sistem YT Fetcher](be/YTNicheFetcher/docs/assets/yt-fetcher-system-flow.png)

## Repository

| Bagian | Lokasi | Peran |
| --- | --- | --- |
| Frontend | [`fe/YTNicheFetcher-FE`](fe/YTNicheFetcher-FE/README.md) | Vue 3 admin interface untuk autentikasi opsional, review channel, pengelolaan niche, backfill, kuota, ranking video, dan riwayat snapshot. |
| Backend | [`be/YTNicheFetcher`](be/YTNicheFetcher/README.md) | ASP.NET Core API dengan PostgreSQL, proses terjadwal, integrasi YouTube, dan autentikasi Firebase opsional. |

Setiap folder tetap merupakan repository Git mandiri dan memiliki dokumentasi, konfigurasi, dependensi, serta workflow CI sendiri.

## Arsitektur Singkat

```text
Admin (Vue frontend)
  -> ASP.NET Core API
  -> PostgreSQL
  -> YouTube Data API

Niche + keyword
  -> discovery channel
  -> review / approval admin
  -> video backfill yang sadar kuota
  -> polling statistik harian + snapshot
  -> ranking video aktif dan referensi
```

Backend menjalankan discovery serta pemrosesan harian melalui Hangfire. Frontend menampilkan hasil operasionalnya dan dapat memakai Google sign-in berbasis Firebase bila autentikasi diaktifkan pada backend.

## Teknologi

- **Frontend:** Vue 3, TypeScript, Vite, Vue Router, SCSS, dan pnpm.
- **Backend:** .NET 10 / ASP.NET Core, Entity Framework Core, Npgsql, PostgreSQL 17, Hangfire, Serilog, dan xUnit.
- **Infrastruktur lokal:** Docker Compose untuk PostgreSQL.

## Mulai Development Lokal

1. Siapkan dan jalankan backend terlebih dahulu. Ikuti konfigurasi database serta `appsettings` pada [README backend](be/YTNicheFetcher/README.md).

   ```powershell
   Set-Location be/YTNicheFetcher
   dotnet run --project .\src\YtFetcher.Api --launch-profile http
   ```

2. Siapkan frontend dan atur `VITE_API_BASE_URL` pada `fe/YTNicheFetcher-FE/.env.local` agar sesuai dengan URL backend yang sedang berjalan.

   ```powershell
   Set-Location ../../fe/YTNicheFetcher-FE
   pnpm install
   Copy-Item .env.example .env.local
   pnpm dev
   ```

3. Bila autentikasi diaktifkan, konfigurasikan Firebase pada kedua aplikasi menurut README masing-masing. Jangan commit kredensial atau file konfigurasi lokal.

## Catatan Konfigurasi Penting

Dokumentasi frontend saat ini menyebut API lokal di `https://localhost:10002`, sedangkan dokumentasi backend menjalankan profil `http` pada `http://localhost:10001`. Pilih satu alamat backend yang benar untuk environment Anda, lalu samakan `VITE_API_BASE_URL` sebelum menjalankan frontend. Rincian setup dan kualitas ada di README tiap repository.

## Perintah Kualitas

Jalankan dari repository terkait:

```powershell
# Frontend
Set-Location fe/YTNicheFetcher-FE
pnpm typecheck
pnpm build

# Backend
Set-Location ../../be/YTNicheFetcher
dotnet test
```
