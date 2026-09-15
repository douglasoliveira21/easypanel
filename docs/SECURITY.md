# Segurança — EasyPanel (Fases 1–4)

## Autenticação

- **ASP.NET Identity** com hashing de senha PBKDF2 (nunca em texto claro).
- **Política de senha:** mínimo 8 caracteres, com maiúscula, minúscula, dígito e
  caractere especial.
- **JWT de acesso:** assinado (HMAC-SHA256), expiração ≤ 15 minutos; expirado → 401.
- **Refresh token:** opaco (256 bits), armazenado apenas como hash SHA-256,
  **rotacionado** a cada uso; expirado/revogado/desconhecido → 401.
- **Revogação:** logout revoga o token da sessão; troca/reset de senha e
  desativação de usuário revogam todos os refresh tokens do usuário.
- **MFA-ready:** o fluxo de segundo fator existe estruturalmente (ticket de curta
  duração + endpoint de verificação). O validador padrão **falha fechado** — um
  usuário com MFA habilitado não obtém tokens até um provedor TOTP real ser
  registrado (fase futura). Usuários sem MFA seguem o fluxo inalterado.

## Proteção contra força bruta e abuso

- **Lockout:** 5 falhas consecutivas bloqueiam a conta por 15 minutos; sucesso
  zera o contador. Toda tentativa de login é auditada (resultado, horário, IP).
- **Rate limiting:** limiter particionado por usuário/tenant/IP e por endpoint;
  ao exceder, HTTP 429 com `Retry-After`. Nunca depende apenas do IP.
- **Não-vazamento:** credenciais inválidas (email desconhecido, senha errada,
  conta inativa) retornam o mesmo erro genérico; recuperação de senha responde
  igual para email existente e inexistente.

## Autorização (RBAC)

- Papéis da Fase 1: Super Admin, Administrador, Financeiro, Operacional, Estoque,
  Técnico, Supervisor, Cliente.
- Permissões granulares `recurso.acao` (ex.: `customer.view`, `user.manage`).
- Avaliação **sempre no backend** via políticas dinâmicas `perm:<permission>`;
  ausência de permissão → 403. O frontend nunca é autoridade.

## Isolamento multi-tenant

- `tenant_id` derivado exclusivamente do token autenticado (ou header admin, só
  para Super Admin em endpoints designados).
- Query filter global + interceptor de escrita no EF Core garantem que nenhum
  tenant acesse dados de outro. Acesso cross-tenant → 404 (não revela existência).
- Suíte de testes de segurança (`EasyPanel.SecurityTests`) prova o isolamento em
  Clientes, Locais e Usuários, e a unicidade por tenant (CNPJ/email). A Fase 3
  estende a suíte para `AlertRule`/`Alert`/`AlertSilence` (leitura, edição,
  reconhecimento/resolução e encerramento cross-tenant → 404, sem vazamento em
  listagens).
- **Segundo nível de isolamento (Fase 10 — Portal do Cliente):** dentro do
  tenant já resolvido, `customer_id` (claim do token, presente apenas para
  o papel `Cliente`) restringe ainda mais as consultas do portal ao
  Cliente vinculado ao usuário (`ICustomerContext`, mesmo padrão scoped de
  `ITenantContext`, resolvido pelo mesmo `TenantResolutionMiddleware`).
  Recurso de outro Cliente do mesmo tenant → 404, mesma regra de
  não-vazamento do isolamento por tenant.

## Auditoria

- `AuditLog` append-only registra login (sucesso/falha), mudanças de usuários e
  tentativas de acesso cross-tenant recusadas, com ator, tenant, IP e horário.
- Valores antigo/novo são JSON **redigido** — senhas, hashes, stamps e tokens são
  mascarados antes da serialização.

## Logs e segredos

- Logs estruturados em JSON (Serilog), enriquecidos com `CorrelationId` e
  `tenant_id`.
- Política de redaction remove de qualquer log estruturado propriedades cujo nome
  sugira segredo (password/senha/token/secret/hash/securityStamp/apikey/
  signingKey/connectionString).
- Segredos (connection strings, chave JWT, credenciais) vêm de variáveis de
  ambiente / cofre — **nunca versionados**.

## Recomendações de implantação

- Fornecer uma `Jwt:SigningKey` forte (≥ 32 bytes) por segredo.
- Terminar TLS no proxy reverso (Caddy) e não expor a API diretamente.
- Restringir o acesso ao Swagger em produção (via proxy/autorização).
- Para múltiplas instâncias, considerar mover rate limiting e cache de permissões
  para o Redis (pontos de extensão documentados).

## Agente Windows e monitoramento (Fase 2)

