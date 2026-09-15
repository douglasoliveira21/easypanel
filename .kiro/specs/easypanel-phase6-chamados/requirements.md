# Requirements Document

## Introduction

Este documento especifica a **FASE 6 (Phase 6)** da plataforma **EasyPanel** — tema
**"Chamados/Helpdesk e SLA"**. A Fase 6 constrói sobre a Fase 1 (Clientes/Locais,
Identity/RBAC), a Fase 2 (Impressoras) e a Fase 5 (Estoque), todas concluídas.

### Premissas verificadas no código antes de escrever este documento

- **Não existe nenhum código de chamados hoje.** O papel `Tecnico` já traz o
  comentário "chamados/SLA em fases futuras" desde a Fase 1
  (`Modules.Identity.Roles`) — esta é essa fase. `Supervisor` é o outro papel
  historicamente sinalizado como dono deste módulo (oversight/auditoria).
- **`Modules.Contracts` não existe** (Fase 7, ainda não iniciada). O roteiro em
  `HANDOFF.md` descreve a Fase 6 como "SLA por contrato", mas sem um Contrato
  para vincular, o SLA desta fase é necessariamente uma **política por tenant**
  (com granularidade opcional por prioridade), não por contrato — ver "Fora de
  escopo" e "Questões em aberto" abaixo. Quando a Fase 7 existir, o vínculo
  Chamado→Contrato pode ser adicionado sem quebrar o modelo desta fase (mesmo
  padrão de referência por `Guid` já usado entre módulos).
- **MinIO está provisionado mas não tem uso funcional.** `docker-compose.yml`
  sobe o serviço e `StorageHealthCheck` só verifica `/minio/health/live`; não
  existe `IFileStorage`/SDK S3 no código. Esta fase é a primeira a implementar
  upload/download funcional de anexos — é item de escopo real, não apenas
  configuração já pronta.
- **Referências por `Guid`, sem FK de módulo cruzado**: `Customer.Id`,
  `Location.Id` (com `CustomerId`) e `Printer.Id` (com `CustomerId`/
  `LocationId`) existem em `Modules.Customers`/`Modules.Monitoring`. Um Chamado
  referenciará Cliente/Local/Impressora apenas por `Guid`, validados quanto à
  existência/tenant em `Infrastructure.Ticketing` — mesmo padrão já usado em
  `Modules.Alerting`/`Modules.Inventory`.
- **Papel `Cliente` já existe** (`Modules.Identity.Roles`) mas o portal
  self-service do cliente é a Fase 10. Nesta fase, chamados são operados pelos
  papéis internos (Administrador/Supervisor/Tecnico/Operacional); abertura por
  um usuário do papel `Cliente` fica fora de escopo (ver abaixo).

### Fora de escopo (adiado para fases futuras)

- **SLA vinculado a Contrato** — depende da Fase 7 (Contratos), inexistente
  ainda. Esta fase usa uma política de SLA por tenant (e, opcionalmente, por
  prioridade), com o vínculo a Contrato como extensão natural de uma fase
  futura.
- **Portal do cliente (abertura/acompanhamento de chamado pelo papel
  `Cliente`)** — pertence à Fase 10 (Portal do Cliente), que trata de todo o
  acesso self-service do tenant-cliente com escopo de permissões restrito
  próprio.
- **Notificação proativa de violação de SLA** (e-mail/webhook quando um prazo
  estoura) — o motor de notificação existe (`Modules.Alerting`/
  `AlertNotificationDispatcher`), mas ele consome exclusivamente `PrinterEvent`
  por Impressora/Agente; um Chamado não tem esse vínculo direto. Esta fase
  expõe a consulta/flag de chamados com SLA violado (R4), mas não dispara
  notificação — possível extensão futura do motor de alertas, sinalizada mas
  não implementada aqui.
