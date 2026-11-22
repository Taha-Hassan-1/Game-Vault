# GameVault

An ASP.NET Core MVC site (.NET 10) where visitors browse a game catalogue, rate and review games, and where the studio's own games get a built-in feedback loop. Built from the spec "GameVault: Spec & Implementation Plan".

## Status

| Milestone | State |
|---|---|
| M0 Setup | Done: solution, CI file, Dockerfile, compose (Docker files not run locally; Docker is not installed on the dev machine) |
| M1 Domain and data | Done: entities, EF configurations, `InitialCreate` migration, seed (31 games incl. Untangle) |
| M2 Catalogue | Done: home, search, genre/platform/tag filters, sort, paging (cap 24), details, similar games, caching decorator |
| M3 RAWG integration | Not started |
| M4 Accounts and reviews | Not started (schema and review display are in place) |
| M5 Studio feedback hub | Not started (schema is in place) |
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

## Tests

```powershell
dotnet test
```

Integration tests create and drop a throwaway database (`GameVault_Test_<guid>`) on `.\SQLEXPRESS`, or on the server in `GAMEVAULT_TEST_SERVER`. They use Windows authentication, so CI currently runs the unit tests only; Testcontainers would replace this once Docker is available.

## Known gaps against the spec

- Search uses SQL `LIKE`, not full-text search (needs the Full-Text feature, which is not confirmed on SQL Express here).
- Secrets and the RAWG key will use user-secrets and environment variables; nothing is stored in the repo.
