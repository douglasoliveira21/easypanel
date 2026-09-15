# Requirements Document

## Introduction

Este documento especifica a **FASE 7 (Phase 7)** da plataforma **EasyPanel** —
tema **"Contratos"**. A Fase 7 constrói sobre a Fase 1 (Clientes/Locais,
Identity/RBAC), a Fase 2 (Impressoras/Contadores) e a Fase 6 (Chamados/SLA),
todas concluídas. Conforme o roteiro em `HANDOFF.md`, esta fase é a **base**
para a Fase 8 (Fechamento e Faturamento): armazena franquias por contador,
vigência e tabela de preço, vinculados a Cliente/Local/Impressora — a
consolidação de contadores por período e a geração de fatura em si pertencem
à Fase 8, não a esta.

### Premissas verificadas no código antes de escrever este documento

- **Não existe nenhum código de contratos hoje.** O papel `Financeiro` já traz
  o comentário "permissões financeiras de contrato/fechamento em fases
  futuras" desde a Fase 1 (`Modules.Identity.Roles`) — esta é essa fase.
- **`PrinterCounter`** (`Modules.Monitoring`, Fase 2) já é quebrado por tipo:
  `CounterType` = `BlackAndWhite`, `Color`, `A3`, `A4`, `Scan`, `Other` (com
  `CounterTypeLabel` livre quando `Other`). Uma franquia "por contador" desta
  fase referencia um destes tipos (ou `Other`+rótulo), não um total agregado
  único — mesma granularidade que o Fechamento (Fase 8) vai consumir.
- **Nenhuma convenção monetária existe ainda** no código (nenhum campo
  `decimal` de preço/valor em nenhum módulo). Esta fase introduz o primeiro
  tipo monetário da plataforma — ver "Questões em aberto" para a decisão de
  moeda/precisão.
- **Referências por `Guid`, sem FK de módulo cruzado**: `Customer.Id`,
  `Location.Id` (com `CustomerId`) e `Printer.Id` (com `CustomerId`/
  `LocationId`) existem em `Modules.Customers`/`Modules.Monitoring`. Um
  Contrato referenciará Cliente/Local/Impressora apenas por `Guid`, validados
  quanto à existência/tenant em `Infrastructure.Contracts` — mesmo padrão já
  usado em `Modules.Alerting`/`Modules.Inventory`/`Modules.Ticketing`.
- **Cascata de especificidade já tem precedente**: `SupplyThreshold` (Fase 4)
  resolve o limiar mais específico primeiro (Impressora+Rótulo → Tenant+Rótulo
  → Tenant geral → padrão de plataforma). Esta fase usa o mesmo raciocínio
  para decidir qual Contrato cobre uma Impressora quando há mais de um
  vinculado ao mesmo Cliente (ver R2/R6).

### Fora de escopo (adiado para a Fase 8 — Fechamento e Faturamento)

- **Consolidação de contadores por período e cálculo de excedente real** —
  a Fase 7 armazena a franquia (quantidade incluída + preço do excedente) por
  (Contrato, CounterType), mas não executa nenhum cálculo periódico sobre o
  histórico de `PrinterCounter`. Isso é o núcleo da Fase 8.
- **Geração de fatura/documento fiscal.**
- **Preço em camadas/volume (tiered pricing)** — cada franquia desta fase tem
  uma quantidade incluída e **um único** preço de excedente por unidade
  (single-tier). Preço escalonado por faixa de volume fica sinalizado como
  gancho futuro, não implementado aqui (ver "Questões em aberto").
- **Encerramento automático por data** — um Contrato com `EndDate` no passado
  não transiciona sozinho para `Encerrado`; a vigência expirada é apenas um
  indicador calculado nas consultas (ver R5). A transição de status continua
  sendo uma ação explícita, mesmo padrão de decisão já usado no ciclo de vida
  do Chamado (Fase 6).
- **Vínculo `SlaPolicy` ↔ Contrato** — gancho futuro sinalizado em
  `HANDOFF.md` desde a Fase 6; não implementado aqui (a Fase 6 mantém SLA por
  tenant/prioridade).

### Princípios não funcionais herdados das Fases 1–6 (válidos na Fase 7)

- **Isolamento multi-tenant inegociável**: toda entidade nova é `TenantEntity`.
- **Backend é a autoridade**: validação de entrada, DTOs distintos das
  entidades, auditoria de operações sensíveis.
- **Cursor pagination** sobre uma coluna portátil (`*Ticks`, `long`) para
  listagens de alto volume.
- **Reuso de padrões**: Result/Error, `PagedResult`/`PageRequest`, RBAC,
  `IAuditLogger`.

## Glossary

