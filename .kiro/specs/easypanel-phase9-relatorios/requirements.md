# Requirements Document

## Introduction

Este documento especifica a **FASE 9 (Phase 9)** da plataforma **EasyPanel**
— tema **"Relatórios e Dashboards"**. A Fase 9 constrói sobre as Fases 2
(Contadores), 3 (Alertas), 6 (Chamados/SLA) e 8 (Faturamento), todas
concluídas: expõe um painel de visão geral do tenant e três relatórios
agregados por período (consumo de impressão, faturamento, cumprimento de
SLA), com exportação em CSV. Conforme o roteiro em `HANDOFF.md`, esta fase
não introduz nenhum domínio de dado novo — consulta e agrega o que já existe.

### Premissas verificadas no código antes de escrever este documento

- **Nenhuma agregação existe hoje em nenhum módulo.** Toda listagem atual é
  paginada item-a-item (`PagedResult`/`PageRequest` ou cursor). O cálculo de
  consumo por período mais próximo de um relatório é o algoritmo interno de
  `BillingClosingService` (Fase 8, R2) — leitura de fim menos leitura de
  início de `PrinterCounter` — que esta fase reaproveita como técnica, sem
  acoplar `Modules.Reporting` a `Modules.Billing`.
- **Redis hoje só tem health check, nenhum uso real de cache.**
  `RedisOptions`/`RedisHealthCheck` existem desde a Fase 1 apenas para
  prontidão; `RolePermissionResolver` resolve permissões **em memória**,
  documentando explicitamente que um cache Redis é "ponto de extensão
  futuro", não implementado. Um cache de relatório nesta fase seria o
  **primeiro uso real** de cache Redis na plataforma — ver "Questões em
  aberto" para a decisão de introduzi-lo agora ou não.
- **Nenhuma exportação (CSV/Excel/PDF) existe hoje.** Esta fase introduz
  exportação do zero, escopada a CSV (ver "Fora de escopo").
- **Cada entidade agregável já tem uma coluna `*Ticks` portável** (ex.:
  `PrinterCounter.TimestampTicks`, `Invoice.PeriodStartTicks`,
  `Ticket.CreatedAtTicks`) — os relatórios desta fase filtram/agrupam por
  essas colunas, nunca por comparação direta de `DateTimeOffset` (armadilha
  de tradução do SQLite já documentada em todas as fases anteriores).
- **Nenhuma permissão de relatório existe hoje** (`RolePermissions.cs`
  confirmado inalterado desde a Fase 8).

### Fora de escopo (adiado para fases futuras)

- **Exportação em Excel ou PDF** — apenas CSV nesta fase.
- **Relatórios agendados/enviados por e-mail.**
- **Construtor de relatório ad-hoc** (designer de consulta customizada pelo
  usuário) — os relatórios desta fase têm forma fixa, definida no backend.
- **Dashboards customizáveis/personalizáveis por usuário** (widgets
  arrastáveis, layout salvo) — o painel desta fase é um conjunto fixo de
  indicadores.
- **Alertas sobre limiares de relatório** (ex.: notificar quando o
  faturamento cair X%) — fora de escopo; é uma extensão natural futura do
  `Modules.Alerting`, não desta fase.
- **Read models materializados** (tabelas de agregação pré-calculadas,
  atualizadas por evento) — os relatórios desta fase consultam os dados
  originais ao vivo, a cada requisição (ver "Questões em aberto" sobre
  cache).

### Princípios não funcionais herdados das Fases 1–8 (válidos na Fase 9)

- **Isolamento multi-tenant inegociável**: toda consulta agregada é
  restrita ao tenant_id do contexto autenticado.
- **Backend é a autoridade**: validação de entrada, DTOs distintos das
  entidades.
- **Portabilidade SQLite/PostgreSQL**: filtros/agrupamentos por período
  usam colunas `*Ticks`, nunca comparação direta de `DateTimeOffset`.
- **Reuso de padrões**: Result/Error, RBAC.

## Glossary

