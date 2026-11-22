# Debugging log

Real problems found while building GameVault: what it looked like, how it was found, the cause, the fix, and the test that now guards it. They are written as short stories because that is how they come up in an interview.

---

## 1. The "throwaway" test database was my real development database

**Symptom.** A test that counts rows failed with `Expected: 9, Actual: 18`. On the next run: `27`. The tests were adding to something that persisted between runs.

**How it was found.** The number grew by exactly one run's worth each time, so the state had to live somewhere permanent. Listing the databases on the server showed no `GameVault_Test_*` database, even mid-run, and the games and users from the tests were in the development database.

**Cause.** The test factory set the connection string in `ConfigureAppConfiguration`. With the minimal hosting model, `Program.cs` reads `builder.Configuration` while it builds the service collection, before the factory's late configuration source is applied. The test host silently kept the development connection string from `appsettings.Development.json`. The earlier read-only tests had passed by luck, and the README's claim that tests use a throwaway database was wrong.

**Fix.** Use `UseSetting(...)`, which feeds the host's own configuration early enough for `Program.cs` to see. The polluted development database held only seed data plus test leftovers, so it was dropped and recreated. Every test class now creates and drops its own database, and `SELECT name FROM sys.databases` shows none left over.

**Guard.** All integration tests run on a unique database per class; a second run gives the same results.

---

## 2. The monthly request count included the first day of next month

**Symptom.** None that a test showed. A usage test failed with `Expected: 10, Actual: 9`, but that was my own miscounting in the test: the row I had added for "next month" was dated the 10th, which the query did not include either way.

**How it was found.** By reading the query while correcting that test. The upper bound was `u.Date <= start.AddMonths(1)`, and `start.AddMonths(1)` is the *first day of the next month*, so `<=` would have included a row dated the 1st.

**Cause.** An inclusive comparison against an exclusive boundary. In production this would have counted the first day of next month's requests against this month's 20,000 allowance, a slightly early stop at the end of a month.

**Fix.** `<` instead of `<=`, and the test now writes a row dated exactly the 1st of the next month, the one case that distinguishes the two.

**Guard.** `Usage_is_counted_per_day_and_summed_per_month...` asserts that row does not count.

---

## 3. A test helper that would have failed CI

**Symptom.** Nothing, locally. The CI job builds with warnings as errors; the local test run does not.

**How it was found.** Running CI's exact command (`dotnet build -warnaserror -c Release`) before staging the milestone. It failed with `CS0108: 'GameVaultFactory.Server' hides inherited member 'WebApplicationFactory<Program>.Server'`.

**Cause.** A private static field I named `Server` (the SQL Server name) hid a property of the base class.

**Fix.** Renamed it `SqlServer`.

**Lesson.** Run the CI command locally before calling a milestone done. Every milestone since ends with that strict build.

---

## 4. A 429 where a 401 was expected: the rate limiter charges rejections twice

**Symptom.** A test sent six requests to the API, then a seventh with a made-up key and expected `401`. It got `429`, although the per-address limit was 8 a minute.

**How it was found.** A throwaway probe sent requests and printed every status. After three accepted requests, five more made-up keys were allowed (8 − 3, as expected). After three accepted requests *and one rejected by the per-key limit*, only three more were allowed. The rejected request had used two address permits.

**Cause.** ASP.NET's rate-limiting middleware first tries a synchronous acquire on the chained limiters; when the endpoint's limiter refuses, it falls back to the asynchronous path, which acquires the global limiter again. Each endpoint-rejected request costs two global permits.

**Decision.** Accept it: it only makes the address cap slightly stricter for someone already being rejected. The test stopped asserting exact counts and asserts the behaviour: made-up keys are eventually stopped by the address cap, and everything after that stays `429`.

**Guard.** `Each_key_has_its_own_limit_and_an_address_cap_stops_made_up_keys`.

---

## 5. An oversized request logged as a server error

**Symptom.** Sending a 20 KB body to the 16 KB-limited feedback endpoint on the real server returned the right `413`, but the log showed `ERR ... responded 500` with a stack trace, and the body was not a problem document.

**How it was found.** The test host (`TestServer`) cannot reproduce it: it has no request-size feature, so the size attribute does nothing there and the tests passed. Running the real Kestrel server and posting from outside showed the log line.