- **Contrato**: acordo comercial de um Cliente, com vigência (início e,
  opcionalmente, fim) e um ou mais Franquias de contador. Pode ser
  escopado a Locais/Impressoras específicos ou, na ausência de escopo,
  cobre todo o Cliente.
- **Franquia (ContractFranchise)**: quantidade de páginas incluída no
  Contrato para um `CounterType`, por ciclo de faturamento, mais o preço
  unitário cobrado por página excedente.
- **Vigência**: intervalo `[StartDate, EndDate]` (`EndDate` nulo = por prazo
  indeterminado) em que o Contrato é considerado válido comercialmente,
  independente do seu `Status` operacional.
- **Escopo do Contrato**: conjunto de Locais e/ou Impressoras cobertos pelo
  Contrato; vazio significa "todo o Cliente".

## Requirements

### Requirement 1: Cadastro de contratos

**User Story:** Como responsável financeiro, quero cadastrar um contrato
vinculado a um Cliente, com número/nome, vigência e observações, para
formalizar o acordo comercial antes de configurar franquias.

#### Acceptance Criteria

1. THE Sistema SHALL armazenar por Contrato os campos: identificador, número/
   nome, Cliente, data de início de vigência, data de fim de vigência
   opcional, status, observações.
2. WHEN um Usuario autorizado cria um Contrato informando dados válidos, THE
   Sistema SHALL persistir o Contrato vinculado ao tenant_id do contexto
   autenticado, com status inicial "Rascunho".
3. IF um Contrato é criado sem Cliente válido do tenant, ou com data de fim
   anterior à data de início, THEN THE Sistema SHALL rejeitar a operação e
   retornar o código de status HTTP 400.
4. WHEN um Usuario autorizado lista ou consulta Contratos, THE Sistema SHALL
   retornar somente os contratos do tenant_id do contexto autenticado, com
   paginação e filtro por Cliente/status.

### Requirement 2: Escopo do contrato (Local/Impressora)

**User Story:** Como responsável financeiro, quero vincular um contrato a
Locais e/ou Impressoras específicos, para cobrir apenas o parque combinado
comercialmente, ou deixar o contrato cobrindo o Cliente inteiro quando não
houver granularidade necessária.

#### Acceptance Criteria

1. THE Sistema SHALL permitir vincular a um Contrato zero ou mais Locais e
   zero ou mais Impressoras do mesmo Cliente do contrato.
2. WHEN um Contrato não tem nenhum Local nem Impressora vinculados, THE
   Sistema SHALL considerá-lo aplicável a todo o parque do Cliente.
3. WHEN um Usuario autorizado vincula um Local ou uma Impressora a um
   Contrato, THE Sistema SHALL validar que ambos pertencem ao mesmo Cliente e
   tenant_id do Contrato, rejeitando com código de status HTTP 400 caso
   contrário.
4. IF uma Impressora vinculada a um Contrato já está vinculada a outro
   Contrato do mesmo Cliente com vigência sobreposta, THEN THE Sistema SHALL
   recusar o vínculo e retornar o código de status HTTP 409.

### Requirement 3: Franquia por contador

**User Story:** Como responsável financeiro, quero configurar, por tipo de
contador, uma quantidade incluída e um preço de excedente, para que a Fase 8
tenha a base necessária para calcular faturas.

#### Acceptance Criteria

1. THE Sistema SHALL armazenar por Franquia os campos: Contrato, tipo de
   contador (CounterType, com rótulo livre quando "Other"), quantidade
   incluída por ciclo, preço unitário do excedente, moeda.
2. WHEN um Usuario autorizado configura uma Franquia para um Contrato e tipo
   de contador, THE Sistema SHALL validar que a quantidade incluída é maior
   ou igual a zero e que o preço unitário do excedente não é negativo,
   rejeitando com código de status HTTP 400 caso contrário.
3. THE Sistema SHALL permitir no máximo uma Franquia por (Contrato,
   CounterType) — configurar novamente sobrescreve (upsert) a Franquia
   existente para aquele tipo.
4. WHEN um Usuario autorizado consulta as Franquias de um Contrato, THE
   Sistema SHALL retornar todas as Franquias configuradas, restritas ao
   tenant_id do contexto autenticado.

### Requirement 4: Resolução do contrato aplicável a uma impressora

**User Story:** Como responsável financeiro, quero que o sistema determine
qual contrato cobre uma impressora numa data específica, para que a Fase 8
consiga atribuir corretamente cada leitura de contador ao contrato correto.

#### Acceptance Criteria

