# =============================================================================
# Dockerfile multi-stage para o backend EasyPanel.Api (.NET 10 / ASP.NET Core)
# Requisito R1.1: distribuir o backend com definições de contêiner.
#
# O build assume o CONTEXTO na RAIZ do repositório (ver `context` no
# docker-compose.yml), de forma que a solução completa e os projetos
# referenciados estejam disponíveis para restauração/compilação.
# =============================================================================

# ---- Estágio de build --------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copia os manifests primeiro para maximizar o cache de restore.
COPY global.json Directory.Build.props ./
COPY src/EasyPanel.Api/EasyPanel.Api.csproj                       src/EasyPanel.Api/
COPY src/EasyPanel.Infrastructure/EasyPanel.Infrastructure.csproj src/EasyPanel.Infrastructure/
COPY src/EasyPanel.Modules.Identity/EasyPanel.Modules.Identity.csproj   src/EasyPanel.Modules.Identity/
COPY src/EasyPanel.Modules.Tenancy/EasyPanel.Modules.Tenancy.csproj     src/EasyPanel.Modules.Tenancy/
COPY src/EasyPanel.Modules.Customers/EasyPanel.Modules.Customers.csproj src/EasyPanel.Modules.Customers/
COPY src/EasyPanel.Modules.Auditing/EasyPanel.Modules.Auditing.csproj   src/EasyPanel.Modules.Auditing/
COPY src/EasyPanel.Shared.Kernel/EasyPanel.Shared.Kernel.csproj         src/EasyPanel.Shared.Kernel/

RUN dotnet restore src/EasyPanel.Api/EasyPanel.Api.csproj

# Copia o restante do código-fonte e publica em modo Release.
COPY src/ src/
RUN dotnet publish src/EasyPanel.Api/EasyPanel.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ---- Estágio de runtime ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# Usuário não-root para reduzir a superfície de ataque.
RUN adduser --disabled-password --gecos "" --uid 5678 appuser
USER appuser

COPY --from=build /app/publish ./

# O Kestrel escuta na porta 8080 dentro do contêiner; o proxy (Caddy) faz o
# roteamento externo (R1.7). Não expomos HTTPS aqui — TLS termina no Caddy.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "EasyPanel.Api.dll"]