**Cause.** Kestrel enforces the limit by throwing `BadHttpRequestException` while the body is read during model binding. The exception travelled up through the request logger, which recorded a 500 even though Kestrel had already chosen 413.

**Fix.** A small middleware on `/api` catches `BadHttpRequestException` and writes a `problem+json` response with Kestrel's own status. The controller also checks `Content-Length` so the behaviour is the same under `TestServer`.

**Lesson.** Some behaviour only exists on the real server. The README records which checks were done on Kestrel.

---

## 6. The strict Content Security Policy found an inline style nobody wrote

**Symptom.** The page-crawl test that asserts "no inline scripts, handlers or styles on any page" failed on `/games/{slug}/reviews`: `inline style attribute`.

**How it was found.** The test greps the rendered HTML of 28 pages. The reviews page contains no `style=` in its template.

**Cause.** `<div asp-validation-summary="All">` always renders a hidden `<li style="display:none"></li>`. Under `style-src 'self'` the browser would block it (harmless visually, but a console error on every form page and proof the policy was not clean).

**Fix.** A small partial, `_ErrorSummary.cshtml`, renders the errors itself and nothing when the form is valid. Also removed the other inline styles (cover colours became twelve CSS classes) and the one inline `onfocus` handler (now a data attribute handled in `site.js`).

**Guard.** `No_page_in_the_site_uses_inline_script_handlers_or_styles`.

---

## 7. HSTS "missing" in a test: it never sends to localhost

**Symptom.** The Production test asserting a `Strict-Transport-Security` header failed with `The given header was not found`, although `UseHsts()` was in the pipeline.

**How it was found.** Checking the pipeline, then the framework: `HstsMiddleware` has an `ExcludedHosts` list that defaults to `localhost`, `127.0.0.1` and `[::1]`, on purpose, so a developer's browser does not pin localhost to HTTPS.

**Fix.** The test client uses the host name `gamevault.test`. The production code was right all along.

**Lesson.** A test that fails on correct code is worth a minute to understand, because the reason is often a useful fact.

---

## 8. A performance "optimisation" that measured as no improvement

**Symptom.** None. This one was found by measuring before keeping a change.

**What happened.** The similar-games ranking sorted 200 candidates with LINQ. I replaced it with a bounded top-N selection that avoids sorting the whole pool. BenchmarkDotNet said: the new version took 1.01 times as long at 200 candidates and 1.05 times at 2,000, and allocated about 1.5 times as much. A second experiment (looping over the concrete `HashSet` type) saved about 4 microseconds per cache miss at 200 candidates (19.2 to 15.5 microseconds) and nothing measurable at 2,000.

**Cause.** The sort was never the cost. Time goes into the set comparisons inside the scoring function, and the whole job takes about 20 microseconds. Loading the candidates from SQL takes about 15 milliseconds, hundreds of times more.

**Decision.** Reverted to the simpler code and kept both experiments in the benchmark project, with a note in the code, as the evidence. The change that did matter was how the candidates are loaded (25 ms to 15 ms, and no EF warning).

**Lesson.** Measure first, then decide. See `docs/PERFORMANCE.md`.

---

## 9. Two EF warnings that led to a measurement

**Symptom.** The log showed `Compiling a query which loads related collections for more than one collection navigation ... no QuerySplittingBehavior has been configured` and `Skip/Take without an OrderBy`.

**Cause.** A projection with two collections (genres and tags) in one query makes SQL repeat each game once per genre-tag pair. A `Take(1)` lookup had no `OrderBy`.

**Fix.** The first fix was EF's `AsSplitQuery`. Measuring three ways of loading 200 candidates on 5,000 games showed plain flat queries beat it: 14.8 ms for three plain queries, 17.2 ms for the split query, 22.5 ms for the single joined query. The flat version shipped. The `Take(1)` got an `OrderBy`.

---

## 10. Two tooling traps while building the measurement tool

- **`SET STATISTICS IO` messages never arrived** for EF's commands (they arrived for hand-written ones), so every "logical reads" column read 0. The session's own counter, `sys.dm_exec_sessions.logical_reads` read before and after each run on a single open connection, works for everything.
- **The seeder failed with `IDENTITY_INSERT` on the second batch.** `ChangeTracker.Clear()` detached the genres, platforms and tags; adding the next batch of games re-inserted them as new rows with explicit ids. Fix: `AttachRange` after each clear.
