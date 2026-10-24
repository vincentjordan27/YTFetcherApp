# YT Fetcher Frontend

[![Frontend CI](https://github.com/vincentjordan27/YTNicheFetcher-FE/actions/workflows/frontend-ci.yml/badge.svg?branch=main)](https://github.com/vincentjordan27/YTNicheFetcher-FE/actions/workflows/frontend-ci.yml)

Vue 3 admin interface for YT Fetcher. It provides the operational screens for reviewing channels, managing keyword niches, inspecting backfill and quota usage, viewing ranked videos and snapshot history, and optional Google sign-in.

## Product Flow

![YT Fetcher system flow](docs/assets/yt-fetcher-system-flow.png)

## Stack

- Vue 3, TypeScript, Vite, Vue Router, and SCSS
- `pnpm` for packages

## Local Setup

```powershell
pnpm install
Copy-Item .env.example .env.local
pnpm dev
```

Vite starts locally at `https://localhost:5173` using a development-only self-signed certificate. The browser may
ask for one-time approval on first visit. Set `VITE_API_BASE_URL` in `.env.local` to the local API HTTPS address:
`https://localhost:10002`. Start the API with its `https` launch profile. Both origins must use HTTPS because the
optional authentication session cookie is secure-only.

When the backend deployment enables authentication, also populate the public Firebase Web values listed in `.env.example`: `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, and `VITE_FIREBASE_APP_ID`. These identify the Firebase web app; server credentials stay in the backend. `.env.local` is ignored by Git; keep local configuration there.

## Structure

```text
src/
|- app/                                Product modules
|  |- auth/                            Optional Firebase Google sign-in and session state
|  |  |- api/                          Session endpoint functions and wire types
|  |  |- components/                   Login and Google sign-in UI
|  |  |- composables/                  Bootstrap, sign-in, sign-out, and unauthorized handling
|  |  |- data/                         Auth-only display copy
|  |  |- styles/                       Auth-only SCSS
|  |  |- types/                        Session and identity types
|  |  |- views/                        Route views
|  |  `- routes.ts                     Auth route records
|  |- channels/                        Pending, approved, and rejected reviews
|  |  |- api/                          Channel endpoint functions
|  |  |- components/                   Channel-only UI
|  |  |- composables/                  List and action state
|  |  |- data/                         API-to-view mapping and design data
|  |  |- styles/                       Channel-only SCSS
|  |  |- types/                        Channel types
|  |  |- views/                        Route views
|  |  `- routes.ts                     Channel route records
|  |- backfill-runs/                   Backfill run history and retry
|  |- keywords/                        Keyword niche management
|  |  |- components/                   Keyword table and dialog
|  |  |- composables/                  Page and dialog state
|  |  |- data/                         Temporary design data
|  |  |- styles/                       Keyword-only SCSS
|  |  |- types/                        Keyword types
|  |  |- views/                        Route view
|  |  `- routes.ts                     Keyword route records
|  |- usage/                           Daily YouTube API quota usage
|  `- videos/                          Active/reference videos, preview, and snapshot history
|     |- components/                   Video table and dialog
|     |- composables/                  Page interaction state
|     |- data/                         Temporary design data
|     |- styles/                       Video-only SCSS
|     |- types/                        Video types
|     |- views/                        Route view
|     `- routes.ts                     Video route records
|- common/                             Cross-module infrastructure
|  |- api/                             Shared fetch client and API errors
|  |- components/                      Shared UI components
|  |- layouts/                         Shared application shells
|  |- router/                          Router setup and route composition
|  |- styles/                          Shared component and layout SCSS
|  `- views/                           Generic views, including not found
|- styles/global/                      Reset, tokens, typography, and base elements
|- App.vue                             Root application shell
`- main.ts                             Application bootstrap
```

Keep feature-specific code inside `src/app/<module>`. Move code into `src/common` only after it is truly shared by more than one module. Route records live with their module and are composed by the root router.

## Quality Checks

```powershell
pnpm typecheck
pnpm build
```

GitHub Actions runs these checks for pushes and pull requests targeting `main`.
