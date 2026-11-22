# 7. Metadata: RAWG behind an interface

Status: accepted (2026-11-20)

**Context.** RAWG has a free API key. Its "similar games" endpoint is paid-only. IGDB needs Twitch OAuth setup.

**Decision.** `IGameMetadataProvider` with `RawgGameProvider` as the one implementation, chosen through a factory. Similar games are computed locally from genre and tag overlap. RAWG rows are stored as a cache with `ExternalFetchedAt` and refreshed after 30 days.

**Consequences.** IGDB can be added as a second provider without touching callers. RAWG's terms do not mention caching, so the owner must confirm with RAWG before the first bulk import; the fallback is fetch-through caching only.

**Rejected.** IGDB for version 1.
