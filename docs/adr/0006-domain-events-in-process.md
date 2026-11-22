# 6. Cross-cutting reactions: a small in-process event dispatcher

Status: accepted (2026-11-20)

**Context.** Rating averages must follow review changes without the review code knowing about ratings.

**Decision.** Plain application services plus a small dispatcher: publishers raise `ReviewChanged` or `SuggestionVoted`, handlers registered in dependency injection react (the Observer pattern).

**Consequences.** About twenty lines of code, easy to test. MediatR moved to a dual commercial licence in 2025, so avoiding it also avoids a licence question.

**Rejected.** MediatR.
