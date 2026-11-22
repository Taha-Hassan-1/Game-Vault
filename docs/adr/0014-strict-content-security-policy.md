# 14. A strict Content Security Policy with no inline code

Status: accepted (2026-11-22)

**Context.** Any page that renders text from strangers (reviews, suggestions, feedback) is a place where script injection could happen. Razor encodes output, but a policy is the second line of defence.

**Decision.** Every response carries `script-src 'self'; style-src 'self'` with no `'unsafe-inline'`. The app therefore has no inline scripts, event handlers or style attributes: cover colours are twelve CSS classes, the one-time key field selects itself through a data attribute handled in `site.js`, and form errors use a hand-written partial because `asp-validation-summary="All"` emits a hidden `style="display:none"` list item. A test crawls every page for inline code so it cannot creep back.

**Consequences.** Images may come from any https host because RAWG's CDN serves covers. Anything new that needs inline code must be rewritten instead.
