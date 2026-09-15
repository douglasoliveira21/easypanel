# Requirements Document

## Introduction

Este documento especifica a **FASE 3 (Phase 3)** da plataforma **EasyPanel** — tema
**"Alertas e Notificações"**. A Fase 3 constrói sobre a Fase 2 (Monitoramento), já
concluída, que deixou o gancho pronto: a entidade `PrinterEvent` (somente-adição), hoje
gravada para heartbeat ausente (`HeartbeatMissing`), falha de coleta
(`CollectionFailure`) e mudança de status (`StatusChanged`), sem que nenhum motor de
regras a consuma.

Esta spec **assume como premissa** tudo que já foi entregue nas Fases 1 e 2: infra
containerizada, isolamento multi-tenant (`TenantEntity`, `ITenantContext`, filtros
globais + interceptor de `SaveChanges`, cross-tenant → 404), Identity/JWT/RBAC (papéis
e permissões `recurso.acao` avaliadas no backend), `AuditLog` somente-adição, Result
pattern, `PagedResult`/`PageRequest`, ProblemDetails, rate limiting, e as entidades de
monitoramento (`WindowsClient`, `Printer`, `PrinterCounter`, `PrinterEvent`,
`Collection`). Nenhum desses recursos é redefinido aqui.

A Fase 3 entrega: (a) **regras de alerta** configuráveis por tenant, que mapeiam
condições sobre `PrinterEvent` (e o estado corrente de `Printer`/`WindowsClient`) a uma
severidade e a um conjunto de canais de notificação; (b) um **motor de avaliação**
assíncrono que consome os `PrinterEvent` gravados e, quando uma regra corresponde, cria
ou atualiza um **Alerta** (`Alert`); (c) o **ciclo de vida do Alerta** (aberto,
reconhecido, resolvido) com histórico somente-adição de transições; (d) **canais de
notificação** por e-mail e por webhook de saída, com histórico de tentativas de envio e
retry; (e) **silenciamento (snooze)** de alertas por regra e/ou por impressora/agente
por um período determinado; e (f) as **permissões, auditoria e não-funcionais**
correspondentes.

### Fora de escopo (adiado para fases futuras)

- **Alertas de suprimentos** (nível de toner/cilindro) — dependem dos dados de
  `SupplyLevels` que só ganham modelo de domínio na Fase 4 (Suprimentos).
- **Canais adicionais** de notificação (SMS, push mobile, integrações com Slack/Teams
  como produto pronto) — a Fase 3 entrega e-mail e webhook genérico assinado; novos
  canais podem reusar a mesma abstração em fases futuras.
- **Telas de frontend (React)** — os requisitos permanecem focados em API/dados; a UI é
  construída separadamente.
- **Dashboards e relatórios agregados de alertas** (contagens, séries históricas,
  exportação) — Fase 9 (Relatórios e Dashboards).
- **Regras baseadas em contadores/consumo** (ex.: volume de páginas acima do esperado) —
  dependem da base de fechamento/faturamento das Fases 7–8; a Fase 3 cobre somente as
  condições já observáveis via `PrinterEvent` e estado corrente de `Printer`/
  `WindowsClient`.
- **Provedor de e-mail transacional específico** (ex.: SendGrid, SES) — a Fase 3 define
  a abstração de envio e um comportamento observável; a escolha do provedor concreto é
  decisão de infraestrutura tratada no design.

### Princípios não funcionais herdados das Fases 1–2 (válidos na Fase 3)

- O **isolamento multi-tenant é inegociável**: `AlertRule`, `Alert`,
  `AlertNotificationAttempt` e `AlertSilence` são entidades de negócio escopadas por
  Tenant.
- O **backend é a autoridade**: validar toda entrada, usar DTOs distintos das entidades,
  logs estruturados sem segredos (nunca registrar credenciais SMTP, segredos de
  assinatura de webhook ou corpo de e-mail com dados sensíveis em nível de erro),
  auditar ações sensíveis (criação/edição/exclusão de regra, reconhecimento/resolução de
  alerta, criação de silenciamento).
