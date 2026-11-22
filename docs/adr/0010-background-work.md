# 10. Background work: a hosted BackgroundService

Status: accepted (2026-11-20)

**Context.** RAWG data needs a nightly refresh within a request budget.

**Decision.** `RawgSyncWorker` runs once a day inside the web process, refreshes the stalest rows first and then imports the next pages of popular games, stopping at 15,000 requests a month. Its page cursor is stored in the database.

**Consequences.** No extra infrastructure. If the site scales to several instances, the job should move to one of them or to a separate worker.

**Rejected.** Hangfire, Quartz.
