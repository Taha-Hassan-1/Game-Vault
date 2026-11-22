# 1. Runtime: .NET 10 (LTS)

Status: accepted (2026-11-20)

**Context.** The other portfolio projects, TaskManager and the retired GameList.Api stub, already target net10.0.

**Decision.** GameVault targets .NET 10, the current long-term-support release.

**Consequences.** Package versions are pinned to 10.x where a newer major exists (for example EF Core and Identity), because the latest packages already target .NET 11 and refuse to restore into a net10.0 project.

**Rejected.** Older .NET versions.