- **Horário comercial / SLA em "horas úteis"** — o cálculo de prazo desta fase
  usa tempo corrido (calendário), não descontando fins de semana/feriados/
  janela de atendimento. Ver "Questões em aberto".
- **Base de conhecimento, macros/respostas prontas, pesquisa de satisfação** —
  não fazem parte do escopo mínimo de helpdesk+SLA desta fase.

### Princípios não funcionais herdados das Fases 1–5 (válidos na Fase 6)

- **Isolamento multi-tenant inegociável**: toda entidade nova é `TenantEntity`.
- **Backend é a autoridade**: validação de entrada, DTOs distintos das
  entidades, auditoria de operações sensíveis.
- **Histórico append-only**: interações/mudanças de status de um Chamado nunca
  são alteradas ou removidas, mesmo padrão de `PrinterMovement`/
  `InventoryMovement`.
- **Cursor pagination** sobre uma coluna portátil (`*Ticks`, `long`) para
  listagens/históricos de alto volume — mesma armadilha de SQLite sobre
  `DateTimeOffset` já documentada nas fases anteriores.
- **Reuso de padrões**: Result/Error, `PagedResult`/`PageRequest`, RBAC,
  `IAuditLogger`.

## Glossary

- **Chamado/Ticket**: registro de uma solicitação ou incidente aberto por um
  Cliente/Local, atribuído a um Técnico, com ciclo de vida de status até o
  fechamento.
- **SLA (Service Level Agreement)**: prazo configurável, por prioridade, para
  primeira resposta e para resolução de um Chamado.
- **Interação**: entrada no histórico append-only de um Chamado — comentário,
  mudança de status ou de atribuição.
- **Anexo**: arquivo (imagem, PDF, documento) vinculado a um Chamado ou a uma
  Interação, armazenado no MinIO.

## Requirements

### Requirement 1: Abertura de chamados

**User Story:** Como usuário operacional, quero abrir um chamado vinculado a um
Cliente e, opcionalmente, a um Local/Impressora, para registrar uma solicitação
ou incidente.

#### Acceptance Criteria

1. THE Sistema SHALL armazenar por Chamado os campos: identificador, título,
   descrição, Cliente, Local opcional, Impressora opcional, prioridade, status,
   solicitante (ator de abertura), responsável atribuído opcional, timestamps
   de criação/última atualização.
2. WHEN um Usuario autorizado abre um Chamado informando dados válidos, THE
   Sistema SHALL persistir o Chamado vinculado ao tenant_id do contexto
   autenticado, com status inicial "Aberto".
3. IF um Chamado é criado sem título ou sem Cliente válido do tenant, THEN THE
   Sistema SHALL rejeitar a operação e retornar o código de status HTTP 400.
4. WHEN um Chamado referencia Local e/ou Impressora, THE Sistema SHALL validar
   que ambos pertencem ao tenant_id do contexto autenticado antes de persistir.
5. WHEN um Chamado é aberto, THE Sistema SHALL calcular e persistir os prazos
   de SLA de primeira resposta e de resolução, conforme a política vigente
   para a prioridade informada (R4).

### Requirement 2: Ciclo de vida, atribuição e histórico

**User Story:** Como Técnico ou Supervisor, quero atualizar o status e a
atribuição de um chamado e registrar interações, para acompanhar o atendimento
até o fechamento.

#### Acceptance Criteria

1. THE Sistema SHALL definir os status possíveis de um Chamado: Aberto, Em
   Andamento, Aguardando Cliente, Resolvido, Fechado, Cancelado.
2. WHEN um Usuario autorizado transiciona o status de um Chamado, THE Sistema
   SHALL validar que a transição é permitida a partir do status corrente e
   registrar a transição como Interação append-only, com o ator e o timestamp.
3. IF uma transição de status inválida é solicitada (ex.: reabrir um Chamado
   Cancelado sem uma ação explícita de reabertura), THEN THE Sistema SHALL
   recusar a operação e retornar o código de status HTTP 400.
