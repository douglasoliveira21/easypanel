# Requirements Document

## Introduction

Este documento especifica a **FASE 10 (Phase 10)** da plataforma **EasyPanel**
— tema **"Portal do Cliente"**. É a **última fase do roteiro atual**
(`HANDOFF.md`, seção 6): *"Área self-service do tenant-cliente (papel
Cliente), visão de parque/contadores/chamados/faturas; escopo de permissões
restrito."* Diferente de todas as fases anteriores — que sempre restringiam
dados ao `tenant_id` (a empresa prestadora de serviço) — esta fase introduz
um **segundo nível de isolamento**, dentro do mesmo tenant: um usuário do
papel `Cliente` deve enxergar apenas os dados do **Cliente (Customer)**
específico ao qual está vinculado, nunca os de outros Clientes do mesmo
tenant.

### Premissas verificadas no código antes de escrever este documento

- **Não existe hoje nenhum vínculo entre `ApplicationUser` e `Customer`.**
  `ApplicationUser` (Identity) tem apenas `TenantId` (nulo só para Super
  Admin), `IsActive`, `MfaEnabled`, `CreatedAt`, além dos campos herdados de
  `IdentityUser<Guid>` (`Email`, `UserName`, etc.). Não há `CustomerId` nem
  tabela de associação usuário↔Cliente em nenhum módulo (`Identity`,
  `Customers`, `Contracts`). **Este vínculo precisa ser criado do zero
  nesta fase.**
