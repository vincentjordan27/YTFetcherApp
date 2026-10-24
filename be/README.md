# YT Fetcher

YT Fetcher discovers, tracks, and updates the most-viewed YouTube videos by niche. It is intended to collect relevant channels, track short-form video statistics over time, and make the resulting rankings available to an admin interface.

Current scope includes PostgreSQL persistence, structured logging, channel discovery and review, manual channel approval, quota-aware video backfill, daily new-upload monitoring, daily video-stat polling with snapshot history, optional Firebase Google authentication, and the connected Vue admin routes.

## Architecture Flow

![YT Fetcher system flow](docs/assets/yt-fetcher-system-flow.png)

## Project Structure

```text
.
├── .github/workflows/       GitHub Actions backend CI
├── src/
│   ├── YtFetcher.Api/       HTTP host, endpoints, and composition root
│   ├── YtFetcher.Application/ use-case contracts and application records
│   ├── YtFetcher.Core/      domain entities and value types
│   └── YtFetcher.Infrastructure/ PostgreSQL and external integrations
├── tests/YtFetcher.Api.Tests/ API and infrastructure tests
├── migrations/               PostgreSQL bootstrap and manual schema scripts
│   ├── 000_provision_database.sql  One-time PostgreSQL admin bootstrap
│   └── 001_initial_schema.sql      Application-owned baseline schema
├── YtFetcher.sln            Visual Studio solution
└── README.md
```

## Data Flow

```text
Niche and keywords
  -> discover candidate YouTube channels              (scheduled)
  -> admin reviews, approves, rejects, or reopens     (available)
  -> backfill eligible channel videos                 (quota-aware)
  -> poll current video statistics and save snapshots (scheduled)
  -> expose ranked active and reference videos        (available)
```

The API exposes health, channel review, keyword, backfill-run, quota-usage, active-video, and reference-video endpoints. Hangfire hosts discovery and daily processing in the API process.

## Tools

| Tool | Use |
| --- | --- |
| .NET 10 / ASP.NET Core | Backend API |
| Entity Framework Core + Npgsql | PostgreSQL access with entity data annotations |
| PostgreSQL 17 | Local application database |
| Docker Compose | Shared local PostgreSQL instance |
| Serilog | JSON logs to stdout |
| Grafana Cloud Loki | Optional Serilog sink when Grafana credentials are configured |
| xUnit | Backend tests |
| GitHub Actions | Continuous integration |

## Run Locally

Copy `src/YtFetcher.Api/appsettings.Local.json` to `src/YtFetcher.Api/appsettings.json`, then replace all database placeholders with your own local PostgreSQL values. The runtime `appsettings.json` is ignored by Git; keep secrets there. `appsettings.Local.json` remains a credential-free template.

```powershell
dotnet run --project .\src\YtFetcher.Api --launch-profile http
```

Health endpoint: `GET http://localhost:10001/api/health`.

The API can run anonymously or require Firebase Google authentication, selected by the local `Auth:Enabled` setting. In enabled mode, the browser obtains a Firebase ID token and the API verifies it through the Firebase Admin SDK before issuing its own HttpOnly session cookie. The backend needs the Firebase project ID and an Admin SDK service-account credential file; public Firebase Web configuration belongs in the frontend `.env`.

For a Docker Compose production deployment, create the credential file directly on the VPS before starting the API. The downloaded Firebase Admin SDK JSON is never committed or copied into the API image:

```bash
sudo install -d -m 700 /etc/yt-fetcher/secrets
sudo cp "$HOME/firebase-admin-sdk-download.json" \
  /etc/yt-fetcher/secrets/firebase-admin-sdk.json
sudo chmod 600 /etc/yt-fetcher/secrets/firebase-admin-sdk.json
```

Grant the API service access using Compose secrets:

```yaml
services:
  api:
    secrets:
      - source: firebase_admin_sdk
        target: firebase-admin-sdk.json

secrets:
  firebase_admin_sdk:
    file: /etc/yt-fetcher/secrets/firebase-admin-sdk.json
```

The API container receives that file read-only at `/run/secrets/firebase-admin-sdk.json`. Its ignored production
configuration must set `Auth:Firebase:ServiceAccountFilePath` to that container path. Compose refuses to create
the API container when the declared host secret source file is absent.

## Database Schema

Provision the application database once using `migrations/000_provision_database.sql` as a PostgreSQL cluster administrator. Before running it, replace all three dummy values in the file:

| Dummy value | Replace with |
| --- | --- |
| `username_app_fetcher` | The login role used by the API |
| `database_name` | The application database name |
| `replace_with_a_unique_long_password` | A unique, long database password |

The script creates the role and database, changes its `psql` connection to the new database, then enables `pgcrypto`. It is intentionally a one-time bootstrap script: do not run it again against an existing deployment.

PowerShell:

```powershell
Get-Content -Raw .\migrations\000_provision_database.sql | docker exec -i postgres-local psql -v ON_ERROR_STOP=1 -U username_global -d postgres
```

Command Prompt:

```cmd
type migrations\000_provision_database.sql | docker exec -i postgres-local psql -v ON_ERROR_STOP=1 -U username_global -d postgres
```

After that, apply the initial schema while logged in as the application role created by the bootstrap. Replace the three dummy values in `000_provision_database.sql` before its first run, then use those exact application-role and database names below. `username_global` is only the pre-existing PostgreSQL cluster administrator; the bootstrap does not create it. The application role owns the resulting tables, so runtime access needs no broad table grants.

PowerShell:

```powershell
Get-Content -Raw .\migrations\001_initial_schema.sql | docker exec -i postgres-local psql -v ON_ERROR_STOP=1 -U username_app_fetcher -d database_name
```

Command Prompt:

```cmd
type migrations\001_initial_schema.sql | docker exec -i postgres-local psql -v ON_ERROR_STOP=1 -U username_app_fetcher -d database_name
```

`migrations/000_provision_database.sql` is an administrator-only bootstrap script, not part of the application schema chain. Its three-digit prefix deliberately keeps it out of PostgreSQL proof schema discovery. `migrations/001_initial_schema.sql` is the baseline schema for a new provisioned database and runs as the new application role. Later schema changes use sequential three-digit SQL filenames; the API does not run migrations at startup.
