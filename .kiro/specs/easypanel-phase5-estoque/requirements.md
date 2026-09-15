# Requirements Document

## Introduction

Este documento especifica a **FASE 5 (Phase 5)** da plataforma **EasyPanel** — tema
**"Estoque"**. A Fase 5 constrói sobre a Fase 1 (Clientes/Locais), a Fase 2
(Impressoras) e a Fase 4 (Suprimentos), todas concluídas.

### Premissas verificadas no código antes de escrever este documento

- **Não existe nenhum código de estoque hoje** além do papel `Estoque`
  (`Modules.Identity.Roles`), já previsto desde a Fase 1 com permissões mínimas
  (`customer.view`, `location.view`) e o comentário "permissões de estoque reais em
  fases futuras" — esta é essa fase.
- A Fase 4 entregou `SupplyReading`/`SupplyThreshold` (`Modules.Monitoring`), que
  rastreiam o **nível percentual restante** de um suprimento já instalado numa
  impressora (leitura SNMP), não o **estoque físico** de peças de reposição. As
  duas coisas são complementares e serão ligadas nesta fase por um campo de rótulo
  compartilhado (`Label`), mas continuam sendo entidades e fluxos distintos: a
  Fase 5 não lê nem escreve `SupplyReading`/`SupplyThreshold`.
- `Printer` (`Modules.Monitoring`) tem `CustomerId`/`LocationId`; `Location`
  (`Modules.Customers`) pertence a um `Customer`. O saldo de estoque desta fase é
  por **Local** (mesmo nível da hierarquia Tenant → Cliente → Local já
  estabelecida), não por impressora individual.
- O motor de alertas da Fase 3 (`AlertEngine`) consome exclusivamente
  `PrinterEvent`, resolvido a partir de uma Impressora ou de um Agente
  (`WindowsClient`). Um item de estoque abaixo do mínimo não tem Impressora nem
  Agente associados diretamente — portanto **não é reaproveitável sem estender o
  modelo do `AlertEngine`**, diferente do que aconteceu com o suprimento SNMP na
  Fase 4. Esta spec trata a integração com alertas como fora de escopo (ver
  abaixo) em vez de forçar um encaixe artificial.

### Fora de escopo (adiado para fases futuras)

- **Alertas automáticos de estoque baixo** via `AlertEngine` — motivo técnico
  registrado acima. Esta fase expõe a consulta de itens abaixo do mínimo
  (R5), mas não gera notificação proativa; isso pode entrar como extensão do
  motor de alertas numa fase futura, se desejado.
- **Pedido de compra/reposição automática** ou integração com fornecedores.
  Também fora de escopo: pedido de compra é o passo natural depois de "abaixo do
  mínimo" e pertence potencialmente a uma fase de Contratos/Fornecedores.
  *(Sinalizar ao usuário como possível gancho futuro, não implementar aqui.)*
- **Baixa automática de estoque a partir de uma leitura de suprimento da Fase 4**
  (ex.: decrementar 1 unidade de "toner-preto" quando o SNMP reporta troca) — o
  vínculo entre `InventoryItem` e o rótulo de suprimento (R4) é apenas
  informativo/de referência nesta fase; a baixa continua sendo um lançamento
  manual de saída.
- **Múltiplas unidades de medida com conversão** (ex.: caixa ↔ unidade) — cada
  item tem uma unidade única e as quantidades são inteiras.

### Princípios não funcionais herdados das Fases 1–4 (válidos na Fase 5)

- **Isolamento multi-tenant inegociável**: toda entidade nova é `TenantEntity`.
- **Backend é a autoridade**: validação de entrada, DTOs distintos das entidades,
  auditoria de operações sensíveis.
- **Histórico append-only**: movimentações de estoque nunca são alteradas ou
  removidas, mesmo padrão de `PrinterMovement`/`PrinterCounter`.
- **Escala**: o saldo por (Item, Local) é mantido como valor materializado
  atualizado atomicamente a cada movimentação (não recalculado por soma a cada
  consulta), para consulta O(1) mesmo com histórico extenso — mesmo raciocínio
  de design já usado nesta base para dados de alto volume.
- **Reuso de padrões**: Result/Error, `PagedResult`/`PageRequest`, RBAC,
  `IAuditLogger`.

## Glossary

- **InventoryItem/Item de estoque**: entidade de catálogo de um consumível
  controlado pelo Tenant (ex.: "Toner HP CF410A Preto"), com unidade de medida e,
  opcionalmente, um rótulo de suprimento (`Label`) que o relaciona à leitura SNMP
  da Fase 4 (mesmo valor usado em `SupplyReading.Label`/`SupplyThreshold.Label`).