- **Autenticação própria do agente:** esquema `ClientBearer` com audience dedicado
  (`easypanel:client`). Um JWT de usuário não autentica endpoints de agente e
  vice-versa (defesa em profundidade via claim `token_kind`). Token de acesso ≤ 15
  min; refresh opaco rotativo hasheado (SHA-256), como o dos usuários.
- **Segredo do agente:** gerado no registro (256 bits), persistido apenas como hash
  e verificado em tempo constante. O valor bruto é entregue uma única vez.
- **Registro por provisionamento:** validado por chave de Local hasheada, com
  expiração/revogação. Registro é auditado (sucesso e falha).
- **Origem de tenant/local:** sempre a identidade do agente (nunca o corpo/query da
  requisição). Acesso cross-tenant → 404; o mesmo isolamento do backend se aplica.
- **Idempotência de ingestão:** `IdempotencyKey` única por tenant evita duplicação,
  inclusive sob concorrência (índice único + backstop `IngestionDedup`).
- **Integridade de contadores:** leituras automáticas são não-decrescentes; ajustes
  administrativos que reduzem valor exigem justificativa e são auditados (ator,
  valor anterior/novo).
- **Comunicação do agente:** HTTPS de saída com validação de certificado TLS
  (falha aborta e loga). Fila local offline em SQLite reenvia com backoff ao
  reconectar; **nenhum** segredo (tokens, secret, community SNMP) é gravado em fila
  ou log — a redação por padrões cobre pares chave/valor e o esquema Bearer.
- **Auto-update seguro:** pacote só é aplicado após verificação de **hash SHA-256**
  e **assinatura RSA** com a chave pública embutida; pacote adulterado/assinatura
  inválida é recusado mantendo a versão. Falha de inicialização dispara rollback.

## Alertas e Notificações (Fase 3)

- **Novas permissões:** `alert.view` (leitura), `alert.manage` (CRUD de
  regra/silenciamento), `alert.acknowledge` (reconhecer/resolver Alerta) — mesmo
  modelo RBAC granular `recurso.acao`, avaliado sempre no backend.
- **Segredo de webhook:** `AlertRule.WebhookSecret` nunca é incluído em DTO de
  leitura, resposta de API, log de aplicação ou `AuditLog` (redigido pelo mesmo
  `AuditValueRedactor` da Fase 1). Usado apenas para assinar (HMAC-SHA256) o corpo
  do webhook de saída no momento do despacho.
- **Webhook HTTPS obrigatório:** a URL de um webhook de saída deve usar HTTPS —
  validado na criação/edição da regra (HTTP simples → 400).
- **Isolamento multi-tenant:** `AlertRule`, `Alert`, `AlertTransition`,
  `AlertNotificationOutbox`, `AlertNotificationAttempt`, `AlertSilence` e
  `AlertEngineCheckpoint` seguem o mesmo modelo de isolamento — as seis primeiras
  são `TenantEntity` (filtro global + interceptor de escrita); o checkpoint é um
  cursor de plataforma cross-tenant (mesmo padrão de `AuditLog`), mas o
  `AlertEngine` nunca combina dados de tenants distintos numa mesma avaliação:
  cada `PrinterEvent` só é avaliado contra as `AlertRule` do seu próprio tenant.
- **Auditoria:** criação/edição de regra, reconhecimento/resolução de Alerta e
  criação/encerramento de silenciamento são eventos sensíveis registrados via
  `IAuditLogger` (ator, tenant, antes/depois quando aplicável).
- **E-mail sem credenciais em log:** o corpo completo de e-mails e as credenciais
  SMTP nunca são registrados; o notificador log-only (usado quando nenhum SMTP
  está configurado) grava apenas o fato do envio, no mesmo padrão do
  `IPasswordResetNotifier` da Fase 1.

## Suprimentos e cliente HTTP real do agente (Fase 4)

- **Novas permissões:** `supply.view` (níveis/histórico/previsão), `supply.manage`
  (configuração de limiares) — mesmo modelo RBAC granular, avaliado no backend.
- **Isolamento multi-tenant:** `SupplyReading`/`SupplyThreshold` são
  `TenantEntity` (filtro global + interceptor de escrita), com testes de
  isolamento cross-tenant via HTTP real (`SuppliesIsolationTests`).
- **Auditoria:** criação/atualização/remoção de limiar de suprimento é evento
  sensível registrado via `IAuditLogger` (ator, valor anterior/novo).
- **Cliente HTTP do agente:** `HttpBackendClient` nunca loga o `ClientSecret`
  nem os tokens obtidos; a autenticação/renovação usa `SemaphoreSlim` para evitar
  corrida entre os dois serviços internos que o compartilham.
- **SNMP real:** dependência nova (`Lextm.SharpSnmpLib`, versão fixada) verificada
  sem vulnerabilidade conhecida na publicação desta fase; nenhuma credencial SNMP
  (community string) é logada.

