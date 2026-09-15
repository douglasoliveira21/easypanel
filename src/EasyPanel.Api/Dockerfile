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

# Copia os manifests primeiro para maximizar o cache de restore. A lista
# precisa cobrir TODO projeto referenciado (direta ou transitivamente) por
# EasyPanel.Api — hoje isso inclui todos os módulos de domínio, já que
# Infrastructure os referencia todos (Fases 1-10). Mantenha esta lista
# sincronizada ao adicionar um módulo novo, ou `dotnet restore` falha
# procurando um .csproj que ainda não foi copiado.
COPY global.json Directory.Build.props ./
COPY src/EasyPanel.Api/EasyPanel.Api.csproj                             src/EasyPanel.Api/
COPY src/EasyPanel.Infrastructure/EasyPanel.Infrastructure.csproj       src/EasyPanel.Infrastructure/
COPY src/EasyPanel.Shared.Kernel/EasyPanel.Shared.Kernel.csproj         src/EasyPanel.Shared.Kernel/
COPY src/EasyPanel.Modules.Tenancy/EasyPanel.Modules.Tenancy.csproj     src/EasyPanel.Modules.Tenancy/
COPY src/EasyPanel.Modules.Identity/EasyPanel.Modules.Identity.csproj   src/EasyPanel.Modules.Identity/
COPY src/EasyPanel.Modules.Customers/EasyPanel.Modules.Customers.csproj src/EasyPanel.Modules.Customers/
COPY src/EasyPanel.Modules.Auditing/EasyPanel.Modules.Auditing.csproj   src/EasyPanel.Modules.Auditing/
COPY src/EasyPanel.Modules.Monitoring/EasyPanel.Modules.Monitoring.csproj src/EasyPanel.Modules.Monitoring/
COPY src/EasyPanel.Modules.Alerting/EasyPanel.Modules.Alerting.csproj   src/EasyPanel.Modules.Alerting/
COPY src/EasyPanel.Modules.Inventory/EasyPanel.Modules.Inventory.csproj src/EasyPanel.Modules.Inventory/
COPY src/EasyPanel.Modules.Ticketing/EasyPanel.Modules.Ticketing.csproj src/EasyPanel.Modules.Ticketing/
COPY src/EasyPanel.Modules.Contracts/EasyPanel.Modules.Contracts.csproj src/EasyPanel.Modules.Contracts/
COPY src/EasyPanel.Modules.Billing/EasyPanel.Modules.Billing.csproj     src/EasyPanel.Modules.Billing/
COPY src/EasyPanel.Modules.Reporting/EasyPanel.Modules.Reporting.csproj src/EasyPanel.Modules.Reporting/
COPY src/EasyPanel.Modules.Portal/EasyPanel.Modules.Portal.csproj       src/EasyPanel.Modules.Portal/

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

# Usuário não-root para reduzir a superfície de ataque: usa o usuário `app`
# já embutido na imagem oficial desde o .NET 8 (via $APP_UID), em vez de
# criar um novo com `adduser` — o comando não existe mais na base atual da
# imagem, e recriar o usuário é desencorajado pela própria Microsoft.
USER $APP_UID

COPY --from=build /app/publish ./

# O Kestrel escuta na porta 8080 dentro do contêiner; o proxy (Caddy) faz o
# roteamento externo (R1.7). Não expomos HTTPS aqui — TLS termina no Caddy.
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_RUNNING_IN_CONTAINER=true

EXPOSE 8080

ENTRYPOINT ["dotnet", "EasyPanel.Api.dll"]
