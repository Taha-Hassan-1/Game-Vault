# 8. HTTP resilience: the standard resilience handler

Status: accepted (2026-11-20)

**Context.** RAWG will sometimes be slow, rate-limit or fail.

**Decision.** A typed `HttpClient` with `Microsoft.Extensions.Http.Resilience`: three retries with exponential backoff and jitter that honour Retry-After, 10 seconds per attempt, 30 seconds in total, and a circuit breaker. A budget handler sits inside the retry handler so every attempt counts against RAWG's monthly allowance.

**Consequences.** A RAWG outage never produces a 500: pages render from the stored copy and the failure is logged. Health checks report RAWG separately and as Degraded only.

**Rejected.** Hand-written retry loops.
