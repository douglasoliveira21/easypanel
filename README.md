# EasyPanel

Plataforma SaaS multi-tenant para gestão de outsourcing de impressão e ativos de TI.

> **Estado atual: FASE 10 (portal do cliente) concluída — roteiro atual completo.** A Fase 1 (fundação) entregou
> infraestrutura, autenticação, RBAC, isolamento multi-tenant, auditoria, gestão
> de usuários e cadastro de Clientes e Locais. A Fase 2 adicionou o
> **monitoramento**: autenticação própria do agente Windows, registro/heartbeat,
> ingestão idempotente de coletas com pipeline assíncrono, cadastro e
> movimentação de impressoras, contadores (não-decréscimo + ajuste auditado),
> configuração/atualização do agente e o **Agente Windows** (solução-satélite:
> SNMP + drivers por fabricante, fila offline, auto-update assinado). A Fase 3
> adicionou **alertas e notificações**: regras de alerta configuráveis por tenant,
> um motor de avaliação que consome os `PrinterEvent` da Fase 2 e cria/atualiza
> Alertas com resolução automática, ciclo de vida com histórico, notificação por
> e-mail e webhook assinado (com retry/backoff) e silenciamento. A Fase 4
> adiciona **suprimentos**: histórico de nível de toner/cilindro por impressora,
> limiar configurável em cascata (Impressora+rótulo → Tenant → plataforma) que
> reusa o motor de alertas da Fase 3 sem alterá-lo, previsão de troca por
> regressão linear, e — fechando uma lacuna que vinha da Fase 2 — a orquestração
> **real** do Agente Windows (cliente HTTP com autenticação/renovação de token,
> coleta SNMP real via `Lextm.SharpSnmpLib`, e os serviços internos
> `Net_Monitoring_Service`/`Communication_Service` supervisionados pelo Guardian).
> A Fase 5 adiciona **estoque**: catálogo de itens, movimentação (Entrada/
> Saída/Ajuste) com saldo materializado por Local atualizado
> transacionalmente, e estoque mínimo configurável por (Item, Local) com
> listagem de itens abaixo do mínimo. A Fase 6 adiciona **chamados/helpdesk e
> SLA**: abertura de chamados vinculados a Cliente/Local/Impressora, ciclo de
> vida com histórico de interações, política de SLA por prioridade com
> cálculo de prazos e indicador de violação, e anexos — primeiro uso
> funcional do MinIO na plataforma (antes, apenas health check). A Fase 7
> adiciona **contratos**: cadastro vinculado a Cliente, com vigência e ciclo
> de vida; escopo opcional a Locais/Impressoras específicos (sem escopo =
> cobre todo o Cliente); franquia por tipo de contador (quantidade incluída
> + preço de excedente); e a resolução do contrato aplicável a uma
> impressora numa data — base para a Fase 8. A Fase 8 adiciona **fechamento e
> faturamento**: fechamento mensal que consolida o consumo de cada
> impressora a partir do histórico de contadores (leitura de fim menos
> leitura de início do período), resolve o contrato aplicável via a Fase 7,
> calcula o excedente contra a franquia configurada, e gera fatura (rascunho
> → emitida → cancelada) por contrato — sem emissão fiscal, PDF ou cobrança
> ainda. A Fase 9 adiciona **relatórios e dashboards**: painel de visão
> geral com contagens de estado corrente (impressoras, alertas, chamados,
> estoque, faturas), três relatórios agregados por período (consumo de
> impressão, faturamento, cumprimento de SLA) e exportação em CSV — tudo
> calculado ao vivo, sem cache. A Fase 10 adiciona o **portal do cliente**:
> área self-service somente-leitura para o papel `Cliente`, vinculado a um
> Cliente (Customer) específico via `CustomerId` — um segundo nível de
> isolamento, além do tenant — com visão do próprio parque de impressoras e
> contadores, chamados e faturas emitidas/canceladas (nunca rascunhos).

## Visão geral