- **Projetado para escala** compatível com a Fase 2 (100k impressoras, milhões de
  eventos): o motor de avaliação processa `PrinterEvent` de forma assíncrona e
  incremental (sem varrer a tabela inteira a cada ciclo), paginação e índices sobre os
  campos de filtro/ordenação/cursor das novas entidades.
- **Reuso dos padrões existentes**: Result/Error, `PagedResult`/`PageRequest`,
  mapeamento ProblemDetails, RBAC (`[RequirePermission]`), `IAuditLogger`, workers de
  contexto de sistema Super Admin (`ISystemDbContextFactory`) para processamento
  cross-tenant, seguindo o modelo de `HeartbeatMonitor`/`CollectionProcessingWorker`.

## Glossary

- **EasyPanel**: A plataforma SaaS completa objeto deste produto.
- **Sistema**: O backend do EasyPanel (ASP.NET Core Web API).
- **Tenant**: Entidade organizacional isolada (Fase 1).
- **PrinterEvent**: Entidade somente-adição (Fase 2) que registra eventos de
  impressora/agente: mudança de status, movimentação, falha de coleta e heartbeat
  ausente. Fonte de dados do motor de alertas.
- **AlertRule**: Entidade de negócio que define, por Tenant, sob quais condições um
  Alerta deve ser gerado: tipo(s) de `PrinterEvent` observado(s), escopo (todo o Tenant,
  um Local específico ou uma Impressora/Agente específico), severidade resultante,
  limiar opcional (ex.: N ocorrências em uma janela de tempo) e os canais de notificação
  associados.
- **Serviço_Regras_Alerta**: Subsistema de backend responsável pelo CRUD e validação de
  `AlertRule`.
- **Motor_de_Alertas**: Subsistema de backend (worker assíncrono) responsável por
  consumir `PrinterEvent` ainda não avaliados, aplicar as `AlertRule` do Tenant
  correspondente e criar/atualizar `Alert`.
- **Alert**: Entidade de negócio que representa uma ocorrência de alerta gerada pelo
  Motor_de_Alertas a partir de uma `AlertRule`, com severidade, estado (aberto,
  reconhecido, resolvido), impressora/agente relacionado quando aplicável, e histórico
  de transições.
- **Severidade**: Classificação da relevância de um Alerta (ex.: informativo, atenção,
  crítico), definida pela `AlertRule` que o originou.
- **Estado_do_Alerta**: Um dos valores aberto, reconhecido ou resolvido.
- **AlertTransition**: Registro somente-adição de uma mudança de `Estado_do_Alerta`, com
  ator, horário e observação opcional.
- **Canal_de_Notificacao**: Meio pelo qual um Alerta é comunicado a um destinatário:
  e-mail ou webhook de saída, nesta fase.
- **AlertNotificationAttempt**: Entidade somente-adição que registra cada tentativa de
  envio de uma notificação de Alerta por um Canal_de_Notificacao, com resultado e
  horário.
- **Serviço_Notificacao**: Subsistema de backend responsável por despachar notificações
  de Alerta pelos Canal_de_Notificacao configurados e registrar
  `AlertNotificationAttempt`.
- **Webhook_de_Saida**: URL HTTPS configurada pelo Tenant em uma `AlertRule` para
  recebimento de notificações de Alerta, com corpo assinado por segredo compartilhado.
- **AlertSilence**: Entidade de negócio que representa um período durante o qual
  Alertas que corresponderiam a uma `AlertRule` (ou a uma Impressora/Agente específico)
  não geram notificação, mas continuam sendo registrados.
- **Serviço_Silenciamento**: Subsistema de backend responsável pelo CRUD de
  `AlertSilence` e por consultar se um Alerta está silenciado no momento da notificação.
