# Tutorial — Como colocar o EasyPanel para rodar

Guia passo a passo, do zero, para clonar, subir e usar o EasyPanel — backend
(.NET 10), infraestrutura (Docker) e o Agente Windows. Para a arquitetura e
decisões de design, ver [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md); para
detalhes de cada endpoint, [docs/API.md](docs/API.md); para o roteiro do que
vem depois, [ROADMAP.md](ROADMAP.md).

## Pré-requisitos

| Ferramenta | Versão | Uso |
|------------|--------|-----|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0.400+ (ver `global.json`) | Compilar/testar backend e Agente |
| [Docker](https://www.docker.com/) + Docker Compose | recente | Subir a stack completa (Postgres/Redis/MinIO/Caddy/observabilidade) |
| [Git](https://git-scm.com/) | qualquer | Clonar o repositório |
| PowerShell ou Bash | — | Executar os comandos abaixo |

O Agente Windows (`src/windows-client/`) só roda de fato em Windows, mas
**compila e testa** em qualquer SO com o .NET SDK (ver "Compilando o Agente
Windows" abaixo).

## 1. Obter o código

```bash
git clone https://github.com/douglasoliveira21/easypanel.git
cd easypanel
```

## 2. Subir a stack completa com Docker Compose (recomendado para começar)

Isso sobe API + PostgreSQL + Redis + MinIO + Caddy + observabilidade
(Prometheus/Loki/Grafana) de uma vez, exatamente como em produção.

```bash
cd deploy
cp .env.example .env
```

Edite `deploy/.env` e preencha pelo menos:

- `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `MINIO_ROOT_PASSWORD` — qualquer
  senha forte para uso local.
- `JWT_SIGNING_KEY` — uma string aleatória com pelo menos 32 bytes (ex.:
  `openssl rand -base64 48` ou `[System.Web.Security.Membership]::GeneratePassword(48,0)` no PowerShell).
- `BOOTSTRAP_SUPERADMIN_EMAIL` e `BOOTSTRAP_SUPERADMIN_PASSWORD` — credenciais
  do primeiro usuário Super Admin, criado automaticamente na inicialização
  (política de senha: mínimo 8 caracteres, maiúscula+minúscula+dígito).

Em desenvolvimento local, `SITE_ADDRESS=:80` (padrão do `.env.example`) já
funciona sem domínio/TLS real.

Suba a stack:

```bash
docker compose up -d --build
```

A API aplica as migrações pendentes automaticamente na inicialização. Aguarde
e verifique a saúde:

```bash
curl http://localhost/health/ready
```

Resposta `200 OK` (ou `Healthy` no corpo) confirma que API, Postgres, Redis e
MinIO estão de pé.

> **Serviços expostos:** só `caddy` (portas 80/443) é publicado no host por
> padrão — os demais ficam na rede interna `backend`/`edge` do Compose (mesmo
> padrão de produção). Para inspecionar Postgres/Redis/MinIO/Grafana
> diretamente durante o desenvolvimento, publique as portas correspondentes
> em `docker-compose.override.yml` (já aplicado automaticamente) ou acesse via
> `docker compose exec <serviço> ...`.

## 3. Primeiro acesso — criar o tenant e o primeiro Administrador

O EasyPanel é multi-tenant: o Bootstrap cria apenas o **Super Admin da
plataforma** (sem tenant — ele opera *entre* tenants). Para o primeiro
cliente/empresa usar o sistema, é preciso criar um **Tenant** e o primeiro
usuário `Administrador` dentro dele. Hoje isso não tem endpoint dedicado (ver
[ROADMAP.md](ROADMAP.md), Fase 12) — o procedimento é:

### 3.1 Criar a linha do Tenant

```bash
docker compose exec postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB" -c \
  "INSERT INTO \"Tenants\" (\"Id\", \"Name\", \"Slug\", \"IsActive\", \"CreatedAt\") \
   VALUES (gen_random_uuid(), 'Minha Empresa', 'minha-empresa', true, now()) \
   RETURNING \"Id\";"
```

Anote o `Id` (UUID) retornado — é o `<TENANT_ID>` usado a seguir.

### 3.2 Login como Super Admin

```bash
curl -s -X POST http://localhost/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"<BOOTSTRAP_SUPERADMIN_EMAIL>","password":"<BOOTSTRAP_SUPERADMIN_PASSWORD>"}'
```

Guarde o `accessToken` da resposta — é o `<SUPERADMIN_TOKEN>` a seguir.

### 3.3 Criar o primeiro Administrador do tenant

O Super Admin opera num tenant específico passando o cabeçalho
`X-Admin-Tenant-Id` (único caso em que esse cabeçalho é honrado — ver
`docs/SECURITY.md`):

```bash
curl -s -X POST http://localhost/api/v1/users \
  -H "Authorization: Bearer <SUPERADMIN_TOKEN>" \
  -H "X-Admin-Tenant-Id: <TENANT_ID>" \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@minha-empresa.com","password":"Str0ng!Passw0rd","roles":["Administrador"]}'
```

Pronto: a partir daqui, faça login normalmente como
`admin@minha-empresa.com` — o token já carrega o `tenant_id` correto — e use
a API sem precisar mais do cabeçalho administrativo.

## 4. Usando a API (exemplos rápidos)

```bash
# Login do Administrador do tenant
TOKEN=$(curl -s -X POST http://localhost/api/v1/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@minha-empresa.com","password":"Str0ng!Passw0rd"}' \
  | jq -r .accessToken)

# Criar um Cliente (Customer)
curl -s -X POST http://localhost/api/v1/customers \
  -H "Authorization: Bearer $TOKEN" -H "Content-Type: application/json" \
  -d '{"razaoSocial":"Cliente Exemplo Ltda","cnpj":"11222333000181"}'

# Listar impressoras do tenant
curl -s http://localhost/api/v1/printers -H "Authorization: Bearer $TOKEN"

# Painel de visão geral (Fase 9)
curl -s http://localhost/api/v1/reports/dashboard -H "Authorization: Bearer $TOKEN"
```

Veja o catálogo completo de endpoints (todas as 10 fases, incluindo o Portal
do Cliente) em [docs/API.md](docs/API.md). Em desenvolvimento, o Swagger UI
também está disponível (ver `docs/DEPLOYMENT.md`).

## 5. Desenvolvimento local sem Docker (backend)

Para iterar rápido no código sem rebuildar imagem a cada mudança:

```bash
# Suba só a infraestrutura de apoio (Postgres/Redis/MinIO) via Docker...
cd deploy
docker compose up -d postgres redis minio

# ...e rode a API diretamente com o SDK, na raiz do repo:
cd ..
dotnet run --project src/EasyPanel.Api
```

Configure a connection string localmente (variáveis de ambiente ou
`src/EasyPanel.Api/appsettings.Development.json`, nunca versionado com
segredos reais):

```bash
export Database__ConnectionString="Host=localhost;Port=5432;Database=easypanel;Username=easypanel;Password=<sua senha do .env>"
export Jwt__SigningKey="uma-chave-de-desenvolvimento-com-32-bytes-ou-mais"
```

### Migrações EF Core

```bash
# Criar uma nova migração após alterar entidades
dotnet ef migrations add <Nome> \
  --project src/EasyPanel.Infrastructure \
  --startup-project src/EasyPanel.Infrastructure

# Verificar que o modelo não tem mudanças pendentes (gate obrigatório antes de commit)
dotnet ef migrations has-pending-model-changes \
  --project src/EasyPanel.Infrastructure \
  --startup-project src/EasyPanel.Infrastructure
```

## 6. Rodando os testes

```bash
dotnet build EasyPanel.sln     # deve terminar com 0 Aviso(s) / 0 Erro(s)
dotnet test EasyPanel.sln      # unit + integration (SQLite in-memory) + security
```

Baseline atual (após a Fase 10): **151 unit + 65 security + 375 integration**,
100% verde. Nenhuma tarefa é considerada concluída neste projeto sem essa
suíte passando.

## 7. Compilando o Agente Windows

O Agente (`src/windows-client/EasyPanel.WindowsClient.slnx`) é uma
solução-satélite **independente** do backend — compila e testa em qualquer SO
com o .NET SDK, mas só *executa como serviço Windows* em Windows.

```bash
cd src/windows-client

# Build (equivalente ao "compilar o agent")
dotnet build EasyPanel.WindowsClient.slnx

# Testes de unidade do Agente
dotnet test EasyPanel.WindowsClient.slnx
```

Saída esperada: `Compilação com êxito. 0 Aviso(s) 0 Erro(s)` e todos os
testes de `EasyPanel.WindowsClient.Tests` verdes.

### Gerar um executável standalone (publish)

Para gerar um binário que roda numa máquina Windows **sem exigir o .NET
Runtime instalado** (self-contained, arquivo único):

```bash
dotnet publish EasyPanel.WindowsClient/EasyPanel.WindowsClient.csproj \
  -c Release \
  -r win-x64 \
  --self-contained true \
  -p:PublishSingleFile=true \
  -o ./publish
```

O executável sai em `./publish/EasyPanel.WindowsClient.exe`. Configure
`appsettings.json` (seção `Agent`: `BackendBaseUrl`, `ProvisioningKey`, etc.
— ver [docs/CLIENT.md](docs/CLIENT.md)) ao lado do executável antes de
rodar/instalar.

> **Empacotamento como Windows Service, assinatura de código e um pipeline
> de release automatizado** ainda não existem — são o escopo da **Fase 16**
> do [ROADMAP.md](ROADMAP.md). Por ora, o binário publicado acima pode ser
> registrado manualmente como serviço com `sc.exe create` ou executado como
> console em desenvolvimento.

## 8. Deploy através do painel EasyPanel.io (sua VPS)

> **Atenção com o nome:** este é o [EasyPanel.io](https://easypanel.io), o
> painel self-hosted de deploy (estilo Coolify/CapRover) que já roda na sua
> VPS — diferente do **produto** deste repositório, que também se chama
> "EasyPanel" por coincidência de nome. Nesta seção, "o painel" sempre se
> refere ao easypanel.io.

O repositório já está preparado para isso: `deploy/docker-compose.easypanel.yml`
é uma variação de `docker-compose.yml` **sem o serviço `caddy`** e **sem
nenhuma porta publicada no host**. Isso é necessário porque o painel
EasyPanel.io já roda seu próprio proxy reverso (Traefik) nas portas 80/443 do
servidor — se o nosso Caddy tentasse publicar essas mesmas portas, o deploy
falharia por conflito. O roteamento público (domínio, TLS, path) passa a ser
configurado dentro do próprio painel, na aba **Domains** de cada serviço.

### 8.1 Criar o serviço Compose no painel

1. No painel, abra (ou crie) um **Project** para o EasyPanel (o produto).
2. **New Service → Compose**.
3. Em **Source**, escolha **Git** e preencha:
   - **Repository:** `https://github.com/douglasoliveira21/easypanel.git`
   - **Branch:** `main`
   - **Build Path:** `/deploy`
   - **Docker Compose File:** `docker-compose.easypanel.yml`

   O repositório é público, então nenhuma chave de deploy é necessária.

4. O painel deve detectar automaticamente `deploy/.env.example` e pré-popular
   o editor de variáveis de ambiente. Preencha (mesmos valores explicados no
   passo 2 deste tutorial):
   - `POSTGRES_PASSWORD`, `REDIS_PASSWORD`, `MINIO_ROOT_PASSWORD`
   - `JWT_SIGNING_KEY` (≥ 32 bytes aleatórios)
   - `BOOTSTRAP_SUPERADMIN_EMAIL` / `BOOTSTRAP_SUPERADMIN_PASSWORD`
   - `GRAFANA_ADMIN_PASSWORD`
   - **Não é preciso preencher** `SITE_ADDRESS`, `ACME_EMAIL`, `HTTP_PORT`,
     `HTTPS_PORT` — esse arquivo não usa mais o Caddy; o painel cuida disso.

5. Clique em **Deploy**. O painel executa `docker compose up --build -d`,
   construindo as imagens `api` (a partir de `src/EasyPanel.Api/Dockerfile`)
   e `frontend`, e subindo Postgres/Redis/MinIO/observabilidade junto.

### 8.2 Expor a API e o frontend publicamente (Domains)

Depois que os containers estiverem no ar, configure os **Domains** do
serviço Compose (isso substitui o roteamento que o Caddyfile fazia):

| Hostname | Path | Serviço interno | Porta |
|----------|------|------------------|-------|
| `seudominio.com` (ou subdomínio) | `/api` | `api` | `8080` |
| `seudominio.com` (mesmo host) | `/health` | `api` | `8080` |
| `seudominio.com` (mesmo host) | `/` (restante) | `frontend` | `80` |

Isso reproduz exatamente o roteamento do `deploy/Caddyfile` original
(`/api/*` e `/health/*` → backend, resto → frontend), mas via Traefik do
painel. Se preferir simplicidade antes do frontend real existir, aponte só
um Domain para `api:8080` e use a API diretamente.

Opcional: adicione um Domain para o serviço `grafana` (porta `3000`) se
quiser os dashboards acessíveis publicamente (proteja com Basic Auth, opção
disponível na mesma aba do painel).

### 8.3 Verificar e criar o primeiro tenant

```bash
curl https://seudominio.com/health/ready
```

Depois, repita exatamente o **passo 3** deste tutorial ("Primeiro acesso —
criar o tenant e o primeiro Administrador"), trocando `http://localhost` pelo
seu domínio. Para rodar o comando `psql` de inserção do Tenant, use o
terminal integrado do painel no serviço `postgres` (ou `docker exec` via SSH
na VPS) em vez de `docker compose exec` local.

### 8.4 Deploy automático a cada push (liga com o CI já existente)

O painel expõe uma **Deployment Trigger URL** por serviço. Cadastre-a como
webhook no repositório GitHub (Settings → Webhooks) para que cada push na
`main` — já validado pelo `backend-ci.yml` (ver seção 6) — dispare um novo
deploy automaticamente no seu servidor. Isso fecha o ciclo: push → CI verde
→ deploy real, sem passo manual.

## 9. Indo para produção/staging (sem o painel, Docker Compose direto)

O guia operacional completo (backup, observabilidade, CI/CD sugerido, pontos
de escala) está em [docs/DEPLOYMENT.md](docs/DEPLOYMENT.md). Resumo do
caminho: gerar segredos fortes e únicos por ambiente (nunca reaproveitar os
de desenvolvimento), apontar `SITE_ADDRESS`/`ACME_EMAIL` para um domínio real
(o Caddy emite TLS automaticamente via Let's Encrypt), subir com
`docker compose -f docker-compose.yml -f docker-compose.prod.yml up -d`, e
repetir o passo 3 deste tutorial para criar o primeiro tenant real.

Automatizar esse fluxo (CI no GitHub Actions + deploy automático) é
justamente a **Fase 11/12** do [ROADMAP.md](ROADMAP.md) — ainda não
implementada.

## Solução de problemas comuns

| Sintoma | Causa provável | Solução |
|---------|-----------------|---------|
| `health/ready` nunca fica OK | Postgres/Redis/MinIO ainda inicializando, ou senha errada no `.env` | `docker compose logs api` para ver o erro exato |
| Login retorna 401 | Email/senha errados, ou usuário criado sem `IsActive` | Confirme o Bootstrap rodou (`docker compose logs api \| grep -i bootstrap`) |
| `POST /api/v1/users` retorna 403 | Faltou `X-Admin-Tenant-Id` no primeiro acesso (passo 3.3), ou o ator não tem `user.manage` | Revise o passo 3; papéis/permissões em `docs/SECURITY.md` |
| `dotnet ef migrations has-pending-model-changes` acusa mudança | Uma entidade foi alterada sem gerar migração | Rode `dotnet ef migrations add <Nome>` antes de commitar |
| Build do Agente falha em Linux/Mac | Normal para *rodar* como serviço; build/testes funcionam em qualquer SO | Só a publicação/instalação final exige Windows |
