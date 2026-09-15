# Requirements Document

## Introduction

Este documento especifica a **FASE 4 (Phase 4)** da plataforma **EasyPanel** — tema
**"Suprimentos (toner/cilindro)"**. A Fase 4 constrói sobre a Fase 2 (Monitoramento)
e a Fase 3 (Alertas e Notificações), ambas concluídas.

### Premissas verificadas no código antes de escrever este documento

- O Agente Windows (`src/windows-client/EasyPanel.WindowsClient.Snmp`) **já coleta**
  níveis de suprimento via SNMP: `DeviceReading.SupplyLevels` é um dicionário
  `rótulo → percentual (0–100)`, produzido pelos drivers de fabricante
  (`IPrinterDriver.Interpret`). O núcleo de descoberta (`PrinterDiscoveryService`)
  já os captura.
- Esse dado **ainda não é transmitido ao backend**: a submissão de coleta do agente
  (`ClientCollectionSubmission`/`SubmittedCounter`, em `Modules.Monitoring`, consumida
  pelo endpoint `POST /api/v1/client/collect`) só carrega contadores de página. Não há
  campo de suprimento no contrato de ingestão nem na entidade `Collection`.
- **Achado maior, que expande o escopo desta fase**: o Agente Windows real
  (`src/windows-client/EasyPanel.WindowsClient/`) registra apenas **um** serviço
  hospedado, `GuardianWorker` (supervisão). Os três serviços internos previstos pelo
  desenho da Fase 2 (`Net_Monitoring_Service`, `Communication_Service`,
  `Update_Service`, como implementações de `ISupervisedService` supervisionadas pelo
  `Guardian`) **nunca foram implementados** — hoje nenhuma implementação de
  `ISupervisedService` é registrada, então o `Guardian` supervisiona uma lista vazia.
  As peças de baixo nível existem e funcionam isoladamente (`PrinterDiscoveryService`
  + drivers SNMP, `QueueResender.ResendBatchAsync`, `ILocalQueue`/`SqliteLocalQueue`,
  `IBackendClient.SubmitCollectionAsync`), mas **nenhum laço periódico as liga**: hoje
  o agente não coleta nem envia contador algum de forma automática, não só suprimento.
  Consultado o usuário, a decisão foi que **a Fase 4 entrega essa orquestração**
  (ver Requirement 1) em vez de tratá-la como pré-requisito de uma fase separada,
  porque sem ela nenhum dado de suprimento (nem de contador) chega ao backend.
- O backend não possui **nenhuma persistência de suprimento** hoje — nem tipo de
  suprimento, nem histórico de leitura, nem limiar configurável.
- O motor de alertas da Fase 3 (`AlertEngine`) já é genérico o bastante para
  consumir qualquer `PrinterEventType` gravado em `PrinterEvent`: uma regra
  (`AlertRule`) casa por tipo de evento + escopo, sem conhecer a origem do evento.
  Isso permite **reusar integralmente** a Fase 3 para alertas de suprimento baixo,
  sem alterar `Modules.Alerting`: basta a Fase 4 gravar um novo `PrinterEventType`
  quando detectar nível baixo.
- Existe também um `IPrinterDriver`/`PrinterReading` **dentro do backend**
  (`Modules.Monitoring`), com o mesmo formato de `SupplyLevels`, mas **não é
  referenciado por nenhum consumidor real** (nem pelo agente Windows — que usa sua
  própria abstração em `EasyPanel.WindowsClient.Snmp` — nem pelo
  `CollectionProcessor`, que só desserializa `SubmittedCounter`). Parece ser um
  contrato vestigial/não conectado da Fase 2; esta spec não o redesenha, apenas
  registra o fato para quem for revisar o código (sinalizado ao usuário para decisão
  futura separada).

### Fora de escopo (adiado para fases futuras)

- **Estoque de suprimentos físicos** (itens, entradas/saídas, saldo) — Fase 5.
- **Pedido de reposição automático** ou integração com fornecedores.
- **SNMP v3** para leitura de suprimento — mesma limitação já registrada na Fase 2
  (v1/v2c implementados, v3 arquitetado para extensão futura).
- **Alertas de suprimento por Local ou Agente** — o escopo de `AlertRule` da Fase 3
  já suporta Tenant/Local/Impressora/Agente; a Fase 4 usa o escopo Impressora (mais
  natural para suprimento) e Tenant (regra ampla), sem introduzir novo tipo de escopo.