- **RBAC**: Controle de Acesso Baseado em Papéis (Fase 1), estendido na Fase 3 com
  permissões de regra e alerta.
- **AuditLog**: Registro persistente somente-adição de eventos sensíveis (Fase 1).
- **DTO**: Data Transfer Object; contrato de dados de entrada/saída da API.
- **Pagina_de_Resultados**: Subconjunto ordenado e limitado de registros (Fase 1).
- **Paginacao_por_Cursor**: Esquema de paginação por cursor (Fase 2), usado para grandes
  volumes de `Alert` e `AlertNotificationAttempt`.

## Requirements

### Requirement 1: Definição de regras de alerta por tenant

**User Story:** Como administrador do Tenant, quero definir regras que determinem
quando um alerta deve ser gerado a partir dos eventos de monitoramento, para que a
equipe seja avisada apenas das condições que importam para o meu negócio.

#### Acceptance Criteria

1. THE Serviço_Regras_Alerta SHALL armazenar por AlertRule os campos: identificador,
   tenant_id, nome, descrição, ativo/inativo, tipo(s) de PrinterEvent observado(s),
   escopo (todo o Tenant, um Local específico ou uma Impressora/Agente específico),
   severidade resultante, limiar opcional de ocorrências em uma janela de tempo
   configurável, e os Canal_de_Notificacao associados.
2. WHEN um Usuario autorizado cria uma AlertRule informando dados válidos, THE
   Serviço_Regras_Alerta SHALL persistir a AlertRule vinculada ao tenant_id do contexto
   autenticado.
3. IF uma AlertRule é criada ou editada referenciando um Local ou uma
   Impressora/Agente de escopo que não pertence ao tenant_id do contexto autenticado,
   THEN THE Serviço_Regras_Alerta SHALL rejeitar a operação e retornar o código de
   status HTTP 404.
4. THE Serviço_Regras_Alerta SHALL restringir a severidade de uma AlertRule aos valores
   informativo, atenção e crítico.
5. WHEN um Usuario autorizado desativa uma AlertRule, THE Motor_de_Alertas SHALL deixar
   de gerar novos Alertas a partir dela, preservando os Alertas já existentes.
6. WHEN um Usuario autorizado consulta ou lista AlertRule, THE Serviço_Regras_Alerta
   SHALL retornar somente as regras do tenant_id do contexto autenticado, com suporte a
   filtros, paginação e ordenação.
7. WHEN uma operação de criação, edição ou exclusão de AlertRule é concluída, THE
   Serviço_Auditoria SHALL registrar o evento no AuditLog com o ator e o antes/depois
   relevante.

### Requirement 2: Motor de avaliação de eventos

**User Story:** Como operador da plataforma, quero que os eventos de monitoramento já
gravados na Fase 2 sejam avaliados automaticamente contra as regras do tenant, para que
alertas sejam gerados sem intervenção manual e sem varrer toda a base a cada ciclo.

#### Acceptance Criteria

1. WHILE existem PrinterEvent ainda não avaliados pelo Motor_de_Alertas, THE
   Motor_de_Alertas SHALL processá-los de forma incremental, por tenant, sem reavaliar
   PrinterEvent já processados.
2. WHEN um PrinterEvent é avaliado, THE Motor_de_Alertas SHALL considerar somente as
   AlertRule ativas do mesmo tenant_id cujo tipo de evento e escopo correspondam ao
   PrinterEvent.
3. IF uma AlertRule correspondente define um limiar de ocorrências em uma janela de
   tempo, THEN THE Motor_de_Alertas SHALL gerar o Alerta somente quando o número de
   PrinterEvent correspondentes dentro da janela atinge o limiar configurado.
4. IF uma AlertRule correspondente não define limiar, THEN THE Motor_de_Alertas SHALL
   gerar o Alerta a partir da primeira ocorrência correspondente do PrinterEvent.
