# 12. Hosting: a container image, host chosen at deploy time

Status: accepted (2026-11-20), open

**Context.** The resume needs a live URL. Free-tier terms change often.

**Decision.** The app ships as a Docker image built in CI. The host (a container host or Azure App Service with a managed SQL database) is chosen on the day, using docs/DEPLOYMENT.md.

**Consequences.** The site has not been deployed yet, so there is no live URL and the production Dockerfile has not been built or run on this machine.

**Rejected.** Choosing a host now.