- **InventoryMovement/Movimentação**: lançamento somente-adição de entrada ou
  saída de um Item num Local, em uma quantidade, num instante, por um ator.
- **Saldo**: quantidade corrente de um Item num Local — soma de entradas menos
  saídas, mantida como valor materializado.
- **Estoque mínimo**: quantidade configurável abaixo da qual um (Item, Local) é
  considerado em nível baixo.

## Requirements

### Requirement 1: Cadastro de itens de estoque

**User Story:** Como responsável por estoque, quero cadastrar os itens
controlados (toners, cilindros, peças), para que eu registre entradas e saídas
contra um catálogo consistente.

#### Acceptance Criteria

1. THE Sistema SHALL armazenar por InventoryItem os campos: identificador, nome,
   código/SKU opcional, unidade de medida, rótulo de suprimento vinculado
   opcional (R4), ativo/inativo, observações.
2. WHEN um Usuario autorizado cria um InventoryItem informando dados válidos, THE
   Sistema SHALL persistir o Item vinculado ao tenant_id do contexto autenticado.
3. IF um InventoryItem é criado ou editado sem nome, THEN THE Sistema SHALL
   rejeitar a operação e retornar o código de status HTTP 400.
4. WHEN um Usuario autorizado lista ou consulta Itens, THE Sistema SHALL retornar
   somente os itens do tenant_id do contexto autenticado, com paginação e busca
   por nome/código.
5. WHEN um Usuario autorizado desativa um InventoryItem, THE Sistema SHALL manter
   as movimentações e o saldo já registrados, apenas impedindo novas
   movimentações contra o item desativado.

### Requirement 2: Movimentação de estoque (entradas e saídas)

**User Story:** Como responsável por estoque, quero registrar entradas e saídas
de itens por Local, para que o saldo reflita a realidade física.

#### Acceptance Criteria

1. THE Sistema SHALL armazenar por InventoryMovement os campos: identificador,
   Item, Local, tipo (Entrada, Saída, Ajuste), quantidade, timestamp, ator,
   Impressora relacionada opcional (R4), motivo/observação.
2. THE Sistema SHALL restringir a quantidade de uma movimentação a um valor
   inteiro positivo.
3. WHEN um Usuario autorizado registra uma Entrada ou Saída, THE Sistema SHALL
   validar que o Item e o Local pertencem ao tenant_id do contexto autenticado
   antes de persistir.
4. IF uma Saída é registrada para um (Item, Local) cujo saldo atual é inferior à
   quantidade solicitada, THEN THE Sistema SHALL recusar a movimentação e
   retornar o código de status HTTP 409, sem permitir saldo negativo.
5. WHEN um Usuario autorizado registra uma movimentação do tipo Ajuste
   (correção de saldo, incluindo quando pode reduzir abaixo do que uma Saída
   normal permitiria), THE Sistema SHALL exigir uma justificativa não vazia e
   registrar o evento no AuditLog com o ator, o saldo anterior e o novo saldo.
6. THE Sistema SHALL persistir InventoryMovement de forma somente-adição, sem
   permitir alteração ou exclusão de lançamentos existentes.
7. WHEN uma movimentação é registrada com um InventoryItem desativado, THE
   Sistema SHALL recusar a operação e retornar o código de status HTTP 400.

### Requirement 3: Consulta de saldo por Local

**User Story:** Como usuário operacional, quero consultar o saldo de cada item
por Local, para saber o que está disponível antes de uma troca em campo.

#### Acceptance Criteria

1. WHEN uma InventoryMovement é registrada com sucesso, THE Sistema SHALL
   atualizar, na mesma operação atômica, o saldo materializado do (Item, Local)
   correspondente.
2. WHEN um Usuario autorizado consulta o saldo de um Local, THE Sistema SHALL
   retornar o saldo corrente de cada Item que já teve movimentação naquele
   Local, restrito ao tenant_id do contexto autenticado.
3. WHEN um Usuario autorizado consulta o histórico de movimentações de um
   (Item, Local), THE Sistema SHALL retornar os registros por meio de
   Paginacao_por_Cursor, mais recentes primeiro.
4. THE Sistema SHALL garantir que a soma das quantidades de Entrada menos Saída
   menos/mais o efeito de Ajustes de um (Item, Local) seja sempre igual ao saldo
   materializado consultável (consistência entre o histórico append-only e o
   saldo).

