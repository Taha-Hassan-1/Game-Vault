# 4. Database: SQL Server with EF Core 10

Status: accepted (2026-11-20)

**Context.** SQL Server is common in .NET teams and already used in TaskManager.

**Decision.** SQL Server, EF Core 10 and code-first migrations.

**Consequences.** Development and tests use the local SQL Server Express. That edition here has no Full-Text Search feature installed, so search uses `LIKE` (see 16). Migrations are applied at start in Development and by an explicit step in production.

**Rejected.** SQLite (it hides provider differences), PostgreSQL.