O EasyPanel permite que empresas prestadoras de serviços gerenciem, de forma
isolada por tenant, seus clientes e os pontos de instalação (locais) onde os
serviços são prestados. A Fase 1 estabelece os alicerces de segurança e
escalabilidade sobre os quais as próximas fases serão construídas.

## Stack

- **Backend:** C# / .NET 10 / ASP.NET Core Web API, Entity Framework Core
- **Banco:** PostgreSQL
- **Cache/processamento:** Redis (preparado)
- **Armazenamento:** S3-compatible / MinIO (provisionado)
- **Autenticação:** ASP.NET Identity, JWT + Refresh Token, RBAC, MFA-ready
- **Observabilidade:** Serilog (JSON), OpenTelemetry (OTLP), Prometheus/Loki/Grafana
- **Infra:** Docker Compose, Caddy (reverse proxy)
- **Arquitetura:** monólito modular (separação clara de módulos)

## Estrutura da solução

```
EasyPanel.sln
├── src/
│   ├── EasyPanel.Api/                 # Host Web API, controllers, middlewares, DI
│   ├── EasyPanel.Modules.Identity/    # Auth, usuários, RBAC (contratos + entidades)
│   ├── EasyPanel.Modules.Tenancy/     # Tenant, ITenantContext, resolução
│   ├── EasyPanel.Modules.Customers/   # Customer + Location (contratos + entidades)
│   ├── EasyPanel.Modules.Auditing/    # AuditLog (contratos)
│   ├── EasyPanel.Modules.Monitoring/  # Fase 2: agente, impressoras, contadores, coletas
│   ├── EasyPanel.Modules.Alerting/    # Fase 3: regras de alerta, alertas, silenciamentos
│   ├── EasyPanel.Shared.Kernel/       # BaseEntity, Result, PagedResult, exceções
│   ├── EasyPanel.Infrastructure/      # DbContext, EF config, Identity, tokens, migrations
│   └── windows-client/                # Fase 2: Agente Windows (solução-satélite .slnx)
├── tests/
│   ├── EasyPanel.UnitTests/
│   ├── EasyPanel.IntegrationTests/    # WebApplicationFactory + SQLite in-memory
│   └── EasyPanel.SecurityTests/       # Isolamento multi-tenant ponta a ponta
├── deploy/                            # docker-compose (dev/staging/prod), Caddyfile
└── docs/                             — ver documentos abaixo
```

## Documentação

- [ARCHITECTURE.md](docs/ARCHITECTURE.md) — arquitetura, módulos e pipeline
- [DATABASE.md](docs/DATABASE.md) — modelo de dados, entidades e índices
- [API.md](docs/API.md) — endpoints das Fases 1 a 4
- [SECURITY.md](docs/SECURITY.md) — autenticação, RBAC, isolamento e boas práticas
- [DEPLOYMENT.md](docs/DEPLOYMENT.md) — execução, configuração e backup
- [CLIENT.md](docs/CLIENT.md) — instalação e operação do Agente Windows (Fases 2 e 4)

## Como rodar (desenvolvimento)

Pré-requisitos: .NET 10 SDK e Docker.

```bash
# Subir a stack de apoio (Postgres, Redis, MinIO, etc.)
docker compose -f deploy/docker-compose.yml up -d

# Rodar a API localmente
dotnet run --project src/EasyPanel.Api
```

A API aplica as migrações pendentes na inicialização e expõe:

- `GET /health/live` e `GET /health/ready` — health checks
- Swagger UI (em desenvolvimento) para explorar os endpoints
- `POST /api/v1/auth/login` e os módulos de usuários/clientes/locais

Configuração sensível (connection strings, chave JWT, credenciais) vem de
variáveis de ambiente / segredos — nunca versionada. Veja
[DEPLOYMENT.md](docs/DEPLOYMENT.md).

## Testes

```bash
dotnet test EasyPanel.sln
```

A suíte cobre unidade, integração (com SQLite in-memory) e segurança (isolamento
multi-tenant). Nenhum teste crítico deve falhar antes de avançar de fase.