- **`Update_Service` (auto-update) como `ISupervisedService`**: o `UpdateApplier`/
  `UpdatePackageVerifier` já existem e não são tocados nesta fase; supervisioná-los
  via `Guardian` é ortogonal a suprimentos e fica para quando a auto-atualização for
  revisitada.
- **Descoberta interativa de novos equipamentos** (Requirement 7 da Fase 2, seleção
  manual de candidatas) — permanece como está; a Fase 4 só adiciona o ciclo de coleta
  **recorrente** sobre impressoras já registradas e com monitoramento habilitado.

### Princípios não funcionais herdados das Fases 1–3 (válidos na Fase 4)

- **Isolamento multi-tenant inegociável**: toda entidade nova é `TenantEntity`.
- **Backend é a autoridade**: validação de entrada, DTOs distintos das entidades,
  logs sem segredos, auditoria de configuração de limiar.
- **Escala**: histórico de leitura de suprimento é somente-adição, com os mesmos
  cuidados de cursor portável (`*Ticks`) já usados em `PrinterCounter`/`PrinterEvent`.
- **Reuso de padrões**: Result/Error, `PagedResult`/`PageRequest`, RBAC, `IAuditLogger`,
  workers de contexto de sistema, e — no agente — `ISupervisedService`/`Guardian`,
  `ILocalQueue`, `IBackendClient` já existentes da Fase 2.

## Glossary

- **SupplyLevel/Nível de suprimento**: leitura percentual (0–100) de um consumível de
  uma Impressora (toner, cilindro/drum, unidade de fusão, etc.), identificado por um
  rótulo de fabricante normalizado (ex.: `toner-preto`, `toner-ciano`, `cilindro`).
- **SupplyReading**: entidade de negócio que representa uma leitura de nível de
  suprimento em um instante — o equivalente, para suprimentos, do `PrinterCounter`
  da Fase 2.
- **SupplyThreshold**: limiar percentual configurável, por (Impressora, rótulo de
  suprimento), abaixo do qual aquele suprimento é considerado baixo, disparando um
  evento para o motor de alertas da Fase 3. Um valor não configurado cai para um
  padrão por Tenant e, na ausência deste, para um padrão de plataforma.
- **Previsão de troca**: estimativa de quando um suprimento atingirá 0%/o limiar,
  calculada por regressão linear simples sobre a taxa de queda observada no
  histórico recente de leituras.
- **PrinterEventType.SupplyLow**: novo valor do enum já existente
  (`Modules.Monitoring.PrinterEventType`), gravado quando uma leitura de suprimento
  cruza o limiar configurado — consumido pelo `AlertEngine` da Fase 3 sem nenhuma
  alteração no módulo de Alerting.
- **Net_Monitoring_Service**: serviço interno do agente (Fase 2, nunca implementado)
  que a Fase 4 entrega como `ISupervisedService`: executa o ciclo periódico de coleta
  (contadores + suprimentos) sobre as impressoras registradas e monitoráveis do Local.
- **Communication_Service**: serviço interno do agente (Fase 2, nunca implementado)
  que a Fase 4 entrega como `ISupervisedService`: drena periodicamente a
  `ILocalQueue` para o backend (reaproveitando `QueueResender.ResendBatchAsync`).
- **MonitoredPrinter**: descrição de uma Impressora monitorável entregue ao agente
  via `GET /api/v1/client/config` (extensão do `ClientConfig` da Fase 2): id no
  backend, endereço/protocolo/porta e fabricante, para o agente saber o que
  consultar via SNMP e sob qual `PrinterId` reportar.

## Requirements

### Requirement 1: Orquestração do ciclo de coleta no Agente Windows

**User Story:** Como operador da plataforma, quero que o agente colete e envie
dados periodicamente sem intervenção manual, para que o monitoramento (contadores e
suprimentos) funcione de ponta a ponta.

#### Acceptance Criteria

1. THE Windows_Client SHALL prover um `Net_Monitoring_Service` (implementação de
   `ISupervisedService`, supervisionada pelo `Guardian`) que, periodicamente, consulta
   via SNMP cada Impressora monitorável reportada pelo backend e produz uma leitura
   normalizada (contadores e níveis de suprimento).
