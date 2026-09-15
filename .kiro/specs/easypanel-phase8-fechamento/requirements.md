# Requirements Document

## Introduction

Este documento especifica a **FASE 8 (Phase 8)** da plataforma **EasyPanel** —
tema **"Fechamento e Faturamento"**. A Fase 8 constrói sobre a Fase 2
(Impressoras/Contadores) e a Fase 7 (Contratos), ambas concluídas: consolida
os contadores de um período em consumo por Impressora, resolve o Contrato
aplicável a cada Impressora via `ContractService.ResolveApplicableAsync`,
calcula o excedente contra a `ContractFranchise` correspondente, e gera
faturas (rascunho) auditáveis. Conforme o roteiro em `HANDOFF.md`, esta fase
é a última do núcleo comercial antes de Relatórios (Fase 9) e Portal do
Cliente (Fase 10).

### Premissas verificadas no código antes de escrever este documento

- **`PrinterCounter.Value` é cumulativo (odômetro), não-decrescente por
  (Impressora, CounterType)**, salvo ajuste administrativo explícito
  (`IsAdministrativeAdjustment`) — confirmado em `Modules.Monitoring` (Fase
  2) e reforçado por `CounterService`, que valida o novo valor contra a
  última leitura e só aceita decréscimo quando marcado como ajuste. **Isso
  determina o método de cálculo de consumo**: consumo do período = última
  leitura até o fim do período menos a leitura de referência do início do
  período — nunca soma de deltas entre leituras.
- **`ContractService.ResolveApplicableAsync(printerId, referenceDate)`**
  (Fase 7) retorna o `Contract` `Ativo` e vigente mais específico
  (Impressora → Local → Cliente inteiro) para uma Impressora numa data, ou
  `null` sem erro quando nenhum se aplica — a Fase 8 reusa exatamente esta
  consulta, uma vez por Impressora, na data de referência do fechamento
  (proposta: o último dia do período).
- **`ContractFranchise`** (Fase 7) já define, por (Contrato, CounterType), a
  quantidade incluída e um preço único de excedente (sem faixas) em `BRL`
  (`decimal(18,4)`) — a Fase 8 não introduz um segundo modelo de preço, usa
  este diretamente.
- **Não existe nenhum conceito de período/ciclo de faturamento no código
  hoje** — `Contract.StartDate`/`EndDate` é vigência comercial, não
  recorrência mensal. Esta fase introduz o período de fechamento do zero.
- **Não existe nenhum código de fatura/fechamento hoje.** O papel
  `Financeiro` já tem `contrato.view`/`contrato.manage` (Fase 7); esta fase
  adiciona as permissões de fechamento/fatura.

### Fora de escopo (adiado para fases futuras)

- **Emissão fiscal/nota fiscal eletrônica**, integração com gateway de
  pagamento, cobrança e conciliação bancária — a fatura desta fase é um
  documento interno (rascunho → emitida → cancelada), não um documento
  fiscal.
- **Geração de PDF/e-mail da fatura** — fica para uma fase de portal/
  comunicação futura; esta fase expõe os dados estruturados da fatura via
  API.
- **Fechamento automático agendado** (ex.: rodar sozinho todo dia 1º) — o
  fechamento desta fase é disparado por uma ação explícita (mesma decisão
  de "sem automação implícita" já usada no ciclo de vida de Chamado/
  Contrato).
- **Disputa/correção manual de consumo faturado** (ex.: glosa, reemissão com
  valor ajustado) — uma fatura só pode ser cancelada por inteiro (R5), não
  editada; reemissão fica para fase futura se necessário.
- **Vínculo `SlaPolicy` ↔ Contrato** — gancho já sinalizado desde a Fase 6,
  continua fora de escopo.
- **Preço em camadas/volume** — herdado da Fase 7 (franquia de preço único
  de excedente).

### Princípios não funcionais herdados das Fases 1–7 (válidos na Fase 8)

