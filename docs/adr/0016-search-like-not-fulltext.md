# 16. Search: SQL LIKE, with full-text as a documented option

Status: accepted (2026-11-22)

**Context.** The spec calls for SQL full-text search. The Full-Text feature is not installed on the SQL Server Express used for development, so a full-text index could not be built or tested here, and tests would depend on index population timing.

**Decision.** Search uses `LIKE '%term%'` on title and description. Measured on 5,000 games it takes about 28 ms (docs/PERFORMANCE.md), growing linearly with the catalogue. `docs/sql/fulltext.sql` holds the optional full-text setup for a server that has the feature, but it is not wired into the app.

**Consequences.** Fine up to tens of thousands of games. Beyond that, enable full-text and switch the query to `CONTAINS`, or search titles only.
