# 17. Studio uploads: content-checked, renamed, behind an interface

Status: accepted (2026-11-22)

**Decision.** Uploaded covers and screenshots are accepted by their first bytes (PNG, JPEG, WebP, up to 2 MB), never by file name or declared type. SVG is refused because it can contain script. Files get random names, live outside `wwwroot` and are served from `/uploads` with `nosniff`. Storage sits behind `IImageStore`, so blob storage can replace the local folder.

**Consequences.** On a host with an ephemeral disk the upload folder must be a mounted volume, or storage must move to blobs.
