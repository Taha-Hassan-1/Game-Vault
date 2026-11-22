# Architecture

GameVault is four projects with dependencies pointing inward, so business rules never depend on EF Core, RAWG or MVC. This page shows the shape, the main flows, the data model and where each design pattern lives. The reasons behind the choices are in [adr/](adr/README.md).

## The layers

```mermaid
flowchart LR
    Browser([Browser]) --> Web
    Unity([Unity build]) -->|X-Api-Key| Web

    subgraph Web[GameVault.Web]
      Controllers[Controllers and Razor views]
      Api[Feedback API]
      Pipeline[Security headers, errors, rate limits, auth]
    end

    subgraph App[GameVault.Application]
      Services[Services: catalogue, reviews, lists, studio, feedback, suggestions]
      Ports[Interfaces: repositories, IGameMetadataProvider, IImageStore]
    end

    subgraph Infra[GameVault.Infrastructure]
      EF[EF Core repositories and migrations]
      Cache[CachedGameCatalog, HybridCache]
      Rawg[RawgGameProvider, resilience, budget]
      Files[FileImageStore]
      Worker[RawgSyncWorker]
    end

    Domain[GameVault.Domain: entities and rules]

    Web --> App
    Web --> Infra
    Infra --> App
    App --> Domain
    EF --> SQL[(SQL Server)]
    Rawg --> RAWG((RAWG API))
    Files --> Disk[(Uploads folder)]
```

Dependency rule: `Domain` depends on nothing; `Application` depends only on `Domain`; `Infrastructure` implements the interfaces `Application` defines; `Web` wires everything together in `Program.cs` and `Infrastructure/DependencyInjection.cs`. Only `Infrastructure` talks to SQL Server, the file system and RAWG, so every service in `Application` is unit-tested with substitutes and no database.

## Flows

### A review changes a game's rating

```mermaid
sequenceDiagram
    participant U as Member
    participant C as ReviewsController
    participant S as ReviewService
    participant R as IReviewRepository
    participant D as Event dispatcher
    participant H as RatingRecalculator
    participant K as Catalogue cache

    U->>C: POST /games/{slug}/reviews
    C->>S: SaveAsync(user, slug, stars, body)
    S->>R: add or edit the member's one review, save
    S->>D: ReviewChanged(gameId)
    D->>H: HandleAsync
    H->>R: average and count of visible reviews
    H->>R: write RatingAverage and RatingCount on Game
    H->>K: invalidate (tag "catalogue")
    C-->>U: redirect to the game page
```

The review code never mentions ratings: it raises an event and a handler reacts (the Observer pattern). List pages read the stored average and never aggregate reviews.

### A game page when RAWG is slow or down

```mermaid
sequenceDiagram
    participant U as Visitor
    participant C as GamesController
    participant I as GameImportService
    participant P as RawgGameProvider
    participant B as Budget handler
    participant X as RAWG
    participant Cat as Catalogue (cached)

    U->>C: GET /games/{slug}
    C->>I: EnsureDetailsAsync(slug)
    alt details missing or older than 30 days
        I->>P: GetByExternalIdAsync
        P->>B: each attempt is counted against the monthly budget
        B->>X: request (3 retries, 10 s per attempt, circuit breaker)
        X--xP: fails or times out
        P--xI: exception
        I->>I: log a warning, swallow it
    end
    C->>Cat: GetDetailsAsync(slug)
    Cat-->>C: the stored copy
    C-->>U: 200, never a 500 because RAWG failed
```

### A Unity build sends feedback

```mermaid
sequenceDiagram
    participant G as Unity build
    participant L as Rate limiter
    participant A as ApiKeyAuthenticationHandler
    participant K as ApiKeyService
    participant F as FeedbackService
    participant DB as FeedbackMessages

    G->>L: POST /api/v1/feedback, X-Api-Key
    L->>L: per-address cap, then per-key cap (429 + Retry-After)
    L->>A: authenticate
    A->>K: SHA-256 of the key, look it up (active keys only)
    K-->>A: game id, or nothing (401 problem+json)
    A->>F: submit(gameId, category, body, appVersion, playerRef)
    F->>F: validate every field (400 with all errors listed)
    F->>DB: insert, status New, source Api
    F-->>G: 201 { "id": ... }
```

The studio reads the same table in `/studio/inbox` next to feedback typed into the website.

### RAWG sync (nightly)

`RawgSyncWorker` waits a minute after start, then once a day calls `GameImportService.RunSyncAsync`: refresh the stalest rows first, then import the next pages of popular games, saving the page cursor in `SyncState`. It stops as soon as the month's sync budget (15,000 requests) is spent and leaves the rest of the 20,000 for on-demand fetches.

