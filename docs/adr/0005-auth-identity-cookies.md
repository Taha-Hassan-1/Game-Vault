# 5. Authentication: ASP.NET Core Identity with cookies and roles

Status: accepted (2026-11-20)

**Context.** The site is server-rendered, so cookie authentication is the standard answer.

**Decision.** Identity with cookie sign-in and the roles Member, Moderator and Admin, wrapped in named policies (`CanModerate`, `StudioOwner`) so controllers state intent. The Unity feedback endpoint uses a separate API-key scheme (see 18), never the cookie.

**Consequences.** A protected page answers an anonymous visitor with a redirect to sign-in and a member without the role with a redirect to an access-denied page. Those are the browser forms of 401 and 403 and the tests assert them.

**Rejected.** Hand-rolled authentication; external login for version 1.
