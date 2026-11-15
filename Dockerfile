FROM node:24-alpine AS web
WORKDIR /source/src/GateMint.Web
COPY src/GateMint.Web/package*.json ./
RUN npm ci
COPY src/GateMint.Web/ ./
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source
COPY GateMint.slnx dotnet-tools.json ./
COPY src/GateMint.Core/GateMint.Core.csproj src/GateMint.Core/
COPY src/GateMint.Infrastructure/GateMint.Infrastructure.csproj src/GateMint.Infrastructure/
COPY src/GateMint.Api/GateMint.Api.csproj src/GateMint.Api/
RUN dotnet restore src/GateMint.Api/GateMint.Api.csproj
COPY src/GateMint.Core/ src/GateMint.Core/
COPY src/GateMint.Infrastructure/ src/GateMint.Infrastructure/
COPY src/GateMint.Api/ src/GateMint.Api/
RUN dotnet publish src/GateMint.Api/GateMint.Api.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /keys \
    && chown app:app /keys
COPY --chown=app:app --from=build /app ./
COPY --chown=app:app --from=web /source/src/GateMint.Web/dist ./wwwroot
USER app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GateMint.Api.dll"]