2. THE Windows_Client SHALL obter a lista de impressoras monitoráveis e o intervalo
   de coleta a partir de `GET /api/v1/client/config` (extensão do `ClientConfig` da
   Fase 2 com a lista de `MonitoredPrinter`), reaplicando a última configuração
   válida conhecida quando a obtenção falha (R15.4 da Fase 2, já vigente).
3. WHEN o `Net_Monitoring_Service` completa uma leitura de uma Impressora, THE
   Windows_Client SHALL montar uma submissão de coleta (`ClientCollectionRequest`)
   com uma `Chave_de_Idempotencia` própria e enfileirá-la na `Fila_Local`
   (`ILocalQueue`), sem depender de conectividade imediata com o backend.
4. THE Windows_Client SHALL prover um `Communication_Service` (implementação de
   `ISupervisedService`, supervisionada pelo `Guardian`) que drena periodicamente a
   `Fila_Local` para o backend, reaproveitando o `QueueResender` já existente da
   Fase 2 (retry com espera crescente, sem descarte prematuro).
5. IF uma Impressora falha ao responder via SNMP durante um ciclo de coleta, THEN
   THE Net_Monitoring_Service SHALL registrar a falha para aquela Impressora e
   continuar o ciclo para as demais, sem interromper a varredura inteira.
6. THE Windows_Client SHALL excluir credenciais SNMP de qualquer submissão de
   coleta, log local ou item da `Fila_Local` (R8.7 da Fase 2, já vigente).

### Requirement 2: Transporte de níveis de suprimento do agente ao backend

**User Story:** Como operador da plataforma, quero que os níveis de suprimento
coletados pelo `Net_Monitoring_Service` cheguem ao backend, para que eu tenha
visibilidade do parque sem uma rotina de coleta separada.

#### Acceptance Criteria

1. THE Windows_Client SHALL incluir os níveis de suprimento lidos via SNMP
   (rótulo + percentual) na mesma submissão de coleta (`ClientCollectionRequest`)
   enviada ao endpoint `POST /api/v1/client/collect`, junto dos contadores.
2. THE Serviço_Ingestao SHALL aceitar níveis de suprimento como parte da mesma
   submissão idempotente de coleta já definida na Fase 2 (mesma `Chave_de_Idempotencia`),
   sem exigir uma submissão separada.
3. IF uma submissão de coleta não inclui níveis de suprimento, THEN THE
   Serviço_Ingestao SHALL processar normalmente os demais dados da coleta (R12/R13
   da Fase 2), sem falhar por ausência de suprimento.
4. WHEN uma coleta com níveis de suprimento é processada com sucesso, THE
   Serviço_Processamento SHALL persistir uma SupplyReading por rótulo reportado,
   vinculada à Impressora, ao Windows_Client e à Coleta de origem.

### Requirement 3: Histórico de leitura de suprimento

**User Story:** Como usuário operacional, quero manter o histórico de níveis de
suprimento de cada impressora, para acompanhar o consumo ao longo do tempo.

#### Acceptance Criteria

1. THE Sistema SHALL armazenar por SupplyReading os campos: identificador da
   Impressora, rótulo do suprimento, percentual (0–100), timestamp, identificador do
   Windows_Client e identificador da Coleta de origem.
2. THE Sistema SHALL persistir SupplyReading de forma somente-adição, mantendo o
   histórico completo (mesmo padrão de `PrinterCounter`).
3. WHEN um Usuario autorizado consulta o histórico de suprimento de uma Impressora,
   THE Sistema SHALL retornar os registros por meio de Paginacao_por_Cursor
   restritos ao tenant_id do contexto autenticado, com filtro opcional por rótulo.
4. WHEN um Usuario autorizado consulta os níveis atuais de uma Impressora, THE
   Sistema SHALL retornar a leitura mais recente de cada rótulo de suprimento
   detectado para aquela Impressora.

### Requirement 4: Limiar de nível baixo e integração com o motor de alertas

**User Story:** Como administrador do Tenant, quero ser alertado quando um
suprimento está acabando, para agir antes que a impressora pare por falta de toner.

#### Acceptance Criteria

1. THE Sistema SHALL prover um limiar percentual padrão por Tenant, aplicável a
   todo (Impressora, rótulo de suprimento) sem limiar específico configurado.
