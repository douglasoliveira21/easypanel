# Roteiro — Próximas Fases do EasyPanel

Este documento estende o roteiro do [HANDOFF.md](HANDOFF.md) (seção 6) depois
da conclusão das 10 fases de domínio (Fundação → Portal do Cliente). Segue o
mesmo processo gated (requisitos → design → tarefas → implementação, uma fase
por vez, com aprovação explícita) documentado em `.kiro/steering/workflow.md`.

**Critério de priorização escolhido:** colocar o sistema **rodando de
verdade em produção** antes de somar mais funcionalidades de negócio. Hoje o
backend é funcionalmente completo (10 fases, 151 unit + 65 security + 375
integration testes verdes) mas nunca rodou fora de testes automatizados —
não há pipeline de CI, não há deploy real, não há frontend, e vários pontos
de extensão (cache Redis, rate limiting distribuído) ficaram deliberadamente
como TODO por não serem "primeiro uso real".

## Visão geral

| Fase | Tema | Por quê agora |
|------|------|----------------|
| 11 | CI/CD no GitHub | Sem isso, cada mudança depende de rodar tudo manualmente — risco alto de regressão silenciosa assim que mais de uma pessoa/sessão mexer no código |
| 12 | Deploy real (staging) | Provar que `deploy/docker-compose*.yml` funciona de ponta a ponta fora da máquina de desenvolvimento |
| 13 | Observabilidade operacional | Sem dashboards/alertas reais, um incidente em produção não seria percebido a tempo |
| 14 | Hardening de segurança | Fechar os dois TODOs de escala sinalizados desde a Fase 1 (rate limiting e RBAC em Redis) antes de expor a internet |
| 15 | Frontend (SPA) | A API está pronta, mas ninguém consegue *usar* o produto sem interface |
| 16 | Compilação e distribuição do Agente Windows | Empacotar o instalador/serviço para ser instalado em máquinas clientes reais |
| 17+ | Extensões de negócio | Portal interativo, multi-Cliente por usuário, emissão fiscal, exportações avançadas — deliberadamente por último |

---

## Fase 11 — CI/CD no GitHub

**Objetivo:** toda alteração enviada ao GitHub é automaticamente compilada e
testada antes de poder ser mesclada; nenhuma regressão passa despercebida.

**Escopo:**
- `.github/workflows/backend-ci.yml`: em push/PR para `main`, roda
  `dotnet build EasyPanel.sln` (zero warnings, `TreatWarningsAsErrors`),
  `dotnet test EasyPanel.sln` (unit + integration + security) e
  `dotnet ef migrations has-pending-model-changes`.
- `.github/workflows/agent-ci.yml`: mesma verificação para
  `src/windows-client/EasyPanel.WindowsClient.slnx` (build + testes),
  independente do pipeline do backend (soluções já são independentes).
- `dotnet list package --vulnerable --include-transitive` como gate de
  segurança de dependências (falha o pipeline se houver vulnerabilidade
  conhecida sem pin de versão).
- Branch protection na `main` (exigir os checks acima antes de merge) —
  configuração no GitHub, não código.
- **Fora de escopo nesta fase:** deploy automático (CD) — fica para a Fase
  12, depois que o ambiente de staging existir para o deploy apontar.

**Critério de pronto:** um PR de teste (ex. um typo em comentário) dispara os
workflows e mostra status verde/vermelho corretamente no GitHub.

## Fase 12 — Deploy real (staging)

**Objetivo:** rodar a stack completa (`docker-compose.yml` +
`docker-compose.staging.yml`) num host real (VM/cloud), não só na máquina de
desenvolvimento, provando que a Fase 1 (Docker Compose, Caddy, Postgres,
Redis, MinIO) funciona fora do papel.

**Escopo:**
- Provisionar um host (VM cloud econômica é suficiente para staging).
- `deploy/.env` real com segredos gerados (nunca reaproveitar os de
  desenvolvimento) — `JWT_SIGNING_KEY`, credenciais de Postgres/MinIO/Redis,
  `SITE_ADDRESS`/`ACME_EMAIL` para TLS automático do Caddy.
- Subir a stack, confirmar `GET /health/ready`, aplicar o Bootstrap
  (`SuperAdminEmail`/`SuperAdminPassword`) para criar o primeiro acesso.
- Runbook de deploy (documentar os passos reais executados, não só o
  teórico já em `docs/DEPLOYMENT.md`) e runbook de rollback.
- **CD automático** (fecha o que a Fase 11 deixou pendente): workflow que,
  após os testes passarem na `main`, builda as imagens Docker e faz deploy
  no host de staging via SSH/registry.

**Critério de pronto:** a API responde publicamente em staging, com TLS
válido, e um push na `main` chega automaticamente ao ambiente em minutos.

## Fase 13 — Observabilidade operacional

**Objetivo:** transformar o Prometheus/Loki/Grafana já provisionados (mas
não usados de verdade) em dashboards e alertas que alguém realmente olharia
num incidente.

**Escopo:**
- Dashboards Grafana: latência/taxa de erro por endpoint (via OTel →
  Prometheus), saúde dos serviços (`/health/*`), fila de notificação de
  alerta (Fase 3) sem processar, filas offline de Agentes Windows paradas.
- Alertas (Grafana Alerting ou Prometheus Alertmanager): erro 5xx acima de
  limiar, serviço fora do ar, disco/memória do host.
