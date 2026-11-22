# Deployment

**Status: not deployed.** There is no live URL yet. The host is chosen on the day (free tiers change their terms), and nothing here has been run on a real host. The container image has not been built on the development machine (Docker is not installed there); CI builds it on every push to `main`. Treat this page as a checklist, and record what you find the first time in `docs/DEBUGGING.md`.

## What the app needs

| Need | How it is provided |
|---|---|
| A SQL Server database | `ConnectionStrings__GameVault`. Azure SQL Database and SQL Server both work. |
| HTTPS | Terminated by the host or a proxy. The app sends HSTS and Secure cookies outside Development. |
| A folder that survives restarts | Uploaded studio images go to `Uploads__Path` (the image sets `/data/uploads`). Mount a volume there, or move storage to blobs behind `IImageStore`. |
| Secrets | Environment variables or the host's secret store. Nothing secret is in the repository. |

## Settings (environment variables use `__` for `:`)

| Setting | Required | What it does |
|---|---|---|
| `ConnectionStrings__GameVault` | yes | The database. |
| `Admin__Email`, `Admin__Password` | first start | Creates the first Admin (all three roles). Ignored once that email exists; remove the password afterwards. |
| `Rawg__Enabled`, `Rawg__ApiKey` | optional | Turns on RAWG import. Startup fails fast if enabled without a key. |
| `Database__MigrateOnStart` | first start | `true` applies migrations and seeds roles and sample data at start. For anything beyond a first run, apply migrations as a separate step (below) and leave this off. |
| `ASPNETCORE_FORWARDEDHEADERS_ENABLED` | behind a proxy | `true` (set in the image). Without it every client appears to come from the proxy's address, which breaks per-address rate limiting and the https redirect. |
| `Security__SecureCookies` | no | Defaults to `true` outside Development. Set `false` only for a plain-http local trial. |
| `FeedbackApi__PermitPerMinute`, `FeedbackApi__IpPermitPerMinute` | no | Rate limits for the Unity endpoint (30 and 120). |
| `Catalogue__CacheEnabled` | no | Leave on. Off only to measure the cache. |
| `Uploads__Path` | no | Where uploads are stored. |

## Database migrations

Preferred, as an explicit step before the new version starts:

```powershell
dotnet ef migrations script --idempotent -p src/GameVault.Infrastructure -s src/GameVault.Web -o migrate.sql
# review it, then run migrate.sql against the production database
```

Convenient for a first run only: `Database__MigrateOnStart=true`. It runs inside the web process, so with several instances they would race.

## Checks after deploying

1. `GET /health` returns 200 and `{"status":"Healthy","checks":[{"name":"database",...}]}`. This is the endpoint to give a load balancer; it checks the database only.
2. `GET /health/rawg` reports RAWG separately. It returns 200 even when RAWG is down (status `Degraded` in the body), so a RAWG outage never takes the site out of rotation.
3. Response headers on any page include `Content-Security-Policy`, `Strict-Transport-Security`, `X-Content-Type-Options`, `Referrer-Policy` and `Permissions-Policy`. A quick look at https://securityheaders.com is a good second opinion.
4. Sign in with the admin account, create a studio game, upload a screenshot, restart the app, and check the screenshot is still there (that proves the uploads volume).
5. Create an API key in `/studio/keys` and send the example request from the README. It must return 201 and the message must appear in `/studio/inbox`.
6. Run the load test against the live URL and write the result into `docs/PERFORMANCE.md`:

```powershell
k6 run -e BASE_URL=https://your-host perf/catalogue.k6.js
```

The target is a 95th-percentile response under 300 ms for cached catalogue pages (`warm_page_duration`).

## Choosing a host (decide on the day)

Compare the free or lowest tier of the options below at the time, because limits change:

- **A container host** (for example Azure Container Apps, Fly.io, Railway, Render): runs the image as is; needs a database service and a volume for uploads.
- **Azure App Service** with Azure SQL Database: no container to manage; needs an Azure account (an open question for the owner) and an Azure Files mount or blob storage for uploads.

Things to check for any host: does it keep a disk across restarts, does it allow outbound calls to `api.rawg.io`, does it cold-start (the first request after idle includes the cache fill), and what are the database size and connection limits.

## Before the first RAWG import

RAWG's terms do not mention caching or local storage. Email RAWG to confirm that storing the fields the pages show is acceptable. If the answer is no, switch to fetch-through caching only: stop the sync worker and keep results in `HybridCache`, not in the database.
