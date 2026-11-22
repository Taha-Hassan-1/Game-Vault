# 15. Reviews: members delete their own, moderators only hide

Status: accepted (2026-11-22)

**Context.** The spec says members can edit and delete their review, and also that reviews are never hard-deleted.

**Decision.** A member deleting their own review removes it outright (their own data). Moderators never delete: hiding keeps the review so it can be restored, removes it from the page and the rating, and resolves its reports.

**Consequences.** One review per member per game still holds, because a hidden review keeps its row.