- **O papel `Cliente` já existe, mas vazio.** `Roles.cs` já declara
  `Cliente` com o comentário *"Perfil do portal do cliente (fase futura);
  sem permissões administrativas na Fase 1"*; `RolePermissions.Map[Roles.
  Cliente] = Array.Empty<string>()` — confirmado inalterado até a Fase 9.
- **O isolamento por tenant é feito via `ITenantContext`** (`TenantId`,
  `IsSuperAdmin`, `HasTenant`), populado por middleware a partir da claim
  `tenant_id` do JWT, e aplicado como filtro global de consulta no
  `AppDbContext`. Um mecanismo análogo — `ICustomerContext`/claim
  `customer_id` — é o caminho natural para o segundo nível de isolamento
  desta fase, sem alterar o padrão já usado em 9 fases.
- **`TokenService` hoje emite apenas `sub`, `jti`, `perm_version`,
  `tenant_id` (se houver) e `roles` (se não vazio).** Nenhuma claim de
  Cliente existe — precisa ser adicionada condicionalmente (só para
  usuários com papel `Cliente`), no mesmo padrão de `tenant_id`.
- **Nada no modelo atual (`Customer`, `Contract`, `ContractFranchise`)
  sugere um login vinculado a múltiplos Clientes.** Cada Contrato pertence
  a exatamente um Cliente; não há tabela de associação N:N em nenhum lugar.
  Proposta desta fase: **um usuário do portal vincula-se a exatamente um
  Customer** (não a vários) — ver "Questões em aberto" para confirmar.
- **Dados já expostos por Customer/Local, reaproveitáveis para o portal:**
  Impressoras (`Printer` → `Location.CustomerId`), leituras de contador
  (`PrinterCounter`), Chamados (`Ticket.CustomerId`), Faturas
  (`Invoice` → via `Contract.CustomerId`). Nenhuma entidade nova de domínio
  é necessária para exibir esses dados — apenas um filtro adicional por
  `CustomerId` sobre consultas já existentes (ou uma camada de leitura
  dedicada no novo módulo).
- **Nenhuma rota de auto-cadastro/convite de usuário-Cliente existe.** Hoje
  todo usuário é criado via `POST /api/v1/users` por um Administrador do
  tenant — o mesmo endpoint pode ser reaproveitado para criar usuários
  `Cliente` vinculados a um `CustomerId`, sem exigir um fluxo de convite
  separado nesta fase (ver "Fora de escopo").

### Fora de escopo (adiado para fases futuras)

- **Autoatendimento de abertura de senha/recuperação de conta dedicado ao
  portal** — reaproveita o fluxo de autenticação já existente
  (`/api/v1/auth/login`, reset de senha) sem tela ou fluxo separado.
- **Convite por e-mail para o usuário-Cliente se cadastrar** — a conta é
  criada diretamente por um Administrador do tenant (reaproveitando
  `POST /api/v1/users` com `CustomerId`), sem fluxo de convite/ativação.
- **Abertura de chamado pelo próprio Cliente pelo portal** — nesta fase o
  portal é **somente leitura** (visão de parque/contadores/chamados/
  faturas); permitir que o Cliente abra ou interaja em chamados é uma
  extensão futura.
- **Pagamento de fatura online pelo portal** (gateway de pagamento,
  boleto, PIX) — o portal nesta fase apenas exibe faturas e seu status, sem
  nenhuma ação de cobrança.
- **Um usuário vinculado a múltiplos Clientes** (ex.: portal para
  distribuidor com várias empresas-cliente) — fora de escopo; ver
  "Questões em aberto".
- **Customização de marca (white-label) do portal por tenant** — fora de
  escopo.
- **Exportação/relatórios dentro do portal do Cliente** (reuso da Fase 9)
  — fora de escopo; o portal expõe apenas visões consolidadas fixas.

### Princípios não funcionais herdados das Fases 1–9 (válidos na Fase 10)

- **Isolamento multi-tenant inegociável**, agora com um segundo nível:
  isolamento por `CustomerId` **dentro** do tenant, com a mesma rigidez
  (falha de contexto → 404, nunca vazamento de existência).
- **Backend é a autoridade**: toda restrição de escopo (tenant + Cliente)
  é aplicada no backend, nunca confiada ao cliente HTTP.
- **Portabilidade SQLite/PostgreSQL**: qualquer novo filtro/consulta usa
  colunas `*Ticks` para ordenação/comparação de data, nunca
  `DateTimeOffset` direto.
- **Reuso de padrões**: Result/Error, RBAC (`[RequirePermission]`),
  auditoria (`IAuditLogger`) para as mutações desta fase (vínculo de
  usuário a Cliente).

## Glossary

- **Usuário-Cliente**: um `ApplicationUser` com papel `Cliente`, vinculado
  a exatamente um `Customer` do tenant.
- **Portal do Cliente**: conjunto de endpoints somente-leitura, acessíveis
  apenas a Usuários-Cliente, restritos aos dados do `Customer` ao qual o
  usuário está vinculado.
- **Escopo de Cliente**: o segundo nível de isolamento desta fase —
  análogo ao `ITenantContext`, mas restringindo por `CustomerId` dentro do
  tenant já resolvido.

## Requirements

### Requirement 1: Vínculo entre usuário e Cliente

**User Story:** Como Administrador do tenant, quero vincular um usuário do
papel Cliente a um Cliente (Customer) específico, para que ele só possa
acessar os dados desse Cliente pelo portal.

#### Acceptance Criteria

1. WHEN um Administrador cria ou atualiza um usuário com o papel `Cliente`,
   THE Sistema SHALL exigir um `CustomerId` pertencente ao mesmo tenant, e
   SHALL rejeitar a operação com HTTP 400 se `CustomerId` não for
   informado ou não existir no tenant.
2. IF um usuário tem o papel `Cliente`, THEN THE Sistema SHALL impedir que
   ele também tenha qualquer outro papel administrativo/operacional do
   tenant (papéis são mutuamente exclusivos com `Cliente`), retornando
   HTTP 400 caso contrário.
3. WHILE um usuário não tem o papel `Cliente`, THE Sistema SHALL manter
   `CustomerId` nulo/ignorado para esse usuário.
4. THE Sistema SHALL registrar em auditoria (`AuditLog`) a criação e a
   alteração do vínculo usuário↔Cliente.

### Requirement 2: Autenticação e escopo de Cliente

**User Story:** Como Usuário-Cliente, quero fazer login normalmente e ter
minhas consultas automaticamente restritas ao meu Cliente, para não precisar
selecionar nem correr risco de ver dados de outra empresa.

#### Acceptance Criteria

1. WHEN um Usuário-Cliente autentica com sucesso, THE Sistema SHALL incluir
   no token de acesso uma claim identificando o `CustomerId` vinculado,
   além das claims já existentes (`tenant_id`, `roles`, etc.).
2. WHEN uma requisição autenticada chega a um endpoint do portal, THE
   Sistema SHALL resolver o Escopo de Cliente a partir dessa claim antes de
   qualquer consulta, no mesmo padrão de resolução do `ITenantContext`.
3. IF a claim de `CustomerId` está ausente, inválida, ou aponta para um
   Cliente de outro tenant, THEN THE Sistema SHALL negar o acesso a
   qualquer endpoint do portal com HTTP 401/403.
4. THE Sistema SHALL impedir que um Usuário-Cliente acesse qualquer
   endpoint fora do portal (os já existentes das Fases 1–9), mesmo que
   tecnicamente autenticado, por não possuir nenhuma permissão administrativa/
   operacional (R1.2).

### Requirement 3: Visão de parque e contadores

**User Story:** Como Usuário-Cliente, quero ver as impressoras instaladas
nos meus locais e o histórico recente de contadores, para acompanhar meu
parque sem precisar abrir chamado.

#### Acceptance Criteria

1. WHEN um Usuário-Cliente consulta seu parque de impressoras, THE Sistema
   SHALL retornar apenas Impressoras cujo Local pertence ao `CustomerId` do
   Escopo de Cliente, com paginação por cursor (padrão das fases
   anteriores).
2. WHEN um Usuário-Cliente consulta o histórico de contadores de uma
   Impressora, THE Sistema SHALL retornar o histórico apenas se a
   Impressora pertencer ao seu Cliente; caso contrário, THE Sistema SHALL
   responder HTTP 404 (nunca 403 — sem revelar a existência do recurso a
   outro Cliente).

### Requirement 4: Visão de chamados

**User Story:** Como Usuário-Cliente, quero ver o status e o histórico dos
meus chamados abertos, para acompanhar o atendimento sem precisar ligar para
o suporte.

#### Acceptance Criteria

1. WHEN um Usuário-Cliente consulta seus Chamados, THE Sistema SHALL
   retornar apenas Chamados cujo `CustomerId` corresponde ao Escopo de
   Cliente, com paginação por cursor.
2. WHEN um Usuário-Cliente consulta o detalhe de um Chamado (incluindo
   interações e anexos), THE Sistema SHALL retornar o conteúdo apenas se o
   Chamado pertencer ao seu Cliente; caso contrário, HTTP 404.
3. THE Sistema SHALL expor os Chamados do portal como somente-leitura
   nesta fase (sem criar, comentar ou anexar pelo portal — ver "Fora de
   escopo").

### Requirement 5: Visão de faturas

**User Story:** Como Usuário-Cliente, quero ver minhas faturas emitidas e
seus valores, para acompanhar minha situação financeira com o prestador de
serviço.

#### Acceptance Criteria

1. WHEN um Usuário-Cliente consulta suas Faturas, THE Sistema SHALL
   retornar apenas Faturas de Contratos cujo `CustomerId` corresponde ao
   Escopo de Cliente, com paginação por cursor.
2. THE Sistema SHALL excluir Faturas em `Rascunho` da visão do portal
   (apenas `Emitida`/`Cancelada` são visíveis — rascunho é estado interno
   do fechamento, não deve ser exposto ao Cliente antes da emissão).
3. WHEN um Usuário-Cliente consulta o detalhe de uma Fatura (linhas de
   consumo/excedente), THE Sistema SHALL retornar o conteúdo apenas se a
   Fatura pertencer ao seu Cliente; caso contrário, HTTP 404.

### Requirement 6: Autorização e isolamento do portal

**User Story:** Como responsável de segurança, quero que o portal do
Cliente tenha isolamento equivalente ao isolamento multi-tenant já
garantido nas fases anteriores, agora também entre Clientes do mesmo
tenant.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover permissões granulares dedicadas ao
   portal (ex.: `portal.parque.view`, `portal.chamado.view`,
   `portal.fatura.view`), mapeadas exclusivamente ao papel `Cliente`.
2. WHEN um Usuário-Cliente requisita qualquer endpoint do portal, THE
   Serviço_Autorizacao SHALL autorizar somente se o papel `Cliente`
   possuir a permissão correspondente E o Escopo de Cliente (R2) estiver
   resolvido.
3. THE Sistema SHALL garantir, por teste de segurança dedicado, que um
   Usuário-Cliente de um Cliente nunca acessa dados de outro Cliente do
   mesmo tenant (isolamento cruzado de Cliente), nem de outro tenant
   (isolamento cruzado de tenant, herdado).
4. THE Sistema SHALL expor os dados do portal por meio de DTOs distintos
   das entidades internas, sem retornar nenhum campo administrativo/
   interno (ex.: custo de excedente por franquia, dados de outros
   Clientes) que não seja pertinente à visão do próprio Cliente.

## Questões em aberto para validação com o usuário antes do design

1. **Um Cliente para muitos usuários, mas um usuário para um único Cliente
   (R1.1, R1.3)**: confirmar essa cardinalidade (1 `Customer` pode ter
   vários logins — ex.: recepção e financeiro do Cliente — mas nenhum
   login abrange mais de um `Customer`). Multi-Cliente por usuário
   (distribuidor) fica fora de escopo desta fase.
2. **Exclusividade do papel `Cliente` (R1.2)**: confirmar que um usuário
   `Cliente` nunca acumula papéis administrativos/operacionais do tenant
   (ex.: não pode ser `Cliente` + `Operacional` ao mesmo tempo) — simplifica
   a resolução de permissões e evita ambiguidade de escopo.
3. **Granularidade de permissões (R6.1)**: proposta de 3 permissões
   (`portal.parque.view`, `portal.chamado.view`, `portal.fatura.view`) em
   vez de uma única `portal.view` — permite no futuro um Cliente com acesso
   parcial (ex.: só financeiro, sem ver chamados), mas todas mapeadas ao
   papel `Cliente` por padrão nesta fase. Confirmar ou preferir uma
   permissão única mais simples.
4. **Faturas em rascunho (R5.2)**: confirmar a exclusão de faturas em
   `Rascunho` da visão do Cliente (evita expor valores ainda não
   confirmados/emitidos).
5. **Criação do usuário-Cliente (Premissas)**: confirmar reaproveitamento
   de `POST /api/v1/users` (com `CustomerId` adicional quando `Roles`
   contém `Cliente`) em vez de um endpoint dedicado `/api/v1/portal/users`
   — mais simples, consistente com o padrão de criação de usuário já
   existente.
6. **Auditoria de leitura do portal (R6)**: confirmar que consultas do
   portal (R3/R4/R5) **não** são auditadas (mesmo padrão de toda listagem
   `GET` já existente na plataforma, incluindo a Fase 9) — só o vínculo
   usuário↔Cliente (R1.4) é auditado, por ser mutação administrativa.