- **Isolamento multi-tenant inegociável**: toda entidade nova é
  `TenantEntity`.
- **Backend é a autoridade**: validação de entrada, DTOs distintos das
  entidades, auditoria de operações sensíveis.
- **Histórico append-only**: uma fatura fechada e seus itens nunca são
  editados após gerados — apenas cancelados (R5).
- **Cursor pagination** sobre uma coluna portátil (`*Ticks`, `long`) para
  listagens de alto volume.
- **Reuso de padrões**: Result/Error, `PagedResult`/`PageRequest`, RBAC,
  `IAuditLogger`.

## Glossary

- **Período de fechamento**: intervalo `[PeriodStart, PeriodEnd]`
  correspondente a um mês calendário (ano+mês), usado como janela de
  consolidação de contadores.
- **Fechamento (Closing)**: operação que, para um período e um tenant,
  resolve o Contrato aplicável de cada Impressora monitorada, calcula o
  consumo por (Impressora, CounterType) e gera uma Fatura por Contrato com
  consumo faturável no período.
- **Consumo do período**: para um (Impressora, CounterType), a diferença
  entre a leitura de referência de fim de período e a leitura de referência
  de início de período, nunca negativa.
- **Excedente**: `max(0, Consumo − Quantidade incluída da Franquia)`.
- **Fatura (Invoice)**: documento gerado pelo Fechamento para um Contrato e
  um período, com um Item de Fatura por (Impressora, CounterType) faturado,
  e ciclo de vida próprio (Rascunho → Emitida → Cancelada).

## Requirements

### Requirement 1: Período de fechamento

**User Story:** Como responsável financeiro, quero que o fechamento opere
sobre um período mensal bem definido, para que o consumo faturado corresponda
a um ciclo de cobrança reconhecível.

#### Acceptance Criteria

1. THE Sistema SHALL definir um período de fechamento como um mês calendário
   (ano e mês), derivando `PeriodStart` (primeiro dia, 00:00 UTC) e
   `PeriodEnd` (último dia, 23:59:59 UTC) automaticamente a partir dele.
2. IF um fechamento é solicitado para um período cujo `PeriodEnd` ainda não
   chegou (período corrente ou futuro), THEN THE Sistema SHALL rejeitar a
   operação e retornar o código de status HTTP 400.
3. WHEN um fechamento é solicitado para um (tenant, período) já fechado com
   sucesso anteriormente, THE Sistema SHALL recusar a operação e retornar o
   código de status HTTP 409, sem gerar faturas duplicadas.

### Requirement 2: Consolidação de consumo por Impressora

**User Story:** Como responsável financeiro, quero que o sistema calcule o
consumo de cada impressora no período a partir do histórico de contadores,
para que o faturamento reflita o uso real sem depender de leitura manual.

#### Acceptance Criteria

1. WHEN o Fechamento processa uma Impressora monitorada do tenant, THE
   Sistema SHALL calcular, para cada `CounterType` com leituras no ou antes
   do período, o consumo como a leitura de referência de fim de período menos
   a leitura de referência de início de período.
2. THE Sistema SHALL definir a leitura de referência de início de período
   como a última leitura daquele (Impressora, CounterType) com timestamp
   menor ou igual ao início do período; SHALL definir a leitura de referência
   de fim de período como a última leitura com timestamp menor ou igual ao
   fim do período.
3. IF não existe nenhuma leitura de um (Impressora, CounterType) com
   timestamp menor ou igual ao início do período, THEN a leitura de
   referência de início de período SHALL ser a primeira leitura encontrada
   dentro do próprio período (evita faturar o valor acumulado histórico total
   na primeira fatura de uma impressora nova).
4. IF o consumo calculado for negativo (ex.: impressora substituída sem
   ajuste administrativo registrado), THEN THE Sistema SHALL tratar o
   consumo daquele (Impressora, CounterType) como zero nesse período, sem
   interromper o fechamento das demais impressoras.
