# 9. Caching: HybridCache through a decorator

Status: accepted (2026-11-20)

**Context.** Catalogue pages are read far more than written.

**Decision.** `CachedGameCatalog` wraps `GameCatalog` and caches with `HybridCache` for five minutes, with tag-based invalidation whenever a game, review or upload changes.

**Consequences.** Caching stays out of business code and is removed by one registration line. `Catalogue:CacheEnabled=false` does exactly that so the effect can be measured (see docs/PERFORMANCE.md). A Redis layer can be added later without changing callers.

**Rejected.** Raw `IMemoryCache` calls spread through services.