## Data model

```mermaid
erDiagram
    Game ||--o{ Screenshot : has
    Game }o--o{ Genre : "GameGenre"
    Game }o--o{ Platform : "GamePlatform"
    Game }o--o{ Tag : "GameTag"
    Game ||--o{ Review : receives
    AppUser ||--o{ Review : writes
    Review ||--o{ ReviewReport : "reported by members"
    AppUser ||--o{ ListEntry : keeps
    Game ||--o{ ListEntry : "wishlist or played"
    Game ||--o{ Suggestion : "studio games only"
    AppUser ||--o{ Suggestion : posts
    Suggestion ||--o{ SuggestionVote : "one per member"
    Game ||--o{ FeedbackMessage : "web or api"
    Game ||--o{ ApiKey : "hash only"

    Game {
      int Id
      string Slug "unique, never changes"
      string Title
      GameSource Source "Rawg or Studio"
      string ExternalId "unique with Source"
      double RatingAverage "denormalised"
      int RatingCount "denormalised"
      datetime ExternalFetchedAt
      datetime DetailsFetchedAt
    }
    Review {
      int Stars "1 to 5"
      bool IsHidden "moderators hide, never delete"
    }
    ApiKey {
      string KeyPrefix
      string KeyHash "SHA-256, unique"
      datetime RevokedAt
    }
```

Two more tables support the background job: `ApiUsage` (requests per provider per day, for the budget) and `SyncState` (the import cursor). Rules that hold across tables: all timestamps are UTC; a review is unique per (game, member); a vote is unique per (suggestion, member) and `VoteCount` is recounted from the vote rows in the same transaction; RAWG-sourced rows are a cache and carry `ExternalFetchedAt`.

## Where each design pattern lives

| Pattern | Where | Why |
|---|---|---|
| Repository | `IGameRepository`, `IReviewRepository`, `IListRepository`, `IStudioGameRepository`, `IFeedbackRepository`, `IApiKeyRepository`, `ISuggestionRepository` in `Application`; EF classes in `Infrastructure/Persistence` | Queries sit behind interfaces so services are tested with substitutes. `DbContext` is already a unit of work, so repositories exist only where a service needs one. |
| Strategy | `ISimilarityStrategy` and `TagOverlapSimilarity` (`Application/Catalogue`) | How "similar games" is computed can change without touching callers. |
| Decorator | `CachedGameCatalog` wraps `GameCatalog` (`Infrastructure/Caching`) | Caching stays out of business code and is removed by one registration line. |
| Factory | `GameMetadataProviderFactory` (`Application/Metadata/Contracts.cs`) | A second provider (IGDB) can be added without changing callers. |
| Observer | `IDomainEventDispatcher`, `ReviewChanged`, `SuggestionVoted`, `RatingRecalculator` (`Application/Events`, `Application/Reviews`) | The review code does not know that ratings exist. |
| Options | `RawgOptions`, `SyncBudgetOptions`, `UploadsOptions`, validated at startup | A bad setting fails at start, not on the first request. |
| Dependency injection | `Program.cs` and `Infrastructure/DependencyInjection.cs` | One place wires everything; every class gets its dependencies through its constructor. |

## Caching

`HybridCache` holds search results, game details, facets, the home page and similar games for five minutes, all under one tag. Anything that changes what a page shows (a review, a hide, a studio edit, an upload, a RAWG import) calls `InvalidateAsync`, which removes that tag. Per-visitor data (their lists, their review) is never cached: it is loaded beside the shared page model. Query counts per page are pinned by `QueryBudgetTests`.

## Security in the request pipeline

Order of middleware in `Program.cs`: request logging, security headers, error handling (a problem document for `/api`, a friendly page for the site), friendly status pages, compression, HTTPS redirect, uploads, routing, rate limiting, authentication, authorization. The Content Security Policy forbids inline script and style, which the code honours everywhere. Details and the reasons: ADR 14, 17 and 18, and the "Security" section of the README.

## Tests

| Level | Where | What it proves |
|---|---|---|
| Unit | `tests/GameVault.Tests/Unit` | Rules in `Application` and `Domain`, with substitutes: services, validation, CSV, keys, budget, health checks, image sniffing, file store |
| Provider | `Rawg/RawgProviderTests.cs` | Mapping, retries on 429 and 5xx, timeouts, the budget, with a fake HTTP handler |
| Integration | `tests/GameVault.Tests/Integration` | The real site over HTTP on a real SQL Server (a throwaway database per class): flows, authorization, security headers, query budgets |
