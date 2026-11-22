# 3. Project layout: Domain, Application, Infrastructure, Web

Status: accepted (2026-11-20)

**Context.** The code should show dependency direction and modularity without being over-engineered.

**Decision.** Four projects plus a test project. Dependencies point inward: Web and Infrastructure depend on Application, Application depends on Domain, and nothing depends on Web. Only Infrastructure talks to SQL Server, the file system and RAWG.

**Consequences.** Application code is unit-tested with substitutes and no database. Everything that needs a real server is covered by integration tests.

**Rejected.** One large project; a full microservice split.