## Estoque (Fase 5)

- **Novas permissões:** `estoque.view` (consulta de itens/movimentações/saldo/
  mínimo), `estoque.manage` (criação/edição de itens, registro de movimentação,
  configuração de mínimo) — mesmo modelo RBAC granular, avaliado no backend.
- **Isolamento multi-tenant:** `InventoryItem`/`InventoryMovement`/
  `InventoryBalance`/`InventoryMinimum` são `TenantEntity` (filtro global +
  interceptor de escrita), com testes de isolamento cross-tenant via HTTP real
  (`InventoryIsolationTests`) e negação RBAC de `estoque.manage` a papel sem a
  permissão.
- **Auditoria:** criação/atualização de item, toda movimentação registrada e
  criação/atualização de mínimo são eventos sensíveis registrados via
  `IAuditLogger` (ator, tenant, valor anterior/novo quando aplicável).
- **Saldo nunca negativo:** o piso em zero para Ajuste-Decrease é aplicado no
  serviço de domínio, não confiável a partir de entrada do cliente — a
  quantidade recebida na requisição nunca é usada diretamente como novo saldo.

## Chamados/Helpdesk e SLA (Fase 6)

- **Novas permissões:** `chamado.view` (consulta de chamados/histórico/
  anexos), `chamado.manage` (abertura, status, atribuição, comentário,
  anexo), `sla.manage` (configuração de política de SLA) — mesmo modelo
  RBAC granular, avaliado no backend.
- **Isolamento multi-tenant:** `Ticket`/`TicketInteraction`/`SlaPolicy`/
  `TicketAttachment` são `TenantEntity` (filtro global + interceptor de
  escrita), com testes de isolamento cross-tenant via HTTP real
  (`TicketingIsolationTests`) e negação RBAC de `chamado.manage`/
  `sla.manage` a papel sem a permissão.
- **Auditoria:** abertura, mudança de status, atribuição, comentário, upload
  de anexo e upsert de política de SLA são eventos sensíveis registrados via
  `IAuditLogger` (ator, tenant, dados relevantes da operação).
- **Isolamento de anexo no próprio caminho do objeto:** a chave de um anexo
  no MinIO inclui o `tenantId` (`tickets/{tenantId}/{ticketId}/...`) — o
  isolamento por tenant não depende só dos metadados em banco; uma leitura
  direta do bucket (fora da API) também respeita o particionamento.
- **Validação de anexo antes do storage:** tamanho e tipo MIME são validados
  **antes** de qualquer chamada ao `IFileStorage` — um arquivo rejeitado
  nunca chega a ser enviado ao MinIO nem gera metadado órfão.
- **Credenciais do MinIO nunca logadas:** `MinioFileStorage` não registra
  `AccessKey`/`SecretKey` em log, mesmo em caso de falha (mesmo padrão do
  `HttpBackendClient` da Fase 4 para credenciais do agente).

## Contratos (Fase 7)

- **Novas permissões:** `contrato.view` (consulta de contratos/escopo/
  franquias), `contrato.manage` (criação/edição, escopo, franquia,
  transição de status) — mesmo modelo RBAC granular, avaliado no backend.
  `Financeiro` é o dono natural do módulo (ambas); `Administrador` também;
  `Operacional`/`Supervisor` só `contrato.view`; `Tecnico`/`Estoque` sem
  nenhuma.
- **Isolamento multi-tenant:** `Contract`/`ContractLocation`/
  `ContractPrinter`/`ContractFranchise` são `TenantEntity` (filtro global +
  interceptor de escrita), com testes de isolamento cross-tenant via HTTP
  real (`ContractIsolationTests`) e negação RBAC de `contrato.manage` a
  papel sem a permissão.
- **Auditoria:** criação/atualização/transição de status de contrato,
  adição/remoção de escopo e upsert de franquia são eventos sensíveis
  registrados via `IAuditLogger` (ator, tenant, dados relevantes da
  operação).
- **Resolução de contrato não vaza existência entre tenants:** consultar o
  contrato aplicável a uma impressora de outro tenant retorna 204 (sem
  contrato aplicável), nunca 404/403 que revelaria se a impressora existe
  em outro tenant — a impressora simplesmente não é encontrada pelo filtro
  global, e a ausência de resultado é tratada como caso normal (R4.2).
- **Sem novo campo sensível:** o primeiro campo monetário da plataforma
  (`ContractFranchise.ExcessUnitPrice`) não é PII nem segredo — segue as
  mesmas regras de auditoria/isolamento de qualquer outro campo de domínio,
  sem tratamento especial de mascaramento.

## Fechamento e Faturamento (Fase 8)