4. WHEN um Usuario autorizado atribui ou reatribui um Chamado a um Técnico, THE
   Sistema SHALL validar que o Técnico pertence ao tenant_id do contexto
   autenticado e registrar a atribuição como Interação append-only.
5. WHEN um Usuario autorizado registra um comentário num Chamado, THE Sistema
   SHALL persistir a Interação de forma append-only, com o ator e o timestamp,
   sem permitir alteração ou exclusão de Interações existentes.
6. WHEN um Usuario autorizado consulta o histórico de Interações de um
   Chamado, THE Sistema SHALL retornar os registros por meio de
   Paginacao_por_Cursor, mais recentes primeiro, restritos ao tenant_id do
   contexto autenticado.
7. WHEN o status de um Chamado muda para "Resolvido" pela primeira vez, THE
   Sistema SHALL registrar o timestamp de resolução, usado no cálculo de
   cumprimento do SLA (R4).

### Requirement 3: Consulta e listagem de chamados

**User Story:** Como usuário operacional, quero listar e filtrar chamados, para
priorizar o atendimento.

#### Acceptance Criteria

1. WHEN um Usuario autorizado lista Chamados, THE Sistema SHALL retornar
   somente os chamados do tenant_id do contexto autenticado, com paginação.
2. THE Sistema SHALL permitir filtrar a listagem de Chamados por status,
   prioridade, Cliente, Local, Impressora e Técnico responsável.
3. WHEN um Usuario autorizado consulta um Chamado por identificador, THE
   Sistema SHALL retornar o Chamado apenas se pertencer ao tenant_id do
   contexto autenticado, incluindo os prazos de SLA e o indicador de violação
   (R4).

### Requirement 4: Política de SLA e cálculo de prazos

**User Story:** Como Supervisor, quero configurar prazos de SLA por
prioridade, para medir se os chamados estão sendo atendidos dentro do
combinado.

#### Acceptance Criteria

1. THE Sistema SHALL permitir configurar, por tenant e por prioridade, um
   prazo de primeira resposta e um prazo de resolução (em minutos), como
   política de SLA.
2. IF um tenant não tem política de SLA configurada para uma prioridade, THEN
   THE Sistema SHALL aplicar um prazo padrão de plataforma para essa
   prioridade.
3. WHEN um Chamado é aberto, THE Sistema SHALL calcular os prazos-limite de
   primeira resposta e de resolução somando o prazo da política vigente ao
   timestamp de abertura (tempo corrido).
4. WHEN a primeira Interação de resposta de um Técnico é registrada num
   Chamado, THE Sistema SHALL marcar o prazo de primeira resposta como
   cumprido ou violado, comparando o timestamp da Interação ao prazo-limite
   calculado.
5. WHEN um Chamado transiciona para "Resolvido", THE Sistema SHALL marcar o
   prazo de resolução como cumprido ou violado, comparando o timestamp da
   resolução ao prazo-limite calculado.
6. WHEN um Usuario autorizado consulta Chamados com SLA violado, THE Sistema
   SHALL retornar os Chamados do tenant_id do contexto autenticado cujo prazo
   de primeira resposta ou de resolução esteja violado ou, para Chamados ainda
   abertos, já vencido no momento da consulta.
7. WHEN uma política de SLA é criada ou alterada, THE Serviço_Auditoria SHALL
   registrar o evento no AuditLog com o ator, a prioridade, o valor anterior e
   o novo valor.

### Requirement 5: Anexos

**User Story:** Como Técnico, quero anexar arquivos (fotos, documentos) a um
chamado, para documentar o atendimento.

#### Acceptance Criteria

1. WHEN um Usuario autorizado envia um anexo para um Chamado, THE Sistema
   SHALL armazenar o arquivo no MinIO num caminho que isola o tenant_id do
   contexto autenticado, e persistir os metadados (nome, tipo, tamanho, ator,
   timestamp) vinculados ao Chamado.
