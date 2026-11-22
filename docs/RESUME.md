# Resume and GitHub mapping

Each claim in the application email maps to a specific, checkable place in this repository. Numbers come from [PERFORMANCE.md](PERFORMANCE.md) and were measured locally on a synthetic 5,000-game catalogue, not on a deployed site.

| Claim | Where it is visible |
|---|---|
| OOP and SOLID | Domain entities with behaviour (`Review.Write`, `Game.ApplyRating`); small interfaces in `Application`; dependencies point inward ([ARCHITECTURE.md](ARCHITECTURE.md)) |
| Design patterns | Repository, Strategy (similarity), Decorator (cached catalogue), Factory (providers), Observer (domain events), Options: table in ARCHITECTURE.md |
| Modular architecture | Four projects plus tests, with the layer diagram in ARCHITECTURE.md |
| API integrations | `RawgGameProvider`: typed `HttpClient`, retry, timeout, circuit breaker, request-budget guard (`Infrastructure/Rawg`); and the feedback API consumed by Unity builds (`Controllers/Api`) |
| ASP.NET Core and MVC | Controllers, Razor views, partials, a view component, tag helpers, Identity, named policies, a custom authentication scheme, rate limiting, health checks |
| Security | Strict Content Security Policy with no inline code, HSTS, Secure cookies, anti-forgery on every POST, hashed API keys, content-checked uploads, CSV-injection-safe export |
| Debugging | [DEBUGGING.md](DEBUGGING.md): ten real problems with symptom, how each was found, cause, fix and the test that guards it |
| Performance optimisation | PERFORMANCE.md: before and after numbers, query plans and counts, a benchmark that rejected an optimisation |
| Testing | 350 tests: unit, provider (fake HTTP handler), integration on a real SQL Server, authorization, security headers, and a browser smoke suite |

## Resume bullets

Use these as drafts. The numbers are real; keep the qualifier about how they were measured.

- Built **GameVault**, an ASP.NET Core MVC catalogue and review platform (.NET 10, EF Core, SQL Server, Identity) with a RAWG integration using a typed `HttpClient`, retry, timeout and circuit-breaker resilience, a monthly request-budget guard and `HybridCache`.
- Cut first-page catalogue reads from about **1,190 to 147** and median query time from **5 to 1 ms** on a 5,000-game catalogue by adding sort-matched indexes, and removed an N+1 pattern (**14 queries to 2**) with projections; enforced the query counts with tests so they cannot regress.
- Raised catalogue throughput about **6x** (**305 to 1,882 requests/s**) and cut the 95th-percentile latency from **124 to 19 ms** with a decorator-based cache, in a local load test; cut page weight by **84%** with brotli.
- Designed a **feedback API and inbox**: per-game API keys stored only as hashes, per-key and per-address rate limiting, problem-details errors, and a moderated studio dashboard so Unity game builds send player feedback straight to the owner.
- Hardened the site with a **strict Content Security Policy (no inline script or style)**, enforced by a page-crawl test and checked in a real browser, plus HSTS, secure cookies, content-sniffed uploads and a CSV export that cannot run as a formula.
- Wrote a test suite of about 350 tests (unit, integration against a real SQL Server, authorization, browser smoke) with **100% line and 92% branch coverage** of the Application layer, and a CI pipeline that fails below 70%.
- Benchmarked a planned optimisation with BenchmarkDotNet, found it **gave no improvement**, and reverted it, keeping the measurements as evidence.

Deployed: the site is live on Azure Container Apps (URL in the README). A load test against it from the development machine gave a p95 of 252 ms, but most of that is network distance; quote it only with that qualifier.

Not claimed, because it has not happened yet: a real Unity build posting feedback, and a load test that isolates server time on the deployed instance.
