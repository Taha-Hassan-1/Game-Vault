# 11. Testing: xUnit, NSubstitute, WebApplicationFactory, a real SQL Server

Status: accepted (2026-11-20), amended (2026-11-22)

**Context.** The spec asked for Testcontainers so integration tests use a real SQL Server.

**Decision.** Application code is covered by fast unit tests with substitutes. Everything that touches the database or HTTP runs against a real SQL Server through `WebApplicationFactory`, each test class on its own throwaway database. Docker is not installed on the development machine, so the tests use the local SQL Server Express with Windows authentication, or any server named in `GAMEVAULT_TEST_CONNECTION` (CI uses a SQL Server service container this way).

**Consequences.** The same behaviour as Testcontainers without Docker. The in-memory EF provider is still avoided because it behaves differently from SQL Server.

**Rejected.** The in-memory EF provider.