2. THE Sistema SHALL permitir configurar um limiar percentual específico por
   (Impressora, rótulo de suprimento) — ex.: um limiar para `toner-preto` e outro
   para `cilindro` na mesma Impressora —, que prevalece sobre o limiar padrão do
   Tenant quando presente.
3. WHEN uma SupplyReading é persistida com percentual igual ou inferior ao limiar
   vigente para aquele (Impressora, rótulo), E a leitura anterior do mesmo
   (Impressora, rótulo) estava acima do limiar (transição de cruzamento, evitando
   repetição a cada leitura), THE Serviço_Processamento SHALL registrar um
   PrinterEvent do tipo SupplyLow para aquela Impressora.
4. WHEN uma SupplyReading subsequente do mesmo (Impressora, rótulo) volta a ficar
   acima do limiar vigente, THE Serviço_Processamento SHALL permitir que uma leitura
   futura abaixo do limiar gere um novo PrinterEvent SupplyLow (o cruzamento é
   reavaliado a cada transição, não apenas uma vez por impressora).
5. THE Sistema SHALL permitir que um Usuario autorizado configure, via `AlertRule`
   já existente da Fase 3, uma regra que observe `PrinterEventType.SupplyLow` — sem
   exigir nenhuma nova entidade ou endpoint de alerta além dos já entregues na Fase 3.

### Requirement 5: Previsão de troca

**User Story:** Como usuário operacional, quero uma estimativa de quando um
suprimento vai acabar, para planejar a reposição com antecedência.

#### Acceptance Criteria

1. WHEN um Usuario autorizado consulta a previsão de troca de um suprimento de uma
   Impressora, THE Sistema SHALL calcular uma estimativa de data por regressão
   linear simples sobre as leituras mais recentes daquele (Impressora, rótulo),
   extrapolando a taxa de queda observada (percentual/dia) até 0%.
2. IF o histórico de leituras de um (Impressora, rótulo) é insuficiente para
   calcular uma taxa de queda confiável (ex.: menos de duas leituras distintas, ou
   percentual sem queda observada), THEN THE Sistema SHALL retornar a previsão como
   indisponível, sem erro.
3. THE Sistema SHALL expor a previsão de troca junto com a consulta de níveis
   atuais de uma Impressora (R3.4), sem exigir uma chamada adicional.

### Requirement 6: Autorização, auditoria e escala

**User Story:** Como responsável de segurança, quero que as novas operações de
suprimento respeitem RBAC, auditoria e as garantias de isolamento das fases
anteriores.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover as permissões granulares `supply.view`
   (consulta de níveis/histórico/previsão) e `supply.manage` (configuração de
   limiares).
2. WHEN um Usuario autenticado requisita uma operação de suprimento, THE
   Serviço_Autorizacao SHALL autorizar a operação somente se algum Papel do Usuario
   possuir a Permissao correspondente.
3. WHEN uma configuração de limiar (padrão do Tenant ou específico de Impressora) é
   criada ou alterada, THE Serviço_Auditoria SHALL registrar o evento no AuditLog
   com o ator, o valor anterior e o novo valor.
4. THE Sistema SHALL expor as operações de suprimento por meio de DTOs distintos
   das entidades de persistência.
5. IF uma requisição tenta acessar ou operar sobre SupplyReading ou configuração de
   limiar de um tenant_id distinto do contexto autenticado, THEN THE Sistema SHALL
   recusar a operação e retornar o código de status HTTP 404.
6. THE Sistema SHALL definir índices de banco de dados sobre tenant_id e sobre os
   campos de filtro/ordenação/cursor de SupplyReading.

## Decisões confirmadas pelo usuário

1. **Granularidade do limiar (R4.1/R4.2)**: confirmado — limiar por
   (Impressora, rótulo de suprimento), com um padrão por Tenant aplicado quando não
   há limiar específico.
2. **Modelo de previsão (R5.1)**: confirmado — regressão linear simples sobre as
   últimas N leituras (taxa de queda por dia, extrapolada até 0%).
3. **Transporte do agente (R2.1)**: confirmado — suprimento acoplado à mesma
   submissão de coleta já existente (mesma `Chave_de_Idempotencia`).
4. **Orquestração do ciclo de coleta (R1)**: confirmado — a Fase 4 entrega o
   `Net_Monitoring_Service`/`Communication_Service` que a Fase 2 previu mas nunca
   implementou, em vez de tratar isso como pré-requisito de uma fase/tarefa separada.