- **Novas permissões:** `fechamento.view` (consulta de faturas/itens/
  histórico de fechamento), `fechamento.manage` (executar fechamento,
  emitir/cancelar fatura) — mesmo modelo RBAC granular, avaliado no
  backend. Mais restritivo que Contratos: só `Financeiro`/`Administrador`
  têm acesso; `Operacional`/`Supervisor` não têm nenhuma das duas
  (fechamento é estritamente financeiro).
- **Isolamento multi-tenant:** `BillingClosing`/`Invoice`/`InvoiceLineItem`
  são `TenantEntity` (filtro global + interceptor de escrita), com testes
  de isolamento cross-tenant via HTTP real (`BillingIsolationTests`) e
  negação RBAC de `fechamento.manage`/`fechamento.view` a papel sem a
  permissão.
- **Auditoria:** execução de fechamento e transição de status de fatura são
  eventos sensíveis registrados via `IAuditLogger` (ator, tenant, período,
  quantidade de faturas geradas, de/para de status).
- **Fechamento não pode ser refeito silenciosamente:** a constraint única
  `(TenantId, Year, Month)` em `BillingClosing` impede refechamento mesmo
  sob concorrência — não é apenas uma checagem em serviço.
- **Fatura imutável após sair de Rascunho:** não existe endpoint de edição
  de Itens de Fatura; a única escrita permitida numa Fatura não-Rascunho é
  a transição de status (auditada).

## Relatórios e Dashboards (Fase 9)

- **Nova permissão:** `relatorio.view` (consulta de painel, relatórios e
  exportação) — sem `.manage`, já que não há nada para configurar/mutar
  neste módulo. `Administrador`, `Financeiro`, `Supervisor` recebem;
  demais papéis (`Operacional`/`Tecnico`/`Estoque`/`Cliente`) não têm
  acesso a relatórios.
- **Isolamento multi-tenant:** toda consulta é restrita ao tenant_id do
  contexto autenticado (garantido pelo filtro global das entidades-fonte,
  todas `TenantEntity`), com testes de isolamento via HTTP real
  (`ReportingIsolationTests`) e negação RBAC de `relatorio.view` a papel
  sem a permissão.
- **Sem auditoria:** consultas de painel/relatório e exportação **não** são
  registradas no `AuditLog` — decisão explícita, mesmo padrão de qualquer
  listagem `GET` já existente na plataforma (nenhuma é auditada).
- **Sem cache:** nenhum dado de relatório é armazenado em cache
  (Redis ou outro) — cada consulta calcula ao vivo, evitando o risco de
  servir dados de outro tenant por chave de cache mal escopada, que um
  cache mal implementado poderia introduzir.
- **Limite de intervalo de datas:** consultas de relatório recusam
  intervalos maiores que 366 dias (400), evitando varreduras arbitrariamente
  grandes.

## Portal do Cliente (Fase 10)

- **Novas permissões:** `portal.parque.view`, `portal.chamado.view`,
  `portal.fatura.view` — mapeadas **exclusivamente** ao papel `Cliente`;
  nenhum outro papel as recebe, e `Cliente` não recebe nenhuma outra
  permissão da plataforma (exclusividade de papel — R1.2).
- **Vínculo usuário↔Cliente validado no backend:** `UserService` exige
  `CustomerId` do mesmo tenant ao atribuir o papel `Cliente`, e recusa
  (400) combiná-lo com qualquer outro papel. `CustomerId` de outro tenant
  é tratado como inexistente (mesmo padrão de não-vazamento).
- **Segundo nível de isolamento** (ver seção "Isolamento multi-tenant"
  acima): `customer_id` do token, resolvido em `ICustomerContext`, filtra
  toda consulta do portal além do tenant. Testado via HTTP real
  (`PortalIsolationTests`): um Usuário-Cliente nunca acessa dados de outro
  Cliente do mesmo tenant nem de outro tenant; RBAC nega `portal.*.view` a
  papéis administrativos/operacionais e nega permissões de negócio (ex.
  `chamado.view`, `customer.view`) a um Usuário-Cliente.
- **Faturas em Rascunho nunca expostas:** filtradas tanto na listagem
  quanto no detalhe por id — um Usuário-Cliente não descobre a existência
  de uma Fatura ainda não emitida, mesmo do próprio Cliente.
- **Sem auditoria:** consultas do portal **não** são registradas no
  `AuditLog` (mesmo padrão de toda listagem `GET` já existente); apenas o
  vínculo usuário↔Cliente é auditado, como parte de `UserCreate`/
  `UserUpdate` (já existentes).

## Conformidade

A validação completa de conformidade (ex.: acessibilidade WCAG, testes de
penetração) exige avaliação manual e ferramentas especializadas, fora do escopo
automatizado desta fase.
