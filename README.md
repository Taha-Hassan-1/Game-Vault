# GameVault

An ASP.NET Core MVC site (.NET 10) where visitors browse a game catalogue, rate and review games, and where the studio's own games get a built-in feedback loop. Built from the spec "GameVault: Spec & Implementation Plan".

## Status

| Milestone | State |
|---|---|
| M0 Setup | Done: solution, CI file, Dockerfile, compose (Docker files not run locally; Docker is not installed on the dev machine) |
| M1 Domain and data | Done: entities, EF configurations, `InitialCreate` migration, seed (31 games incl. Untangle) |
| M2 Catalogue | Done: home, search, genre/platform/tag filters, sort, paging (cap 24), details, similar games, caching decorator |
| M3 RAWG integration | Done: provider interface + factory, typed client with retry/timeout/circuit breaker, mapper, request budget, lazy details, nightly sync. Off until you set a key (see below); not yet run against the live RAWG API |
| M4 Accounts and reviews | Done: Identity with Member/Moderator/Admin roles, register/sign in/profile, 1 to 5 star reviews (one per member per game), `ReviewChanged` handler keeps ratings current, wishlist and played lists, report and moderation queue |
| M5 Studio feedback hub | Done: studio dashboard, studio game create/edit with cover and screenshot uploads, suggestion board with votes and statuses, feedback API with per-key rate limiting, API-key management, feedback inbox with filters and CSV export. A feedback message from a real Unity build of Untangle has not been sent yet (see below) |
| M6 Harden and ship | Not started |

## Layout

```
src/GameVault.Domain          entities, enums, rules (no dependencies)
src/GameVault.Application     interfaces, catalogue service, similarity strategy, domain events
src/GameVault.Infrastructure  EF Core + SQL Server, repository, HybridCache decorator, seed
src/GameVault.Web             MVC controllers, Razor views, view component
tests/GameVault.Tests         xUnit unit tests + integration tests (WebApplicationFactory)
```

Patterns in use: Repository (`IGameRepository`), Strategy (`ISimilarityStrategy`), Decorator (`CachedGameCatalog`), Observer (`IDomainEventDispatcher`), dependency injection from `Program.cs`.

## Run

Needs the .NET 10 SDK and a SQL Server. Development uses `.\SQLEXPRESS` with Windows auth (`appsettings.Development.json`); change the connection string there if yours differs.

```powershell
dotnet run --project src/GameVault.Web
```

In Development the app applies migrations and seeds on start. With Docker: `docker compose up`.

## Accounts and roles

Anyone can register and becomes a Member. Moderators and Admins are not self-service: the first Admin is created at startup from configuration, and that account has all three roles. Set it with user-secrets (or the environment variables `Admin__Email` and `Admin__Password` in production); nothing is stored in the repo:

```powershell
dotnet user-secrets set "Admin:Email" "you@example.com" --project src/GameVault.Web
dotnet user-secrets set "Admin:Password" "<at least 10 characters, upper, lower, digit>" --project src/GameVault.Web
```

Roles and what they open:

| Area | Access |
|---|---|
| Catalogue, game pages, similar games | Public |
| Write, edit or delete own review; report a review; lists; profile | Member |
| `/moderation`: hide or restore reviews, dismiss reports | Moderator or Admin (policy `CanModerate`) |

Behaviour worth knowing:
- A member may delete their own review outright. Moderators never delete: they hide, which removes the review from the page and the rating but keeps it so it can be restored. Hiding also resolves its reports.
- Anonymous visitors to a protected page are redirected to sign-in, and members without the role to an access-denied page. This is how cookie authentication answers 401 and 403 in a browser.
- Every POST needs an anti-forgery token. Review text is plain text and always HTML-encoded.
- Sign-in locks an account for 10 minutes after 5 failed attempts. Email confirmation is off until a mail sender exists.

## Studio feedback hub

Everything under `/studio` belongs to the Admin (policy `StudioOwner`). Moderators cannot open it.