5. THE Sistema SHALL ignorar, para fins de faturamento, qualquer (Impressora,
   CounterType) sem nenhuma leitura de referência disponível (nem antes, nem
   dentro do período).

### Requirement 3: Resolução de contrato e cálculo de excedente

**User Story:** Como responsável financeiro, quero que o sistema atribua
cada impressora ao contrato vigente correto e calcule o excedente contra a
franquia configurada, para que o valor faturado esteja correto.

#### Acceptance Criteria

1. WHEN o Fechamento processa uma Impressora, THE Sistema SHALL resolver o
   Contrato aplicável usando `ContractService.ResolveApplicableAsync` com a
   data de referência igual ao fim do período.
2. IF nenhum Contrato aplicável é resolvido para uma Impressora, THEN THE
   Sistema SHALL excluí-la do fechamento sem erro, sem gerar Item de Fatura
   para ela.
3. WHEN um Contrato é resolvido para uma Impressora, THE Sistema SHALL
   calcular, para cada (Impressora, CounterType) com consumo no período
   (R2), o excedente como o consumo menos a quantidade incluída da
   `ContractFranchise` daquele Contrato e CounterType, nunca negativo.
4. IF não existe `ContractFranchise` configurada para um CounterType com
   consumo no Contrato resolvido, THEN THE Sistema SHALL ignorar aquele
   (Impressora, CounterType) no cálculo de excedente, sem erro.
5. THE Sistema SHALL calcular o valor de cada Item de Fatura como o
   excedente multiplicado pelo preço unitário de excedente da Franquia,
   arredondado à precisão monetária da plataforma.

### Requirement 4: Geração de fatura

**User Story:** Como responsável financeiro, quero que o fechamento gere uma
fatura por contrato com os itens de excedente calculados, para consolidar o
valor a cobrar do cliente naquele período.

#### Acceptance Criteria

1. WHEN o Fechamento de um período é executado, THE Sistema SHALL agrupar os
   Itens de Fatura calculados por Contrato e gerar uma Fatura por Contrato
   que teve ao menos um Item de Fatura com excedente maior que zero.
2. THE Sistema SHALL armazenar por Fatura: identificador, Contrato, Cliente,
   período (início/fim), status, valor total, moeda, data de geração.
3. THE Sistema SHALL armazenar por Item de Fatura: Fatura, Impressora,
   CounterType, consumo, quantidade incluída, excedente, preço unitário,
   valor do item.
4. WHEN uma Fatura é gerada, THE Sistema SHALL persisti-la com status
   "Rascunho" e calcular o valor total como a soma dos valores dos seus
   Itens de Fatura.
5. WHEN um Contrato do tenant não tem nenhuma Impressora com excedente no
   período, THE Sistema SHALL não gerar Fatura para aquele Contrato naquele
   fechamento.

### Requirement 5: Ciclo de vida da fatura

**User Story:** Como responsável financeiro, quero emitir ou cancelar uma
fatura gerada, para controlar quais faturas estão formalizadas para
cobrança.

#### Acceptance Criteria

1. THE Sistema SHALL definir os status possíveis de uma Fatura: Rascunho,
   Emitida, Cancelada.
2. WHEN um Usuario autorizado transiciona uma Fatura de Rascunho para
   Emitida, THE Sistema SHALL registrar o evento no AuditLog com o ator e o
   timestamp; a partir daí a Fatura e seus Itens são imutáveis.
3. WHEN um Usuario autorizado cancela uma Fatura (Rascunho ou Emitida), THE
   Sistema SHALL transicioná-la para Cancelada e registrar o evento no
   AuditLog, sem excluir o registro nem seus Itens.
4. IF uma transição de status é solicitada a partir de "Cancelada", THEN THE
   Sistema SHALL recusar a operação e retornar o código de status HTTP 400
   (estado terminal).

### Requirement 6: Consulta de fechamentos e faturas

