# Implantação — EasyPanel (Fase 1)

> **Deploy via painel EasyPanel.io (self-hosted, numa VPS):** ver
> [TUTORIAL.md](../TUTORIAL.md), seção 8 — usa
> `deploy/easypanel/docker-compose.yml` (sem Caddy nem MinIO — o painel roda
> seu próprio proxy reverso e o storage usa o provedor `Local`, um volume
> persistente) e é o caminho recomendado quando a VPS já tem o painel
> instalado. Esta página (`docs/DEPLOYMENT.md`) documenta o caminho genérico
> com Docker Compose + Caddy próprio, para quem não usa esse painel.

## Componentes (Docker Compose)

O arquivo `deploy/docker-compose.yml` provisiona: `api`, `frontend`, `postgres`,
`redis`, `minio`, `caddy` (reverse proxy + TLS), `otel-collector`, `prometheus`,
`loki`, `grafana`. Overrides por ambiente:

- `docker-compose.override.yml` — development (aplicado por padrão)
- `docker-compose.staging.yml` — staging
- `docker-compose.prod.yml` — production

## Configuração

Copie `deploy/.env.example` para `deploy/.env` e preencha com segredos reais
(o `.env` é ignorado no git — R1.6). Chaves principais:

| Variável | Descrição |
|----------|-----------|
| `ASPNETCORE_ENVIRONMENT` | `Development` / `Staging` / `Production` |
| `SITE_ADDRESS`, `ACME_EMAIL` | Endereço/e-mail para TLS automático no Caddy |
| `POSTGRES_*` | Banco de dados |
| `REDIS_PASSWORD` | Redis |
| `MINIO_*` | Armazenamento S3-compatible |
| `JWT_ISSUER`, `JWT_AUDIENCE`, `JWT_SIGNING_KEY` | Autenticação (chave ≥ 32 bytes) |
| `GRAFANA_*` | Grafana |

A aplicação lê configuração de variáveis de ambiente / arquivos por ambiente.
Seções relevantes do `appsettings`: `Database`, `Redis`, `Storage`, `Jwt`,
`RateLimiting`, `Bootstrap`.

### Bootstrap (primeiro acesso)

A seção `Bootstrap` controla o seed de inicialização:

- `SeedRolesOnStartup` (padrão `true`) — semeia os 8 papéis (idempotente).
- `SuperAdminEmail` + `SuperAdminPassword` — se **ambos** forem fornecidos, um
  usuário Super Admin (sem tenant) é criado na inicialização para onboarding. Sem
  eles, nenhum usuário é criado (sem dados fictícios). Forneça a senha por segredo.

O seed só executa quando `Database:ApplyMigrationsOnStartup=true` (implantações
reais); hosts de teste o ignoram.

## Subir

```bash
cd deploy
cp .env.example .env      # e preencha os segredos
docker compose up -d
```

Na inicialização, a API aplica as migrações pendentes (serializadas por advisory
lock do PostgreSQL) antes de aceitar tráfego. Verifique a saúde:

```bash
curl http://localhost/health/ready
```

## Observabilidade

- **Logs:** JSON estruturado (Serilog) no stdout, coletável pelo Loki.
- **Traces/métricas:** OpenTelemetry via OTLP. O endpoint do collector é
  configurável por `OTEL_EXPORTER_OTLP_ENDPOINT`. Se não houver collector
  acessível, a exportação apenas não entrega — sem afetar a aplicação. Em
  ambientes sem OTel, pode-se desabilitar o exporter para evitar timeouts.
- **Dashboards:** Grafana (Prometheus + Loki como fontes).

## Backup e restauração

> Procedimento documentado; a automação (agendamento/retenção) é infraestrutural.

**PostgreSQL** — backup diário lógico:

```bash
docker compose exec postgres pg_dump -U "$POSTGRES_USER" "$POSTGRES_DB" \
  | gzip > backup-$(date +%F).sql.gz
```

Restauração:

```bash
gunzip -c backup-YYYY-MM-DD.sql.gz \
  | docker compose exec -T postgres psql -U "$POSTGRES_USER" -d "$POSTGRES_DB"
```

**MinIO** — habilitar versionamento no bucket e replicar/backup via `mc mirror`
para um destino secundário; definir política de retenção conforme requisito.

Recomendação: reter backups conforme política (ex.: 7 diários / 4 semanais / 12
mensais) e testar restauração periodicamente.

## CI/CD (recomendação)

Pipeline sugerido: `build` → `test` (unit + integração + segurança) → `lint` →
`migrations` (validar `has-pending-model-changes`) → `docker build` → `security
scan` → `deploy`. Deploy bloqueado se testes críticos falharem.

## Escala (pontos de extensão)

Para múltiplas instâncias em produção:
- Mover o **rate limiting** para um store distribuído no Redis (limite global).
- Cachear o mapeamento **papel→permissões** no Redis (fail-open para o mapa em
  código quando o Redis estiver indisponível).

Ambos são pontos de extensão que não alteram o comportamento observável atual.