5. IF já existe um Alerta em estado aberto ou reconhecido para a mesma AlertRule e a
   mesma Impressora/Agente relacionado, THEN THE Motor_de_Alertas SHALL associar o novo
   PrinterEvent correspondente ao Alerta existente em vez de criar um Alerta duplicado.
6. WHEN um PrinterEvent de mudança de status indica retorno à normalidade (ex.:
   impressora volta a Online, agente volta a Ativo) para uma Impressora/Agente com
   Alerta aberto ou reconhecido originado de uma condição de indisponibilidade, THE
   Motor_de_Alertas SHALL registrar a resolução automática do Alerta correspondente.
7. WHEN o Motor_de_Alertas cria um novo Alerta, THE Serviço_Notificacao SHALL ser
   acionado para despachar a notificação pelos Canal_de_Notificacao da AlertRule
   correspondente.
8. THE Motor_de_Alertas SHALL operar restrito a um Tenant por vez durante a avaliação,
   sem misturar PrinterEvent nem AlertRule de tenants distintos.

### Requirement 3: Ciclo de vida e consulta de alertas

**User Story:** Como usuário operacional, quero acompanhar o estado de cada alerta e
registrar seu reconhecimento e resolução, para que a equipe saiba o que já está sendo
tratado e o que ainda precisa de atenção.

#### Acceptance Criteria

1. THE Sistema SHALL armazenar por Alert os campos: identificador, tenant_id, AlertRule
   de origem, severidade, Estado_do_Alerta, Impressora/Agente relacionado quando
   aplicável, horário de criação, horário da última ocorrência correspondente, horário
   de reconhecimento, horário de resolução e o histórico de AlertTransition.
2. THE Sistema SHALL restringir o Estado_do_Alerta aos valores aberto, reconhecido e
   resolvido.
3. WHEN um Usuario autorizado reconhece um Alert do próprio Tenant em estado aberto, THE
   Sistema SHALL transicionar o Alert para reconhecido e registrar uma AlertTransition
   com o ator e o horário.
4. WHEN um Usuario autorizado resolve manualmente um Alert do próprio Tenant em estado
   aberto ou reconhecido, THE Sistema SHALL transicionar o Alert para resolvido e
   registrar uma AlertTransition com o ator, o horário e uma observação opcional.
5. THE Sistema SHALL persistir o histórico de AlertTransition de forma somente-adição,
   sem permitir exclusão de entradas existentes.
6. WHEN um Usuario autorizado lista Alert, THE Sistema SHALL retornar uma
   Pagina_de_Resultados restrita ao tenant_id do contexto autenticado, com suporte a
   filtros por estado, severidade, AlertRule, Impressora/Agente e intervalo de tempo, e
   ordenação.
7. WHEN um Usuario autorizado consulta um Alert por identificador, THE Sistema SHALL
   retornar o Alert somente se pertencer ao tenant_id do contexto autenticado, incluindo
   seu histórico de AlertTransition.
8. WHEN uma transição de Estado_do_Alerta é concluída, THE Serviço_Auditoria SHALL
   registrar o evento no AuditLog com o ator, o estado anterior e o novo estado.

### Requirement 4: Notificação por e-mail

**User Story:** Como administrador do Tenant, quero que os alertas críticos sejam
enviados por e-mail aos destinatários que eu configurar, para que a equipe seja avisada
mesmo sem estar com o painel aberto.

#### Acceptance Criteria

1. THE Serviço_Regras_Alerta SHALL permitir associar a uma AlertRule uma lista de
   endereços de e-mail destinatários quando o Canal_de_Notificacao e-mail é selecionado.
2. WHEN o Serviço_Notificacao despacha uma notificação de Alerta pelo canal e-mail, THE
   Serviço_Notificacao SHALL compor uma mensagem com a AlertRule de origem, a
   severidade, a Impressora/Agente relacionado quando aplicável e um resumo do evento,
   sem incluir credenciais ou dados sensíveis.