- **Painel (Dashboard)**: consulta de estado corrente (não histórica) que
  retorna, num único documento, indicadores-chave do tenant no momento da
  consulta (contagens por status).
- **Relatório**: consulta agregada sobre um intervalo de datas
  (`[StartDate, EndDate]`), retornando totais/contagens agrupados por uma
  dimensão (ex.: por Cliente, por Impressora, por dia).
- **Exportação**: representação em CSV do resultado de um Relatório,
  entregue como download.

## Requirements

### Requirement 1: Painel de visão geral

**User Story:** Como usuário operacional, quero ver um painel com os
principais indicadores do tenant no momento atual, para entender rapidamente
a situação geral sem navegar por várias telas.

#### Acceptance Criteria

1. WHEN um Usuario autorizado consulta o painel de visão geral, THE Sistema
   SHALL retornar, restrito ao tenant_id do contexto autenticado: a
   contagem de Impressoras por `Status`; a contagem de Alertas abertos por
   severidade/estado; a contagem de Chamados por `Status`; a contagem de
   itens de Estoque abaixo do mínimo; a contagem de Faturas por `Status` e
   o valor total das Faturas em "Rascunho"/"Emitida".
2. THE Sistema SHALL calcular todos os indicadores do painel a partir do
   estado corrente dos dados no momento da consulta, sem nenhum
   pré-cálculo assíncrono ou defasagem proposital.
3. IF um domínio específico do painel não tem nenhum registro no tenant
   (ex.: nenhum Chamado ainda aberto), THEN THE Sistema SHALL retornar zero
   para aquele indicador, não omitir o campo nem retornar erro.

### Requirement 2: Relatório de consumo de impressão

**User Story:** Como responsável operacional, quero consultar o consumo de
páginas por impressora num intervalo de datas, para acompanhar o uso do
parque independente de faturamento.

#### Acceptance Criteria

1. WHEN um Usuario autorizado solicita o relatório de consumo para um
   intervalo `[StartDate, EndDate]`, THE Sistema SHALL calcular, por
   Impressora e tipo de contador, o consumo como a leitura de referência de
   fim do intervalo menos a leitura de referência de início do intervalo
   (mesma técnica de cálculo da Fase 8, R2), nunca negativo.
2. THE Sistema SHALL agrupar o resultado por Cliente e por Local, permitindo
   filtro opcional por Cliente e/ou Local.
3. IF `EndDate` é anterior a `StartDate`, THEN THE Sistema SHALL rejeitar a
   operação e retornar o código de status HTTP 400.

### Requirement 3: Relatório de faturamento

**User Story:** Como responsável financeiro, quero consultar o total
faturado por cliente num intervalo de datas, para acompanhar a receita por
período.

#### Acceptance Criteria

1. WHEN um Usuario autorizado solicita o relatório de faturamento para um
   intervalo `[StartDate, EndDate]`, THE Sistema SHALL retornar, agrupado
   por Cliente, a quantidade de Faturas e o valor total, restrito a Faturas
   cujo período de referência se sobrepõe ao intervalo solicitado.
2. THE Sistema SHALL permitir filtrar o relatório por `Status` de Fatura.
3. THE Sistema SHALL calcular o valor total do relatório como a soma dos
   `TotalAmount` das Faturas incluídas, sem incluir Faturas de outro
   tenant.

### Requirement 4: Relatório de cumprimento de SLA

**User Story:** Como responsável de suporte, quero consultar a taxa de
cumprimento de SLA de primeira resposta e de resolução num intervalo de
datas, para avaliar a qualidade do atendimento.

#### Acceptance Criteria

1. WHEN um Usuario autorizado solicita o relatório de SLA para um intervalo
   `[StartDate, EndDate]` (por data de abertura do Chamado), THE Sistema
   SHALL retornar a quantidade de Chamados com primeira resposta
   Cumprida/Violada/Pendente, e a quantidade com resolução
   Cumprida/Violada/Pendente.
