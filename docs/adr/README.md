# Architecture decision records

Short records of the decisions behind GameVault. 1 to 13 mirror the decision log in the spec; 14 onward were made while building.

- [1. Runtime: .NET 10 (LTS)](0001-runtime-dotnet-10.md)
- [2. Web framework: ASP.NET Core MVC with Razor views](0002-web-framework-mvc.md)
- [3. Project layout: Domain, Application, Infrastructure, Web](0003-project-layout.md)
- [4. Database: SQL Server with EF Core 10](0004-database-sql-server.md)
- [5. Authentication: ASP.NET Core Identity with cookies and roles](0005-auth-identity-cookies.md)
- [6. Cross-cutting reactions: a small in-process event dispatcher](0006-domain-events-in-process.md)
- [7. Metadata: RAWG behind an interface](0007-metadata-provider-rawg.md)
- [8. HTTP resilience: the standard resilience handler](0008-http-resilience.md)
- [9. Caching: HybridCache through a decorator](0009-caching-hybridcache-decorator.md)
- [10. Background work: a hosted BackgroundService](0010-background-work.md)
- [11. Testing: xUnit, NSubstitute, WebApplicationFactory, a real SQL Server](0011-testing-strategy.md)
- [12. Hosting: a container image, host chosen at deploy time](0012-hosting-container.md)
- [13. Repository: a new repo, not the GameList.Api stub](0013-repository.md)
- [14. A strict Content Security Policy with no inline code](0014-strict-content-security-policy.md)
- [15. Reviews: members delete their own, moderators only hide](0015-reviews-delete-and-hide.md)
- [16. Search: SQL LIKE, with full-text as a documented option](0016-search-like-not-fulltext.md)
- [17. Studio uploads: content-checked, renamed, behind an interface](0017-studio-uploads.md)
- [18. Feedback API: API keys, per-key and per-address limits](0018-feedback-api-keys.md)
