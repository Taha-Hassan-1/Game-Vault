# 18. Feedback API: API keys, per-key and per-address limits

Status: accepted (2026-11-22)

**Decision.** `POST /api/v1/feedback` authenticates with an `X-Api-Key` header through its own authentication scheme. A key is 32 random bytes, belongs to one game and is shown once; only a SHA-256 hash and a short prefix are stored. Requests are limited per key (30 a minute) and per address across `/api` (120 a minute) so made-up keys do not buy extra requests. Errors are `application/problem+json`.

**Consequences.** A lost key is replaced, not recovered. Behind a proxy, forwarded headers must be enabled or every client shares one address.