3. WHEN o envio de uma notificação por e-mail é concluído, THE Serviço_Notificacao SHALL
   registrar um AlertNotificationAttempt com o resultado (sucesso ou falha), o horário e
   o canal.
4. IF o envio de uma notificação por e-mail falha, THEN THE Serviço_Notificacao SHALL
   aplicar nova tentativa segundo uma política de retry com espera crescente, sem
   bloquear a avaliação de outros Alertas.
5. THE Sistema SHALL excluir dos logs de aplicação o corpo completo de e-mails enviados
   e quaisquer credenciais do provedor de e-mail.

### Requirement 5: Notificação por webhook

**User Story:** Como responsável técnico do Tenant, quero configurar um webhook de
saída para receber alertas em meus próprios sistemas, para que eu integre o EasyPanel a
ferramentas externas de operação.

#### Acceptance Criteria

1. THE Serviço_Regras_Alerta SHALL permitir associar a uma AlertRule uma URL HTTPS de
   Webhook_de_Saida e um segredo compartilhado quando o Canal_de_Notificacao webhook é
   selecionado.
2. WHEN o Serviço_Notificacao despacha uma notificação de Alerta pelo canal webhook, THE
   Serviço_Notificacao SHALL enviar uma requisição HTTPS de saída com o corpo da
   notificação assinado por HMAC usando o segredo compartilhado configurado.
3. IF a URL configurada para um Webhook_de_Saida não usa HTTPS, THEN THE
   Serviço_Regras_Alerta SHALL rejeitar a configuração e retornar o código de status
   HTTP 400.
4. WHEN o envio de uma notificação por webhook é concluído, THE Serviço_Notificacao
   SHALL registrar um AlertNotificationAttempt com o resultado (sucesso ou falha,
   incluindo o código de status HTTP retornado quando aplicável), o horário e o canal.
5. IF o envio de uma notificação por webhook falha ou não recebe resposta de sucesso do
   destinatário, THEN THE Serviço_Notificacao SHALL aplicar nova tentativa segundo uma
   política de retry com espera crescente, até um número máximo de tentativas
   configurável.
6. THE Sistema SHALL excluir o segredo compartilhado de Webhook_de_Saida de qualquer
   resposta de API e de logs de aplicação.

### Requirement 6: Histórico de notificações

**User Story:** Como operador da plataforma, quero consultar o histórico de tentativas
de notificação de um alerta, para que eu diagnostique falhas de entrega.

#### Acceptance Criteria

1. THE Sistema SHALL persistir AlertNotificationAttempt de forma somente-adição.
2. WHEN um Usuario autorizado consulta o histórico de notificações de um Alert do
   próprio Tenant, THE Sistema SHALL retornar os AlertNotificationAttempt por meio de
   Paginacao_por_Cursor restritos ao tenant_id do contexto autenticado.
3. THE Sistema SHALL definir índices de banco de dados sobre tenant_id e sobre os campos
   de filtro, ordenação e cursor de Alert e AlertNotificationAttempt.

### Requirement 7: Silenciamento de alertas

**User Story:** Como usuário operacional, quero silenciar temporariamente alertas de uma
regra ou de uma impressora/agente específico durante uma janela de manutenção, para que
eu não seja notificado de condições já conhecidas e tratadas.

#### Acceptance Criteria

1. THE Serviço_Silenciamento SHALL armazenar por AlertSilence os campos: identificador,
   tenant_id, escopo (uma AlertRule específica e/ou uma Impressora/Agente específico),
   horário de início, horário de término, ator e justificativa opcional.
2. WHEN um Usuario autorizado cria um AlertSilence do próprio Tenant com dados válidos,
   THE Serviço_Silenciamento SHALL persistir o AlertSilence e passar a considerá-lo a
   partir do horário de início configurado.