1. WHEN o Sistema resolve o Contrato aplicável a uma Impressora numa data,
   THE Sistema SHALL priorizar, nesta ordem, o Contrato mais específico entre
   os Contratos "Ativo" do Cliente vigentes naquela data: vínculo direto à
   Impressora, depois vínculo ao Local da Impressora, depois o Contrato sem
   nenhum escopo (cobrindo todo o Cliente).
2. IF não existe nenhum Contrato "Ativo" e vigente que cubra a Impressora na
   data informada, THEN a resolução SHALL retornar "nenhum contrato
   aplicável", sem erro.
3. THE Sistema SHALL expor a resolução do contrato aplicável como uma
   consulta de domínio reutilizável (sem executar nenhum cálculo de
   consolidação de contador nesta fase — ver "Fora de escopo").

### Requirement 5: Ciclo de vida e vigência do contrato

**User Story:** Como responsável financeiro, quero controlar o status de um
contrato (rascunho, ativo, suspenso, encerrado) e ver se a vigência já
expirou, para saber quais contratos estão comercialmente válidos.

#### Acceptance Criteria

1. THE Sistema SHALL definir os status possíveis de um Contrato: Rascunho,
   Ativo, Suspenso, Encerrado.
2. WHEN um Usuario autorizado transiciona o status de um Contrato, THE
   Sistema SHALL validar que a transição é permitida a partir do status
   corrente, registrando o evento no AuditLog; transição inválida SHALL
   retornar o código de status HTTP 400.
3. WHEN um Usuario autorizado consulta um Contrato, THE Sistema SHALL incluir
   um indicador calculado de vigência expirada (`EndDate` no passado), sem
   alterar o `Status` armazenado automaticamente.
4. THE Sistema SHALL considerar, para a resolução de R4, apenas Contratos com
   `Status = Ativo` e vigência corrente na data de referência.

### Requirement 6: Autorização, auditoria e escala

**User Story:** Como responsável de segurança, quero que as operações de
contrato respeitem RBAC, auditoria e as garantias de isolamento das fases
anteriores.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover as permissões granulares
   `contrato.view` (consulta de contratos/franquias/escopo) e
   `contrato.manage` (criação/edição de contrato, franquia, escopo,
   transição de status).
2. WHEN um Usuario autenticado requisita uma operação de contrato, THE
   Serviço_Autorizacao SHALL autorizar a operação somente se algum Papel do
   Usuario possuir a Permissao correspondente.
3. WHEN um Contrato é criado, tem status/escopo/franquia alterados, THE
   Serviço_Auditoria SHALL registrar o evento no AuditLog com o ator, o
   tenant, e os dados relevantes da operação.
4. THE Sistema SHALL expor as operações de contrato por meio de DTOs
   distintos das entidades de persistência.
5. IF uma requisição tenta acessar ou operar sobre um Contrato, uma Franquia
   ou um vínculo de escopo de um tenant_id distinto do contexto autenticado,
   THEN THE Sistema SHALL recusar a operação e retornar o código de status
   HTTP 404.
6. THE Sistema SHALL definir índices de banco de dados sobre tenant_id e
   sobre os campos de filtro/ordenação de Contrato, Franquia e escopo.

## Questões em aberto para validação com o usuário antes do design

1. **Papéis e permissões (R6.1)**: confirmar o mapeamento — proposta:
   `Financeiro` recebe `contrato.view` + `contrato.manage` (dono natural do
   módulo); `Administrador` recebe ambas também; `Operacional`/`Supervisor`
   recebem apenas `contrato.view`; `Tecnico`/`Estoque` sem permissões de
   contrato.
2. **Moeda e precisão monetária (R3.1)**: confirmar `decimal` com moeda fixa
   `BRL` nesta fase (sem multi-moeda) — proposta: campo `Currency` como
   string ISO 4217 fixa em `"BRL"` por enquanto, já modelado para permitir
   multi-moeda numa fase futura sem migração destrutiva.
3. **Conflito de escopo sobreposto (R2.4)**: confirmar a regra proposta —
   duas Impressoras não podem estar vinculadas, ao mesmo tempo, a dois
   Contratos "Ativo"/"Rascunho" do mesmo Cliente com vigência sobreposta
   (409 ao tentar). Contratos "Suspenso"/"Encerrado" não contam para essa
   validação de conflito.
4. **Preço em camadas (R3, "Fora de escopo")**: confirmar que a Franquia
   desta fase usa um preço único de excedente (sem faixas de volume), com
   preço escalonado ficando para uma fase futura, se necessário.
5. **Granularidade de vigência (R5)**: confirmar que `StartDate`/`EndDate`
   são datas (sem hora), e que a data de referência usada por R4 é a data
   corrente (UTC) no momento da consulta/consolidação.
