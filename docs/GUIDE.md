# Operating guide

How each part of GameVault works and how to configure it. The README is the two-minute overview; this is the reference. Deployment steps are in [DEPLOYMENT.md](DEPLOYMENT.md), the reasons for the design in [adr/](adr/README.md).

## Running it

Needs the .NET 10 SDK and a SQL Server. Development uses `.\SQLEXPRESS` with Windows authentication (`appsettings.Development.json`); change the connection string there if yours differs.

```powershell
dotnet run --project src/GameVault.Web
```

In Development the app applies migrations and seeds roles and 31 sample games (one of them the studio's own, "Untangle") on start. With Docker: set `GAMEVAULT_ADMIN_EMAIL` and `GAMEVAULT_ADMIN_PASSWORD`, then `docker compose up --build` (the compose file has not been run on the development machine, which has no Docker).

## Configuration

Settings come from `appsettings*.json`, user-secrets in development and environment variables in production (`__` stands for `:`). Nothing secret is in the repository.

| Setting | Default | What it does |
|---|---|---|
| `ConnectionStrings:GameVault` | (empty) | The SQL Server database. Required. |
| `Admin:Email`, `Admin:Password` | unset | Creates the first Admin at start. |
| `Rawg:Enabled`, `Rawg:ApiKey` | `false`, empty | Turns on the RAWG import. Startup fails fast if enabled without a key. |
| `SyncBudget:MonthlyLimit`, `SyncBudget:SyncLimit` | 20,000 and 15,000 | RAWG request budget per month; the sync stops at the second. |
| `Database:MigrateOnStart` | `false` | Apply migrations and seed at start outside Development (first run only). |
| `Security:SecureCookies` | `true` outside Development | Secure flag on the sign-in and anti-forgery cookies. |
| `FeedbackApi:PermitPerMinute`, `FeedbackApi:IpPermitPerMinute` | 30 and 120 | Rate limits for the Unity endpoint. |
| `Catalogue:CacheEnabled` | `true` | Off only to measure the cache. |
| `Uploads:Path` | `App_Data/uploads` | Where uploaded images are stored. |

## Accounts and roles

Anyone can register and becomes a Member. Moderators and Admins are not self-service: the first Admin is created at startup from configuration, and that account has all three roles. Set it with user-secrets (or the environment variables `Admin__Email` and `Admin__Password` in production):

```powershell
dotnet user-secrets set "Admin:Email" "you@example.com" --project src/GameVault.Web
dotnet user-secrets set "Admin:Password" "<at least 10 characters, upper, lower, digit>" --project src/GameVault.Web
```

| Area | Access |
|---|---|
| Catalogue, game pages, similar games, suggestion boards (reading) | Public |
| Write, edit or delete own review; report a review; lists; profile; suggestions and votes; web feedback | Member |
| `/moderation`: hide or restore reviews, dismiss reports | Moderator or Admin (policy `CanModerate`) |
| `/studio/*`: games, inbox, API keys, suggestion statuses | Admin only (policy `StudioOwner`) |

Behaviour worth knowing:
- A member may delete their own review outright. Moderators never delete: they hide, which removes the review from the page and the rating but keeps it so it can be restored. Hiding also resolves its reports.
- Anonymous visitors to a protected page are redirected to sign-in, and members without the role to an access-denied page. This is how cookie authentication answers 401 and 403 in a browser.
- Every POST needs an anti-forgery token. Review text is plain text and always HTML-encoded.
- Sign-in locks an account for 10 minutes after 5 failed attempts. Email confirmation is off until a mail sender exists.

## Studio feedback hub

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

Answers: `201 {"id": 123}` on success. Errors are `application/problem+json`: `400` with a field-by-field `errors` object, `401` for a missing, unknown or revoked key, `413` over 16 KB, `415` if it is not JSON, `429` with `Retry-After` when over the limit, `500` with a `traceId` to quote if something breaks on our side. The key decides the game, so a build cannot post to another game.

Limits: 30 requests a minute per key and 120 a minute per address across `/api`, so made-up keys do not buy extra requests. Behind a proxy or load balancer, set `ASPNETCORE_FORWARDEDHEADERS_ENABLED=true` (the container image does) or every client appears to share the proxy's address.

## Security

- **Headers on every response:** a Content Security Policy with `script-src 'self'; style-src 'self'` and no `unsafe-inline`, `X-Content-Type-Options`, `X-Frame-Options`, `Referrer-Policy`, `Permissions-Policy`, `Cross-Origin-Opener-Policy`; HSTS for a year outside Development.
- **No inline code anywhere.** A test crawls 28 pages for inline scripts, handlers and styles, and the browser smoke test confirms the policy blocks nothing ([ADR 14](adr/0014-strict-content-security-policy.md)).
- **Cookies** are HttpOnly, SameSite=Lax and Secure outside Development.
- **API keys:** only a SHA-256 hash and a 10-character prefix are stored. A lost key cannot be recovered, only replaced.
- **Uploads** are accepted by their content (PNG, JPEG, WebP, up to 2 MB), never by file name or declared type. SVG is refused because it can carry script. Files get random names, live outside `wwwroot` and are served with `nosniff`.
- **CSV export** neutralises cells that begin with `=`, `+`, `-` or `@`, since feedback text comes from strangers and a spreadsheet would otherwise run it as a formula.
- **Errors** show a friendly page (the site) or a problem document (the API) with a reference id, never an exception or a stack trace.

## Health checks

| Endpoint | Meaning |
|---|---|
| `GET /health` | The database. 200 when healthy, 503 when not. Give this one to a load balancer. |
| `GET /health/rawg` | Whether RAWG answers. Always 200; a RAWG outage shows as `Degraded` in the body and never takes the site out of rotation. Sends no API key and spends none of the monthly budget. |

Both return names and statuses only, never connection strings or exception text.

## RAWG

Off by default. To turn it on, get a free key at rawg.io/apidocs and store it in user-secrets, never in the repo:

```powershell
dotnet user-secrets set "Rawg:ApiKey" "<your key>" --project src/GameVault.Web
dotnet user-secrets set "Rawg:Enabled" "true" --project src/GameVault.Web
```

In production use the environment variables `Rawg__ApiKey` and `Rawg__Enabled`.

How it behaves:
- **Lazy details:** a RAWG game's description and screenshots are fetched the first time its page is opened, then stored and refreshed after 30 days.
- **Nightly sync:** a background service refreshes the stalest rows first, then imports the next pages of popular games (cursor saved in `SyncState`). The first run starts one minute after boot.
- **Budget:** every outbound attempt, retries included, is counted in `ApiUsage`. The sync stops at 15,000 requests a month; on-demand calls stop at 20,000.
- **Outages:** retries (3, exponential with jitter, honours `Retry-After`), 10 s per attempt, 30 s total, circuit breaker. A failure is logged and the page renders from the stored copy.
- **Images:** cards ask RAWG's CDN for a 420 px version of each cover, with a fallback to the original. Not verified against the live service (see PERFORMANCE.md).
- **Attribution:** the footer and every RAWG game page link to rawg.io, as RAWG's terms require.
- **Open item:** RAWG's terms don't mention caching. Email them to confirm before the first bulk import; if the answer is no, switch to fetch-through caching only. **Not yet run against the live RAWG API.**

## Tests

```powershell
dotnet test                                                                  # unit + integration (about 350 tests, 15 seconds)
dotnet test --settings coverage.runsettings --collect:"XPlat Code Coverage" --results-directory TestResults
powershell scripts/check-coverage.ps1                                        # Application line coverage, fails below 70%
$env:GAMEVAULT_SMOKE = "1"; dotnet test tests/GameVault.Smoke                # real-browser smoke flows (needs Edge or Chrome)
```

- **Integration tests** create and drop a throwaway database per test class (`GameVault_Test_<guid>`) on `.\SQLEXPRESS` with Windows authentication, or on the server in `GAMEVAULT_TEST_CONNECTION` (a connection string without a `Database` part; CI uses it with a SQL Server container).
- **Query counts** per page are pinned by `QueryBudgetTests`.
- **Smoke tests** (`tests/GameVault.Smoke`, outside the main solution so CI does not need the Playwright package) start the real site on a free port and drive Edge through register, review, wishlist and suggestion flows, failing on any Content Security Policy violation. Set `GAMEVAULT_SCREENSHOTS` to a folder to save pictures.