### Requirement 4: Vínculo a suprimentos (Fase 4) e a impressoras

**User Story:** Como usuário operacional, quero relacionar um item de estoque ao
suprimento monitorado por SNMP e à impressora onde foi usado, para ter
rastreabilidade entre o nível reportado pela impressora e a peça física.

#### Acceptance Criteria

1. THE Sistema SHALL permitir configurar, opcionalmente, um rótulo de suprimento
   (`Label`) num InventoryItem, sem validar sua existência prévia em
   `SupplyReading`/`SupplyThreshold` (o vínculo é referencial, por convenção de
   nome, não uma chave estrangeira de banco entre os dois fluxos).
2. THE Sistema SHALL permitir informar, opcionalmente, uma Impressora numa
   movimentação de Saída, validando que a Impressora pertence ao tenant_id do
   contexto autenticado quando informada.
3. WHEN um Usuario autorizado consulta o histórico de movimentações de uma
   Impressora, THE Sistema SHALL retornar as Saídas de estoque que a
   referenciaram, restritas ao tenant_id do contexto autenticado.

### Requirement 5: Estoque mínimo e consulta de itens em nível baixo

**User Story:** Como responsável por estoque, quero configurar um mínimo por
(Item, Local) e consultar quem está abaixo dele, para planejar reposição sem
depender de alerta automático.

#### Acceptance Criteria

1. THE Sistema SHALL permitir configurar um estoque mínimo por (Item, Local); a
   ausência de configuração equivale a nenhum mínimo (nunca considerado baixo).
2. WHEN um Usuario autorizado consulta os itens abaixo do mínimo, THE Sistema
   SHALL retornar os (Item, Local) do tenant_id do contexto autenticado cujo
   saldo materializado é inferior ao mínimo configurado.
3. WHEN uma configuração de mínimo é criada ou alterada, THE Serviço_Auditoria
   SHALL registrar o evento no AuditLog com o ator, o valor anterior e o novo
   valor.

### Requirement 6: Autorização, auditoria e escala

**User Story:** Como responsável de segurança, quero que as operações de estoque
respeitem RBAC, auditoria e as garantias de isolamento das fases anteriores.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover as permissões granulares `estoque.view`
   (consulta de itens/saldo/histórico) e `estoque.manage` (CRUD de itens,
   registro de movimentações, configuração de mínimo).
2. WHEN um Usuario autenticado requisita uma operação de estoque, THE
   Serviço_Autorizacao SHALL autorizar a operação somente se algum Papel do
   Usuario possuir a Permissao correspondente.
3. WHEN uma movimentação de estoque é registrada, THE Serviço_Auditoria SHALL
   registrar o evento no AuditLog com o ator, o tipo, a quantidade e o
   (Item, Local).
4. THE Sistema SHALL expor as operações de estoque por meio de DTOs distintos
   das entidades de persistência.
5. IF uma requisição tenta acessar ou operar sobre InventoryItem,
   InventoryMovement ou configuração de mínimo de um tenant_id distinto do
   contexto autenticado, THEN THE Sistema SHALL recusar a operação e retornar o
   código de status HTTP 404.
6. THE Sistema SHALL definir índices de banco de dados sobre tenant_id e sobre os
   campos de filtro/ordenação/cursor de InventoryMovement e do saldo
   materializado.

## Questões em aberto para validação com o usuário antes do design

1. **Papel `Estoque` (R6.1)**: confirmar o mapeamento de permissões — proposta:
   `Estoque` recebe `estoque.view` + `estoque.manage` (dono natural do módulo);
   `Administrador` recebe ambas também; `Operacional`/`Técnico`/`Supervisor`
   recebem apenas `estoque.view` (para saber o que está disponível em campo).
2. **Ajuste de saldo (R2.5)**: confirmar se o tipo de movimentação `Ajuste` (que
   pode fixar/corrigir o saldo diretamente, com justificativa obrigatória e
   auditoria — mesmo espírito do ajuste administrativo de contador da Fase 2) é
   suficiente, ou se além disso é necessário um "saldo inicial" especial na
   criação do item (nesta proposta, um saldo inicial é apenas a primeira Entrada
   registrada — sem tipo dedicado).
3. **Escopo do vínculo com Impressora (R4.2)**: confirmar se basta registrar a
   Impressora na própria movimentação de Saída (proposta atual, mais simples), ou
   se também é necessário um relatório dedicado "consumo de estoque por
   impressora" nesta fase (a consulta do R4.3 já cobre isso de forma simples,
   listando saídas filtradas por impressora).
