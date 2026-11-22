# NOT built or run on the development machine (Docker is not installed there); CI builds it on every push to main.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY GameVault.slnx ./
COPY src/ src/
# Restore and publish only the web project and what it references; tests and benchmarks are not in the image.
RUN dotnet publish src/GameVault.Web -c Release -o /app --no-self-contained

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Uploaded studio images need a folder that survives restarts: mount a volume at /data.
RUN mkdir -p /data/uploads && chown -R app:app /data
VOLUME /data

ENV ASPNETCORE_URLS=http://+:8080 \
    Uploads__Path=/data/uploads \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true
EXPOSE 8080

# The official runtime image ships a non-root user named "app".
USER app
ENTRYPOINT ["dotnet", "GameVault.Web.dll"]