2. IF um anexo excede o tamanho máximo configurado ou tem um tipo de arquivo
   não permitido, THEN THE Sistema SHALL rejeitar o envio e retornar o código
   de status HTTP 400, sem persistir metadados nem o arquivo.
3. WHEN um Usuario autorizado solicita o download de um anexo, THE Sistema
   SHALL retornar o arquivo apenas se o Chamado associado pertencer ao
   tenant_id do contexto autenticado.
4. WHEN um Usuario autorizado lista os anexos de um Chamado, THE Sistema SHALL
   retornar apenas os anexos do Chamado, restritos ao tenant_id do contexto
   autenticado.

### Requirement 6: Autorização, auditoria e escala

**User Story:** Como responsável de segurança, quero que as operações de
chamados respeitem RBAC, auditoria e as garantias de isolamento das fases
anteriores.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover as permissões granulares
   `chamado.view` (consulta de chamados/histórico/anexos), `chamado.manage`
   (abertura, atualização de status, atribuição, comentário, anexo) e
   `sla.manage` (configuração de política de SLA).
2. WHEN um Usuario autenticado requisita uma operação de chamados ou de SLA,
   THE Serviço_Autorizacao SHALL autorizar a operação somente se algum Papel
   do Usuario possuir a Permissao correspondente.
3. WHEN um Chamado é criado, tem status/atribuição alterados, recebe anexo, ou
   quando uma política de SLA é alterada, THE Serviço_Auditoria SHALL
   registrar o evento no AuditLog com o ator, o tenant, e os dados relevantes
   da operação.
4. THE Sistema SHALL expor as operações de chamados e de SLA por meio de DTOs
   distintos das entidades de persistência.
5. IF uma requisição tenta acessar ou operar sobre um Chamado, uma Interação,
   um Anexo ou uma política de SLA de um tenant_id distinto do contexto
   autenticado, THEN THE Sistema SHALL recusar a operação e retornar o código
   de status HTTP 404.
6. THE Sistema SHALL definir índices de banco de dados sobre tenant_id e sobre
   os campos de filtro/ordenação/cursor de Chamado, Interação e Anexo.

## Questões em aberto para validação com o usuário antes do design

1. **Papéis e permissões (R6.1)**: confirmar o mapeamento — proposta:
   `Supervisor` recebe `chamado.view` + `chamado.manage` + `sla.manage`
   (dono do módulo, já com perfil de oversight/auditoria); `Tecnico` recebe
   `chamado.view` + `chamado.manage` (atende chamados, mas não configura SLA);
   `Administrador` recebe todas as três; `Operacional` recebe apenas
   `chamado.view` (para saber o que está em aberto); `Financeiro`/`Estoque`
   sem permissões de chamado.
2. **Granularidade da política de SLA (R4.1)**: confirmar se basta uma
   política por (tenant, prioridade) — proposta atual, mais simples — ou se é
   necessária desde já uma política por (tenant, Cliente, prioridade), para
   diferenciar SLA por cliente mesmo sem o módulo de Contratos.
3. **Armazenamento de anexos (R5)**: confirmar a abordagem técnica para o
   cliente MinIO — proposta: usar o SDK `Minio` (pacote oficial,
   S3-compatible) ou o `AWSSDK.S3` (mais maduro, mais pesado) para
   upload/download via presigned URL ou stream direto pelo backend; e
   confirmar limites propostos (tamanho máximo por arquivo, tipos MIME
   permitidos) — sugestão inicial: 10 MB por arquivo, imagens comuns + PDF.
4. **Prioridades do Chamado**: confirmar o conjunto fixo proposto — Baixa,
   Média, Alta, Urgente — e os prazos padrão de plataforma (R4.2) para cada
   uma (sugestão: primeira resposta 4h/2h/1h/30min; resolução 48h/24h/8h/4h,
   tempo corrido).