2. THE Sistema SHALL calcular a taxa de cumprimento como a proporção de
   Chamados com o indicador correspondente igual a "Cumprido" sobre o total
   de Chamados com o indicador diferente de "Pendente" (excluindo os ainda
   em aberto do denominador).
3. IF não há nenhum Chamado no intervalo solicitado, THEN THE Sistema SHALL
   retornar contagens e taxas zeradas, não erro.

### Requirement 5: Exportação em CSV

**User Story:** Como usuário autorizado, quero exportar um relatório em
CSV, para analisar os dados numa planilha ou compartilhar externamente.

#### Acceptance Criteria

1. WHEN um Usuario autorizado solicita a exportação em CSV de um dos
   Relatórios (R2/R3/R4), THE Sistema SHALL retornar um arquivo CSV com os
   mesmos dados e filtros da consulta agregada correspondente, com
   cabeçalho de coluna na primeira linha.
2. THE Sistema SHALL codificar o arquivo CSV em UTF-8, restrito ao tenant_id
   do contexto autenticado.

### Requirement 6: Autorização e escala

**User Story:** Como responsável de segurança, quero que as consultas de
painel/relatório respeitem RBAC e as garantias de isolamento das fases
anteriores, sem expor dados de outro tenant nem degradar a performance das
consultas transacionais existentes.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover a permissão granular
   `relatorio.view` (consulta de painel, relatórios e exportação).
2. WHEN um Usuario autenticado requisita o painel, um relatório ou uma
   exportação, THE Serviço_Autorizacao SHALL autorizar a operação somente
   se algum Papel do Usuario possuir `relatorio.view`.
3. THE Sistema SHALL expor painel/relatórios por meio de DTOs de agregação
   distintos das entidades de persistência, sem retornar nenhum registro
   individual de outro tenant_id.
4. THE Sistema SHALL definir, quando necessário, índices de banco de dados
   adicionais sobre as colunas `*Ticks` já existentes para suportar os
   agrupamentos por período desta fase sem varredura completa de tabela.
5. IF um intervalo de datas solicitado num Relatório (R2/R3/R4) excede o
   limite máximo configurado, THEN THE Sistema SHALL recusar a operação e
   retornar o código de status HTTP 400.

## Questões em aberto para validação com o usuário antes do design

1. **Papéis e permissão (R6.1)**: confirmar o mapeamento — proposta:
   `Administrador`, `Financeiro`, `Supervisor` recebem `relatorio.view`
   (todos já têm permissões `*View` amplas de negócio); demais papéis sem
   acesso a relatórios/painel.
2. **Cache em Redis ("considerar read models/cache no Redis" no roteiro)**:
   como seria o **primeiro uso real** de cache na plataforma (hoje Redis só
   tem health check), proponho **não introduzir cache nesta fase** —
   calcular tudo ao vivo a cada requisição, com índices adequados (R6.4), e
   revisitar cache como otimização futura somente se um problema de
   performance real for observado. Confirmar essa decisão ou preferir já
   introduzir um cache Redis simples (TTL curto, fail-open) para o Painel
   (R1), que é a consulta mais repetida.
3. **Auditoria de leitura (R6)**: confirmar que consultas de painel/
   relatório **não** são auditadas no `AuditLog` (mesmo padrão de toda
   listagem `GET` existente na plataforma, que também não é auditada) —
   apenas a exportação (R5) poderia ser auditada, por extrair dados em
   massa; proposta: não auditar nem exportação nesta fase, por
   simplicidade, e revisitar se houver exigência de compliance futura.
4. **Limite máximo de intervalo de datas (R6.5)**: confirmar um valor —
   proposta: 366 dias (cobre relatórios anuais sem permitir varreduras
   arbitrariamente grandes).
5. **Escopo do painel (R1.1)**: confirmar a lista de indicadores proposta
   (Impressoras por status, Alertas abertos, Chamados por status, itens de
   Estoque abaixo do mínimo, Faturas por status/valor) ou ajustar — por
   exemplo, incluir/excluir algum domínio.