3. WHILE um AlertSilence vigente corresponde a um Alerta recém-gerado, THE
   Serviço_Notificacao SHALL registrar o Alerta normalmente, mas SHALL NOT despachar
   notificações pelos Canal_de_Notificacao da AlertRule correspondente.
4. WHEN o horário de término de um AlertSilence é atingido, THE Serviço_Silenciamento
   SHALL deixar de aplicá-lo a novos Alertas, sem afetar Alertas já gerados durante sua
   vigência.
5. WHEN um Usuario autorizado encerra antecipadamente um AlertSilence do próprio
   Tenant, THE Serviço_Silenciamento SHALL deixar de aplicá-lo imediatamente.
6. WHEN um Usuario autorizado lista AlertSilence, THE Serviço_Silenciamento SHALL
   retornar somente os silenciamentos do tenant_id do contexto autenticado.
7. WHEN uma operação de criação ou encerramento de AlertSilence é concluída, THE
   Serviço_Auditoria SHALL registrar o evento no AuditLog.

### Requirement 8: Autorização, auditoria e escala

**User Story:** Como responsável de segurança, quero que as novas operações de alerta
respeitem RBAC, auditoria e as garantias de escala e isolamento das fases anteriores,
para que a Fase 3 não introduza brechas de segurança ou multi-tenancy.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover as permissões granulares `alert.view`,
   `alert.manage` (CRUD de AlertRule e AlertSilence) e `alert.acknowledge` (reconhecer e
   resolver Alert).
2. WHEN um Usuario autenticado requisita uma operação sobre AlertRule, Alert ou
   AlertSilence, THE Serviço_Autorizacao SHALL autorizar a operação somente se algum
   Papel do Usuario possuir a Permissao correspondente.
3. THE Sistema SHALL expor as operações de AlertRule, Alert, AlertNotificationAttempt e
   AlertSilence por meio de DTOs distintos das entidades de persistência.
4. WHEN uma requisição aos endpoints da Fase 3 contém entrada inválida em relação ao DTO
   esperado, THE Sistema SHALL rejeitar a requisição e retornar o código de status HTTP
   400 com detalhes de validação.
5. THE Motor_de_Alertas SHALL executar em contexto de sistema (cross-tenant) somente
   para orquestrar a varredura de trabalho pendente, aplicando as regras de cada Alert,
   AlertRule e PrinterEvent estritamente dentro do próprio tenant_id de cada um,
   nunca combinando dados de tenants distintos em uma mesma avaliação.
6. IF uma requisição tenta acessar ou operar sobre AlertRule, Alert, AlertSilence ou
   AlertNotificationAttempt de um tenant_id distinto do contexto autenticado, THEN THE
   Sistema SHALL recusar a operação e retornar o código de status HTTP 404.

## Questões em aberto para validação com o usuário antes do design

1. **Provedor de e-mail**: a Fase 1/2 não têm integração SMTP alguma (hoje existe
   apenas `LogOnlyPasswordResetNotifier`, que só grava em log). Confirmar se a Fase 3
   deve integrar um provedor SMTP real (ex.: SMTP genérico configurável via appsettings)
   ou se, tal como a Fase 1, um notificador "log-only" é aceitável para esta fase,
   deixando o provedor real para configuração de infraestrutura posterior.
2. **Escopo da AlertRule**: confirmar se o escopo "uma Impressora/Agente específico" é
   necessário já nesta fase ou se, para o primeiro corte, regras por Tenant inteiro
   e/ou por Local já atendem — isso simplifica o modelo de dados e a UI futura.
3. **Resolução automática (R2.6)**: confirmar o comportamento desejado — hoje a Fase 2
   grava `PrinterEventType.StatusChanged` sempre que o status muda; a regra proposta é
   resolver automaticamente um Alerta de indisponibilidade quando a impressora/agente
   volta a um status saudável. Confirmar se esse comportamento é desejado por padrão ou
   se deve ser uma opção por AlertRule.