- Retenção de logs configurada no Loki (hoje só provisionado).
- **Não escopo:** tracing distribuído multi-serviço (a plataforma é um
  monólito modular — um único processo — então o ganho de tracing
  distribuído é baixo nesta fase).

**Critério de pronto:** um erro 500 induzido deliberadamente aparece no
dashboard e dispara alerta em menos de 5 minutos.

## Fase 14 — Hardening de segurança

**Objetivo:** fechar os pontos de extensão sinalizados desde a Fase 1 que só
importam com tráfego real/múltiplas instâncias, e revisar a superfície de
ataque antes de expor a produção à internet pública.

**Escopo:**
- **Rate limiting distribuído no Redis** (hoje in-process — não funciona
  corretamente com múltiplas instâncias da API atrás do Caddy).
- **Cache de RBAC no Redis** (`RolePermissionResolver`, hoje em memória,
  documentado como "ponto de extensão futuro" desde a Fase 1) — com
  fail-open para o mapa em código se o Redis cair.
- Revisão de cabeçalhos de segurança HTTP (CSP, HSTS, etc.) no Caddy.
- Rotação de segredos (`JWT_SIGNING_KEY`, credenciais de banco) documentada
  como procedimento operacional.
- Checklist de pentest básico (OWASP Top 10) sobre os endpoints públicos —
  pode ser o gatilho para `/code-review ultra` ou uma revisão dedicada.

**Critério de pronto:** com 2+ instâncias da API rodando atrás do Caddy, o
rate limit e o cache de permissões continuam corretos (testado manualmente
ou por teste de carga simples).

## Fase 15 — Frontend (SPA)

**Objetivo:** dar ao produto uma interface utilizável — hoje só existe
`frontend/Dockerfile` e um `dist/index.html` placeholder.

**Escopo (a definir em requirements.md próprio quando esta fase começar):**
- Stack: React (já sinalizada no produto/README) + TypeScript.
- Login/MFA, gestão de usuários/Clientes/Locais (Fase 1), parque de
  impressoras (Fase 2), alertas (Fase 3), estoque (Fase 5), chamados (Fase
  6), contratos (Fase 7), faturamento (Fase 8), dashboards/relatórios (Fase
  9) e o **Portal do Cliente** (Fase 10) como uma área logada separada,
  coerente com o RBAC já existente no backend.
- Consome exclusivamente a API já pronta — nenhuma lógica de negócio
  duplicada no frontend (a API já é a autoridade).

**Critério de pronto:** um Administrador consegue, pela interface, cadastrar
um Cliente, abrir um chamado e visualizar o painel — sem usar `curl`/Swagger.

## Fase 16 — Compilação e distribuição do Agente Windows

**Objetivo:** ir de "compila e passa nos testes" para "gera um artefato
instalável" — hoje o Agente só foi validado via `dotnet build`/`dotnet test`
(ver seção "Compilando o Agente" no tutorial), nunca publicado como binário
standalone nem empacotado como instalador.

**Escopo:**
- `dotnet publish` self-contained, single-file, para `win-x64` (ver comando
  no tutorial) — artefato que roda sem exigir o .NET Runtime instalado na
  máquina cliente.
- Empacotamento como Windows Service instalável (script de instalação
  `sc create`/`New-Service` ou um instalador MSI/EXE, ex. via WiX ou
  Velopack, coerente com o auto-update assinado já implementado).
- Assinatura de código do executável (certificado Authenticode) — sem isso,
  o SmartScreen do Windows bloqueia a instalação em máquinas de cliente.
- Pipeline de release do Agente (tag → build → assinatura → publicação do
  pacote consumido pelo fluxo de auto-update já implementado, R16).

**Critério de pronto:** um instalador `.msi`/`.exe` gerado pelo pipeline
instala e registra o Agente como serviço Windows numa máquina limpa, sem
.NET Runtime pré-instalado.

## Fase 17+ — Extensões de negócio (backlog, não fases fechadas)

Fora de escopo até aqui deliberadamente (ver `requirements.md` de cada fase
anterior); candidatas a fases futuras, cada uma exigindo spec própria
quando priorizada:

- **Portal do Cliente interativo:** abertura de chamado e pagamento de
  fatura pelo próprio Cliente (Fase 10 é somente-leitura).
- **Multi-Cliente por usuário do portal:** hoje um login vincula-se a no
  máximo um `Customer` (Fase 10, decisão explícita).
- **Emissão fiscal/PDF de fatura:** Fase 8 gera `Invoice` sem NF-e/PDF.
- **Exportação em Excel/PDF e relatórios agendados:** Fase 9 só tem CSV
  sob demanda.
- **SNMP v3, descoberta por subnet/broadcast:** Agente Windows hoje cobre
  v1/v2c e descoberta por lista de alvos (ver `docs/CLIENT.md`, "Itens
  previstos, não implementados").

## Como usar este roteiro

Antes de iniciar qualquer fase acima: confirmar com o usuário qual é a
próxima (a ordem sugerida é a recomendada, mas não obrigatória — ex.: se o
frontend virar prioridade de negócio, a Fase 15 pode vir antes da 13/14), e
então seguir o processo gated normal: criar
`.kiro/specs/easypanel-faseNN-<slug>/requirements.md` em EARS, aguardar
aprovação, design.md, aguardar aprovação, tasks.md, aguardar aprovação, só
então implementar.
