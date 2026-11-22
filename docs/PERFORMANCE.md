# Performance

The approach for every item below was: **measure, change, measure again**, and keep a change only if the numbers justify it. One change did not (see "What did not help"), and it was reverted. The raw output of every run is in [`perf/`](perf/), and the tools to reproduce it are in `benchmarks/GameVault.Benchmarks`.

## How to read these numbers

- **Dataset.** A synthetic catalogue of 5,000 games (19 genres, 12 platforms, 300 tags, 15,047 reviews, 37,532 game-tag rows), seeded with a fixed random seed so before and after runs see identical data. The 31-game seed data would make everything look instant.
- **Machine.** AMD Ryzen 5 7500F (12 logical cores), Windows 11, .NET 10.0.6, SQL Server Express on the same machine. The browser, the site, the database and the load generator all share it.
- **Local, not deployed.** There is no network between the site and the database, so a query costs less here than on a real host, and **the number of queries matters more in production than these timings show**. Nothing here is a claim about a deployed instance: that measurement (the spec's "p95 under 300 ms on the deployed instance") is still to do once the site has a host, with `perf/catalogue.k6.js`.
- **Method.** Query timings are the median and 95th percentile of 40 runs after 3 warm-up runs, each run on a fresh `DbContext` as a request would. "Reads" are logical page reads from the server's own session counter. The load runs use 16 concurrent clients.

## Summary against the spec's table

| Area | What was done | Proof | Before | After |
|---|---|---|---|---|
| Reads | Project to view models with `Select`, `AsNoTracking`, page on the server | One catalogue page, 5,000 games | 747 ms, 4 queries, 1,532 reads (a naive tracked graph paged in memory) | **1.2 ms, 2 queries, 147 reads** |
| N+1 queries | Genre names inside the one projected query; query counts pinned by tests | 12 cards with their genres | 3.4 ms, 14 queries (query per game) | **1.0 ms, 2 queries** |
| Paging and sorting | One index per sort, key order matching the full `ORDER BY` | First page of each sort | 5.2 to 7.2 ms and about 1,190 reads | **0.9 to 1.0 ms and about 147 reads** |
| Search | `LIKE` on title and description (full-text is not available here) | Search for one word | 57.9 ms, 2,236 reads | 28.9 ms, 1,196 reads (the sort index helps); still a scan |
| Rating averages | Denormalised on `Game`, updated by the `ReviewChanged` handler | Top-rated page | 23.3 ms, 3,410 reads (average computed on every read) | **0.2 ms, 2 reads** |
| Caching | `HybridCache` decorator, five-minute lifetime, tag invalidation | Load test, 7 pages | cache off: p95 123.7 ms, 305 requests/s | **cache on: p95 19.2 ms, 1,882 requests/s** |
| HTTP | Response compression; long cache on versioned assets; smaller RAWG images | Page weight; response headers | 21.2 KB per page; assets revalidated on every view | **3.4 KB (brotli); assets cached for a year** |
| Hot code | BenchmarkDotNet on the similarity strategy | 200 and 2,000 candidates | 19.2 µs / 274.6 µs | unchanged: nothing worth shipping (below) |
| Load | k6 script for the deployed site; a local load runner | Local run | n/a | script ready, **not run against a deployed site** |

## Reads, N+1 and the shape of a page

The catalogue page, in the shipped form, is two queries: a count and one projected page that includes each game's genre names. The naive form loads the whole entity graph (genres, platforms, tags) tracked and pages in memory.

| Variant | Median | p95 | Queries | Logical reads |
|---|---:|---:|---:|---:|
| Shipped: projection, server paging | 1.2 ms | 1.5 ms | 2 | 147 |
| Naive: tracked graph, paged in memory | 747 ms | 875 ms | 4 | 1,532 |
| Shipped: 12 cards with genres | 1.0 ms | 1.7 ms | 2 | 144 |
| Naive: a genre query per game | 3.4 ms | 4.0 ms | 14 | 183 |

(The naive variants are counterfactuals written in the benchmark tool for comparison, not code that ever shipped. In the "before" run, one of the 40 naive runs took 99 seconds, an SQL Server Express stall that shows as a 98,891 ms p95 in `perf/queries-before.txt`; the median is unaffected. The naive figures in the table above are from the "after" run.)

The N+1 gap looks small here (3.4 ms against 1.0 ms) because the database is on the same machine. Every extra query is a network round trip on a real host, so at a typical one to five milliseconds each, twelve extra queries would add roughly 12 to 60 ms to one page. That is an estimate from the query count, not a measurement.

**Query counts per page are pinned by tests** (`QueryBudgetTests`, which count every SQL command the app sends):

| Page | Cold cache | Warm cache |
|---|---:|---:|
| Home | 3 | 0 |
| Catalogue (a page, sorted or filtered) | 2 (plus 3 for the filter lists on the first request after a change) | 0 |
| A game page (visitor) | 12 | 0 |
| A game page (signed-in member), extra on top of the shared page | 4 | 4 |
| Studio inbox | 3 | n/a (not cached) |

The tests also assert that the count does **not** change with the page size, with the number of reviews on a page, or with which game is shown. A number that grows with the data means an N+1 has crept in. The 12 for a game page are 6 for the details and reviews and 6 for the similar-games candidates, and happen once per game per five minutes.

## Paging and sorting: indexes that match the sort

Each catalogue sort now has an index whose key order matches the whole `ORDER BY`, including the tie-break on title. Before, only the title sort had one, and the others scanned and sorted all 5,000 games.

| Sort, page 1 | Before | After |
|---|---|---|
| Newest | 5.2 ms, 1,194 reads | **1.0 ms, 147 reads** |
| Released | 5.6 ms, 1,200 reads | **1.0 ms, 153 reads** |
| Top rated | 7.2 ms, 1,194 reads | **1.0 ms, 147 reads** |
| Title (already indexed: the control) | 1.0 ms, 153 reads | 0.9 ms, 147 reads |
| Genre filter + top rated | 3.6 ms, 1,224 reads | 2.1 ms, 1,058 reads |
| Home page data (3 queries) | 5.6 ms, 2,292 reads | 1.6 ms, 1,236 reads |

The Title row is the control: it was already indexed and did not move, so the other gains come from the new indexes, not from noise.

**Trade-off, stated plainly.** Very deep pages now read more pages, not fewer: page 200 of Newest went from 1,192 to 7,773 reads, because the index scan has to look up each skipped row. Median time still improved (5.3 to 3.3 ms) and a visitor rarely goes that deep, but the cost grows with the page number. If deep paging mattered, keyset paging (continue after the last seen value) would fix it; it is not needed at this size.

## Search

| Variant | Median | Logical reads |
|---|---:|---:|
| Shipped: `LIKE '%term%'` on title and description | 28.9 ms (was 57.9 ms) | 1,196 (was 2,236) |
| Title only, `LIKE '%term%'` | 4.6 ms | 38 |
| Title prefix, `LIKE 'term%'` (can seek the title index) | 0.4 ms | 10 |

A word anywhere in the title or description is a scan, so the cost grows with the catalogue; if it is roughly linear, 50,000 games would be about ten times slower (an extrapolation, not a measurement). The spec asks for SQL full-text search. **It could not be built or tested here:** the SQL Server Express used for development has no Full-Text feature installed (`FULLTEXTSERVICEPROPERTY('IsFullTextInstalled')` returns 0). [`docs/sql/fulltext.sql`](sql/fulltext.sql) holds the setup for a server that has it, clearly marked untested, and [ADR 16](adr/0016-search-like-not-fulltext.md) records the decision.

## Rating averages: denormalised on purpose

| Variant | Median | Logical reads |
|---|---:|---:|
| Shipped: the stored `RatingAverage` with its index | 0.2 ms | 2 |
| Naive: average the reviews on every read | 23.3 ms | 3,410 |

The `ReviewChanged` handler recomputes the stored average whenever a review is added, edited, deleted, hidden or restored, so no list page ever aggregates reviews (about 115 times faster on 15,000 reviews).

## Similar games

Loading 200 candidates, three ways, on the same data:

| Variant | Median | Queries |
|---|---:|---:|
| M5: EF split query | 25.0 ms | 4 |
| Single joined query (repeats each game per genre-tag pair) | 21.8 ms | 1 |
| Three plain queries joined in memory, ordered like the rating index (**shipped**) | **11.0 ms** | 3 |

The shipped version reads more pages than the M5 one (3,767 against 1,576) because it takes the top 200 in index order with a key lookup each, instead of sorting; it is still much faster. It also removed the EF warning about loading several collections at once.

### What did not help: the similarity maths

BenchmarkDotNet, 200 and 2,000 candidates ([`perf/similarity-bdn.txt`](perf/similarity-bdn.txt)):

| Variant | 200 candidates | Allocated | 2,000 candidates | Allocated |
|---|---:|---:|---:|---:|
| **Shipped:** LINQ pipeline | 19.2 µs | 5.5 KB | 274.6 µs | 51.1 KB |
| Experiment: bounded top-N insertion buffer | 19.4 µs (1.01×) | 8.1 KB (1.48×) | 288.9 µs (1.05×) | 78.4 KB (1.53×) |
| Experiment: score on concrete `HashSet`s | 15.5 µs (0.80×) | 5.5 KB | 271.2 µs (0.99×) | 51.1 KB |

I wrote the bounded top-N version expecting a win, because it avoids sorting the whole pool. It was no faster and allocated more, so it was **reverted** and the original, simpler code stays. Ranking 200 candidates takes about 20 microseconds; loading them from SQL takes about 11 milliseconds, roughly 500 times more. The experiments live in the benchmark project so the result can be reproduced ([DEBUGGING.md](DEBUGGING.md), story 8).

## Caching

Load test, 4,000 requests over seven pages (home, three catalogue views, a search, two game pages), 16 concurrent clients, brotli, Release build in the Production environment:

| | p50 | p90 | p95 | p99 | Requests/s |
|---|---:|---:|---:|---:|---:|
| Cache **off** (`Catalogue:CacheEnabled=false`) | 36.8 ms | 109.1 ms | 123.7 ms | 170.3 ms | 305 |
| Cache **on** (shipped) | 7.1 ms | 14.5 ms | **19.2 ms** | 29.2 ms | **1,882** |

The cache takes the 95th percentile down about 6.4 times and raises throughput about 6.2 times. Seven distinct URLs means seven misses in 4,000 requests, so the hit rate is 99.8% by construction (computed from the test design, not read from a counter). The first request to each page (a miss, plus JIT and connection setup) took 29 to 393 ms; see `perf/load-cache-on.txt`. A cached page is invalidated by anything that changes it (a review, a hide, a studio edit, an upload, an import).

## HTTP

**Page weight** (average over the seven pages, from `perf/load-weight.txt`):

| Encoding | Average response | Saving |
|---|---:|---:|
| None | 21.2 KB | n/a |
| gzip | 4.5 KB | 79% |
| brotli | 3.4 KB | 84% |

ASP.NET does not compress HTTPS responses by default (a deliberate defence against the BREACH attack on pages that contain secrets such as anti-forgery tokens). Behind a platform proxy that terminates TLS the app sees plain HTTP and compresses; if a host does not forward `Accept-Encoding`, or the app terminates TLS itself, compression must happen at the proxy instead.

**Static assets.** Before, the layout referenced `bootstrap.min.css`, `bootstrap.bundle.min.js`, `site.css` and (unused) `site.js`, and every response was `Cache-Control: no-cache`, so a browser revalidated four files on every page view. Now the layout asks for `?v=hash` URLs (the hash changes when the file does) and those are served `public,max-age=31536000,immutable`, so a repeat visit sends no asset requests at all. Plain URLs keep `no-cache` with an ETag. This was found by checking the real response headers, not assumed; the `?v=` form alone was still `no-cache`. See `perf/static-headers.txt` (taken before the fix) and `Versioned_assets_are_cached_for_a_year...` in the tests.

**Image size.** RAWG serves full-size covers (often over a megabyte). Cards now ask RAWG's CDN for 420 px and the details cover for 640 px (`ImageUrls`), and the page falls back to the original if the smaller address fails to load. **Checked against live RAWG** after the first import: for one imported cover, the original was 95 KB and the 420 px address (`/media/resize/420/-/...`) returned 200 at 21 KB, about a fifth of the size. That is one cover, not a survey, and covers vary a lot in size. The fallback means a wrong address would still cost bandwidth only, never a broken picture.

## Load testing a deployed site

[`perf/catalogue.k6.js`](../perf/catalogue.k6.js) warms ten pages once, then ramps to 20 virtual users for about two minutes and asserts `warm_page_duration p(95) < 300 ms`, which is the spec's target. **k6 is not installed here and there is no deployed site, so it has not been run.** The local numbers above (p95 19.2 ms with the cache on, 123.7 ms off) are on the same machine as the database; a deployed run will include network latency and a cold database, so write its result into this file when it exists.

## Reproduce

```powershell
dotnet build -c Release
dotnet run --project benchmarks/GameVault.Benchmarks -c Release -- seed 5000      # creates GameVault_Perf (a throwaway database)
dotnet run --project benchmarks/GameVault.Benchmarks -c Release -- queries "label" # the tables above
dotnet run --project benchmarks/GameVault.Benchmarks -c Release -- bdn            # the similarity benchmark (about 10 minutes)
# a load run against the site started with ConnectionStrings__GameVault pointing at GameVault_Perf:
dotnet run --project benchmarks/GameVault.Benchmarks -c Release -- load http://127.0.0.1:5098 4000 16 br / /games "/games?sort=TopRated"
```

Set `GAMEVAULT_PERF_CONNECTION` to point the tool at another server. The raw outputs behind this page: `perf/queries-before.txt` (M5 code, no sort indexes), `perf/queries-after.txt`, `perf/similarity-bdn.txt`, `perf/load-cache-on.txt`, `perf/load-cache-off.txt`, `perf/load-weight.txt`, `perf/static-headers.txt`.