**User Story:** Como responsável financeiro, quero consultar o histórico de
fechamentos e as faturas geradas, para acompanhar o faturamento por período e
por cliente.

#### Acceptance Criteria

1. WHEN um Usuario autorizado lista Faturas, THE Sistema SHALL retornar
   somente as faturas do tenant_id do contexto autenticado, com paginação e
   filtro por Cliente/Contrato/período/status.
2. WHEN um Usuario autorizado consulta uma Fatura por identificador, THE
   Sistema SHALL retornar a Fatura com seus Itens, restrita ao tenant_id do
   contexto autenticado.
3. WHEN um Usuario autorizado consulta o histórico de fechamentos
   executados, THE Sistema SHALL retornar, por período, o resultado
   (quantidade de faturas geradas, quando foi executado, por quem).

### Requirement 7: Autorização, auditoria e escala

**User Story:** Como responsável de segurança, quero que as operações de
fechamento e fatura respeitem RBAC, auditoria e as garantias de isolamento
das fases anteriores.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover as permissões granulares
   `fechamento.view` (consulta de faturas/itens/histórico de fechamento) e
   `fechamento.manage` (executar fechamento, emitir/cancelar fatura).
2. WHEN um Usuario autenticado requisita uma operação de fechamento/fatura,
   THE Serviço_Autorizacao SHALL autorizar a operação somente se algum
   Papel do Usuario possuir a Permissao correspondente.
3. WHEN um fechamento é executado ou uma Fatura tem status alterado, THE
   Serviço_Auditoria SHALL registrar o evento no AuditLog com o ator, o
   tenant, e os dados relevantes da operação.
4. THE Sistema SHALL expor as operações de fechamento/fatura por meio de
   DTOs distintos das entidades de persistência.
5. IF uma requisição tenta acessar ou operar sobre uma Fatura, um Item de
   Fatura ou um registro de fechamento de um tenant_id distinto do contexto
   autenticado, THEN THE Sistema SHALL recusar a operação e retornar o
   código de status HTTP 404.
6. THE Sistema SHALL definir índices de banco de dados sobre tenant_id e
   sobre os campos de filtro/ordenação de Fatura, Item de Fatura e registro
   de fechamento.

## Questões em aberto para validação com o usuário antes do design

1. **Papéis e permissões (R7.1)**: confirmar o mapeamento — proposta:
   `Financeiro` recebe `fechamento.view` + `fechamento.manage` (dono
   natural do módulo, mesmo padrão de `contrato.*`); `Administrador` recebe
   ambas também; `Operacional`/`Supervisor` sem nenhuma (diferente de
   Contratos — fechamento é estritamente financeiro).
2. **Granularidade do fechamento (R1)**: confirmar que o fechamento é
   executado por tenant e por período (todas as impressoras do tenant de
   uma vez), não impressora a impressora ou contrato a contrato
   individualmente — mais simples operacionalmente e evita fechamentos
   parciais inconsistentes.
3. **Consumo negativo (R2.4)**: confirmar a proposta de tratar como zero
   (sem interromper o fechamento) em vez de rejeitar o fechamento inteiro
   ou sinalizar a impressora para revisão manual — dado que não há ainda um
   mecanismo de "pendência"/revisão na plataforma, zero é o comportamento
   mais simples e não bloqueia o faturamento das demais impressoras.
4. **Precisão/arredondamento monetário (R3.5)**: confirmar arredondamento
   bancário (`MidpointRounding.ToEven`) para 2 casas decimais no valor final
   de cada Item de Fatura (a franquia já guarda `decimal(18,4)` de preço
   unitário, mas o valor do item, exposto ao cliente, deve ter 2 casas).
5. **Refechamento (R1.3)**: confirmar que um período já fechado com sucesso
   nunca pode ser refeito automaticamente — se for necessário corrigir,
   isso exige cancelar as faturas geradas manualmente primeiro (fora de
   escopo desta fase reabrir um fechamento).
