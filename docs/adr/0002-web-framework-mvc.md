# 2. Web framework: ASP.NET Core MVC with Razor views

Status: accepted (2026-11-20)

**Context.** The project exists to show MVC and API integration work, and to stay focused on C#.

**Decision.** Server-rendered MVC with Razor views, partials, a view component and tag helpers, Bootstrap 5 and a few lines of vanilla JavaScript.

**Consequences.** No client-side framework to maintain. Pages need no JavaScript to work, which keeps the Content Security Policy simple (see 14).

**Rejected.** A single-page app (Angular is already used in TeamBoard).