| Page | What it does |
|---|---|
| `/studio` | Per-game rating and counts of new feedback and open suggestions, newest feedback, and feedback per app version (bugs, ideas, praise per build) |
| `/studio/games` | Create and edit studio games, upload a cover and up to 8 screenshots. The address (slug) is generated once and never changes |
| `/studio/inbox` | Web and API feedback together; filter by game, category, status and source; mark as New, Read or Archived; export the filtered list as CSV |
| `/studio/keys` | Create and revoke API keys per game. A key is shown once, on creation |
| `/games/{slug}/suggestions` | Public board for each studio game. Members post and upvote (one vote each, withdrawable); the Admin sets New, Planned, In progress, Shipped or Declined |
| `/games/{slug}/feedback` | Members send a bug, idea or praise straight to the inbox |

### Sending feedback from a Unity build

Create a key in `/studio/keys`, then post JSON with it in the `X-Api-Key` header:

```
POST /api/v1/feedback
X-Api-Key: gv_...
Content-Type: application/json

{ "category": "Bug", "body": "The level 12 board will not load.", "appVersion": "1.4.0", "playerRef": "anon-7f3a" }
```

| Field | Rules |
|---|---|
| `category` | `Bug`, `Idea` or `Praise` (required) |
| `body` | 1 to 2000 characters (required) |
| `appVersion` | optional, up to 32 characters of letters, digits and `. _ + -` |
| `playerRef` | optional opaque string up to 100 characters; do not send personal data |

Answers: `201 {"id": 123}` on success. Errors are `application/problem+json`: `400` with a field-by-field `errors` object, `401` for a missing, unknown or revoked key, `413` over 16 KB, `415` if it is not JSON, `429` with `Retry-After` when over the limit. The key decides the game, so a build cannot post to another game.

Limits (configurable): 30 requests a minute per key (`FeedbackApi:PermitPerMinute`) and 120 a minute per address across `/api` (`FeedbackApi:IpPermitPerMinute`), so made-up keys do not buy extra requests. Behind a proxy or load balancer, forwarded headers must be configured at deploy time or every client will share one address.

Security notes:
- Only a SHA-256 hash and a 10-character prefix of each key are stored. A lost key cannot be recovered, only replaced.
- Uploaded images are accepted by their content (PNG, JPEG, WebP, up to 2 MB), never by file name or declared type. SVG is refused because it can carry script. Files are renamed to random names, stored outside `wwwroot` (`Uploads:Path`, default `App_Data/uploads`, git-ignored) and served with `nosniff`.
- CSV export neutralises cells that begin with `=`, `+`, `-` or `@`, since feedback text comes from strangers and a spreadsheet would otherwise run it as a formula.

## RAWG

Off by default. To turn it on, get a free key at rawg.io/apidocs and store it in user-secrets, never in the repo:

```powershell
dotnet user-secrets set "Rawg:ApiKey" "<your key>" --project src/GameVault.Web
dotnet user-secrets set "Rawg:Enabled" "true" --project src/GameVault.Web
```

In production use the environment variables `Rawg__ApiKey` and `Rawg__Enabled`. Startup fails fast if `Rawg:Enabled` is true without a key.

How it behaves:
- **Lazy details:** a RAWG game's description and screenshots are fetched the first time its page is opened, then stored and refreshed after 30 days.
- **Nightly sync:** a background service refreshes the stalest rows first, then imports the next pages of popular games (cursor saved in `SyncState`). The first run starts one minute after boot.
- **Budget:** every outbound attempt, retries included, is counted in `ApiUsage`. The sync stops at 15,000 requests a month; on-demand calls stop at 20,000.
- **Outages:** retries (3, exponential with jitter, honours `Retry-After`), 10 s per attempt, 30 s total, circuit breaker. A failure is logged and the page renders from the stored copy.
- **Attribution:** the footer and every RAWG game page link to rawg.io, as RAWG's terms require.
- **Open item:** RAWG's terms don't mention caching. Email them to confirm before the first bulk import; if the answer is no, switch to fetch-through caching only.

## Tests

```powershell
dotnet test
```

Integration tests create and drop a throwaway database (`GameVault_Test_<guid>`) on `.\SQLEXPRESS`, or on the server in `GAMEVAULT_TEST_SERVER`. They use Windows authentication, so CI currently runs the unit tests only; Testcontainers would replace this once Docker is available.

## Known gaps against the spec

- Search uses SQL `LIKE`, not full-text search (needs the Full-Text feature, which is not confirmed on SQL Express here).
- Secrets and the RAWG key will use user-secrets and environment variables; nothing is stored in the repo.
