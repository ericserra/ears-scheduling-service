# ── build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore em camada própria: cache de pacotes sobrevive a mudanças de código.
COPY EARS.Integration.sln ./
COPY src/EARS.Integration.Domain/*.csproj          src/EARS.Integration.Domain/
COPY src/EARS.Integration.Application/*.csproj     src/EARS.Integration.Application/
COPY src/EARS.Integration.Infrastructure/*.csproj  src/EARS.Integration.Infrastructure/
COPY src/EARS.Integration.Api/*.csproj             src/EARS.Integration.Api/
COPY tests/EARS.Integration.UnitTests/*.csproj     tests/EARS.Integration.UnitTests/
RUN dotnet restore

COPY . .
RUN dotnet publish src/EARS.Integration.Api/EARS.Integration.Api.csproj \
    -c Release -o /app --no-restore

# ── runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Usuário sem privilégios
RUN adduser --disabled-password --gecos "" --uid 1001 ears && chown -R ears /app
USER ears

COPY --from=build --chown=ears /app .

ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    TZ=America/Sao_Paulo

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
  CMD ["/bin/sh", "-c", "curl -fsS http://localhost:8080/health || exit 1"]

ENTRYPOINT ["dotnet", "EARS.Integration.Api.dll"]
