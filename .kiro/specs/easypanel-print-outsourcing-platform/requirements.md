# Requirements Document

## Introduction

O **EasyPanel** é uma plataforma SaaS comercial para gestão de outsourcing de impressão e gestão de ativos de TI. A visão completa do produto contempla, ao longo de múltiplas fases, o cadastro e monitoramento de impressoras (via SNMP), coleta de contadores, gestão de suprimentos e estoque, alertas, chamados (tickets), gestão de técnicos e SLA, contratos, precificação, fechamento mensal, faturamento, gestão de dispositivos de TI, relatórios, dashboards, webhooks, um agente cliente para Windows (Windows Client) e um portal do cliente.

Este documento de requisitos está **escopado exclusivamente à FASE 1 (Phase 1)**, que estabelece a fundação da plataforma. A Fase 1 entrega a infraestrutura base, o sistema de autenticação, o controle de acesso baseado em papéis (RBAC), o isolamento multi-tenant, a gestão de usuários e o cadastro (CRUD) de Clientes e Locais (pontos de instalação). As demais capacidades da visão de produto serão detalhadas em specs futuras e estão **fora do escopo** deste documento.

O contexto tecnológico da plataforma inclui: frontend em React + TypeScript + Vite + Tailwind + shadcn/ui; backend em C# / .NET 10 / ASP.NET Core Web API com Entity Framework Core; banco PostgreSQL; Redis e Hangfire para cache e processamento; SignalR para tempo real; armazenamento compatível com S3 (MinIO); autenticação com ASP.NET Identity, JWT, Refresh Token, RBAC e preparação para MFA. A arquitetura é um monólito modular com separação clara de módulos para futura extração em microsserviços. Os requisitos permanecem, sempre que possível, agnósticos de tecnologia, registrando restrições técnicas apenas quando são premissas de negócio ou segurança.

Princípios não funcionais que valem desde a Fase 1: o isolamento de dados multi-tenant é um requisito de segurança inegociável (nunca confiar no frontend); o backend é a autoridade sobre as regras de negócio; toda entrada deve ser validada; logs estruturados em JSON não devem registrar senhas, tokens, segredos ou credenciais; ações sensíveis devem ser auditadas; deve haver rate limiting; e a plataforma deve ser projetada para grande escala (milhares de tenants), com paginação e indexação desde o início.

## Glossary

- **EasyPanel**: A plataforma SaaS completa objeto deste produto.
- **Sistema**: O backend do EasyPanel (ASP.NET Core Web API) responsável por aplicar regras de negócio e segurança. Termo genérico usado quando nenhum subsistema específico é mais preciso.
- **Serviço_Autenticacao**: Subsistema responsável por login, logout, emissão e renovação de tokens, recuperação e alteração de senha, e controle de sessões.
- **Serviço_Autorizacao**: Subsistema responsável por avaliar papéis (roles) e permissões (RBAC) para autorizar operações.
- **Serviço_Tenant**: Subsistema responsável por resolver, aplicar e validar o isolamento de dados por tenant.
- **Serviço_Usuario**: Subsistema responsável pela gestão (CRUD, ativação/desativação) de usuários.
- **Serviço_Cliente**: Subsistema responsável pelo cadastro (CRUD) de Clientes.
- **Serviço_Local**: Subsistema responsável pelo cadastro (CRUD) de Locais (pontos de instalação).
- **Serviço_Auditoria**: Subsistema responsável por registrar eventos de auditoria (AuditLog).
- **Serviço_RateLimit**: Subsistema responsável por limitar a taxa de requisições.
- **Tenant**: Entidade organizacional que representa uma instância isolada de dados de uma empresa contratante do EasyPanel. Todo dado de negócio pertence a exatamente um Tenant.
- **tenant_id**: Identificador do Tenant, presente de forma obrigatória em todas as entidades de negócio.
- **Usuario**: Pessoa autenticável no Sistema, pertencente a um Tenant, com um ou mais papéis atribuídos.
- **Papel (Role)**: Conjunto nomeado de permissões atribuível a um Usuario. Os papéis da Fase 1 são: Super Admin, Administrador, Financeiro, Operacional, Estoque, Técnico, Supervisor e Cliente.
- **Super_Admin**: Papel com acesso global à plataforma, capaz de operar entre tenants para administração da plataforma.
- **Permissao**: Autorização granular para executar uma operação específica sobre um recurso.
- **RBAC**: Controle de Acesso Baseado em Papéis (Role-Based Access Control).
- **Sessao**: Vínculo autenticado ativo entre um Usuario e o Sistema, representado por tokens.
- **JWT**: Token de acesso assinado (JSON Web Token) de curta duração.
- **Refresh_Token**: Token de longa duração usado para obter novos JWT sem novo login.
- **MFA**: Autenticação de múltiplos fatores (Multi-Factor Authentication).
- **AuditLog**: Registro persistente de eventos sensíveis (quem, o quê, quando, contexto).
- **Cliente**: Empresa atendida por um Tenant, alvo dos serviços de outsourcing. É uma entidade de negócio (não confundir com o papel "Cliente" do RBAC).
- **Local**: Ponto de instalação físico pertencente a um Cliente (ex.: filial, unidade, andar).
- **DTO**: Data Transfer Object; contrato de dados de entrada/saída da API, distinto das entidades de persistência.
- **CNPJ**: Cadastro Nacional da Pessoa Jurídica (identificador fiscal brasileiro de empresas).
- **Pagina_de_Resultados**: Subconjunto ordenado e limitado de registros retornado por consultas de listagem.

## Requirements

### Requirement 1: Infraestrutura base da plataforma

**User Story:** Como operador de infraestrutura, quero uma stack de serviços containerizada e reproduzível, para que a plataforma possa ser executada de forma consistente entre ambientes.

#### Acceptance Criteria

1. THE Sistema SHALL ser distribuído com definições de contêiner (Docker) para a aplicação backend.
2. THE Sistema SHALL prover uma composição de serviços (Docker Compose) que inclua PostgreSQL, Redis, armazenamento compatível com S3 (MinIO) e proxy reverso.
3. WHEN a composição de serviços é iniciada, THE Sistema SHALL disponibilizar um endpoint de verificação de saúde (health check) que retorne o estado operacional do backend.
4. WHEN o backend é iniciado, THE Sistema SHALL aplicar as migrações de banco de dados pendentes antes de aceitar requisições de negócio.
5. IF um serviço de dependência obrigatório (PostgreSQL, Redis ou armazenamento S3) estiver indisponível na inicialização, THEN THE Sistema SHALL reportar estado não saudável no endpoint de verificação de saúde.
6. THE Sistema SHALL obter parâmetros de conexão e segredos a partir de variáveis de ambiente ou de um cofre de configuração externo.
7. THE Sistema SHALL expor o tráfego HTTP externo por meio do proxy reverso configurado.

### Requirement 2: Autenticação de usuários

**User Story:** Como usuário, quero autenticar-me com credenciais, para que eu possa acessar as funcionalidades autorizadas da plataforma.

#### Acceptance Criteria

1. WHEN um Usuario ativo submete credenciais válidas, THE Serviço_Autenticacao SHALL emitir um JWT de acesso e um Refresh_Token associados à Sessao.
2. IF um Usuario submete credenciais inválidas, THEN THE Serviço_Autenticacao SHALL rejeitar a autenticação e retornar uma mensagem de erro genérica que não revele qual campo está incorreto.
3. WHEN um Usuario autenticado solicita logout, THE Serviço_Autenticacao SHALL invalidar o Refresh_Token da Sessao correspondente.
4. WHEN um Usuario apresenta um Refresh_Token válido e não expirado, THE Serviço_Autenticacao SHALL emitir um novo JWT de acesso.
5. IF um Refresh_Token expirado, revogado ou inválido é apresentado, THEN THE Serviço_Autenticacao SHALL recusar a renovação e exigir nova autenticação.
6. THE Serviço_Autenticacao SHALL expirar cada JWT de acesso em, no máximo, 15 minutos após a emissão.
7. WHEN um JWT de acesso expira, THE Serviço_Autenticacao SHALL recusar requisições que o utilizem e retornar o código de status HTTP 401.
8. THE Serviço_Autenticacao SHALL armazenar senhas exclusivamente como hashes gerados por função de derivação de chave resistente a força bruta.
9. WHERE o MFA está habilitado para um Usuario, THE Serviço_Autenticacao SHALL exigir um segundo fator válido antes de emitir os tokens da Sessao.
10. THE Serviço_Autenticacao SHALL estruturar a autenticação de forma a permitir habilitar MFA por Usuario sem alteração do fluxo de login dos demais usuários.

### Requirement 3: Recuperação e alteração de senha

**User Story:** Como usuário, quero recuperar e alterar minha senha, para que eu mantenha o acesso seguro à minha conta.

#### Acceptance Criteria

1. WHEN um Usuario solicita recuperação de senha informando um email cadastrado, THE Serviço_Autenticacao SHALL gerar um token de redefinição de uso único com validade máxima de 60 minutos.
2. IF um email não cadastrado é informado na recuperação de senha, THEN THE Serviço_Autenticacao SHALL responder com a mesma mensagem apresentada para email cadastrado, sem revelar a existência da conta.
3. WHEN um token de redefinição válido e não expirado é apresentado com uma nova senha em conformidade com a política de senhas, THE Serviço_Autenticacao SHALL atualizar o hash da senha e invalidar o token de redefinição.
4. WHEN a senha de um Usuario é alterada, THE Serviço_Autenticacao SHALL invalidar todos os Refresh_Token ativos desse Usuario.
5. WHEN um Usuario autenticado altera a própria senha informando a senha atual correta e uma nova senha válida, THE Serviço_Autenticacao SHALL atualizar o hash da senha.
6. IF a senha atual informada na alteração está incorreta, THEN THE Serviço_Autenticacao SHALL rejeitar a alteração.
7. THE Serviço_Autenticacao SHALL aceitar apenas senhas com no mínimo 8 caracteres contendo ao menos uma letra maiúscula, uma letra minúscula, um dígito e um caractere especial.

### Requirement 4: Proteção contra tentativas de acesso abusivas

**User Story:** Como responsável de segurança, quero limitar tentativas de login e requisições abusivas, para que a plataforma resista a ataques de força bruta e abuso.

#### Acceptance Criteria

1. WHEN uma tentativa de login ocorre, THE Serviço_Autenticacao SHALL registrar o resultado (sucesso ou falha), o horário e o endereço IP de origem.
2. WHILE um Usuario acumula falhas de autenticação consecutivas, THE Serviço_Autenticacao SHALL incrementar o contador de tentativas falhas desse Usuario.
3. IF um Usuario atinge 5 falhas de autenticação consecutivas, THEN THE Serviço_Autenticacao SHALL bloquear temporariamente a conta por 15 minutos.
4. WHILE uma conta está temporariamente bloqueada, THE Serviço_Autenticacao SHALL recusar novas tentativas de login desse Usuario e informar o estado de bloqueio.
5. WHEN um Usuario autentica com sucesso, THE Serviço_Autenticacao SHALL zerar o contador de tentativas falhas desse Usuario.
6. THE Serviço_RateLimit SHALL aplicar limites de taxa de requisições por Usuario, por endereço IP, por Tenant e por endpoint.
7. IF um limite de taxa configurado é excedido, THEN THE Serviço_RateLimit SHALL recusar a requisição e retornar o código de status HTTP 429.

### Requirement 5: Controle de acesso baseado em papéis (RBAC)

**User Story:** Como administrador, quero atribuir papéis e permissões granulares aos usuários, para que cada usuário acesse apenas o que lhe é autorizado.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL suportar os papéis Super Admin, Administrador, Financeiro, Operacional, Estoque, Técnico, Supervisor e Cliente.
2. THE Serviço_Autorizacao SHALL associar cada Papel a um conjunto de Permissoes granulares por recurso e operação.
3. WHEN um Usuario autenticado requisita uma operação, THE Serviço_Autorizacao SHALL autorizar a operação somente se algum Papel do Usuario possuir a Permissao correspondente.
4. IF um Usuario autenticado requisita uma operação sem a Permissao correspondente, THEN THE Serviço_Autorizacao SHALL recusar a operação e retornar o código de status HTTP 403.
5. WHEN um Papel é atribuído ou removido de um Usuario, THE Serviço_Autorizacao SHALL aplicar o conjunto de Permissoes resultante na próxima avaliação de autorização do Usuario.
6. THE Serviço_Autorizacao SHALL avaliar as Permissoes no backend para toda operação, independentemente de controles apresentados pelo frontend.

### Requirement 6: Isolamento multi-tenant

**User Story:** Como operador da plataforma, quero que os dados de cada tenant fiquem isolados, para que nenhum tenant acesse dados de outro tenant.

#### Acceptance Criteria

1. THE Sistema SHALL definir a entidade Tenant como unidade de isolamento de dados de negócio.
2. THE Sistema SHALL exigir um tenant_id não nulo em toda entidade de negócio persistida.
3. WHEN uma requisição autenticada é processada, THE Serviço_Tenant SHALL resolver o tenant_id a partir do contexto de autenticação do Usuario, e não a partir de valores fornecidos pelo frontend.
4. WHEN uma consulta a entidades de negócio é executada, THE Serviço_Tenant SHALL aplicar automaticamente, na camada de ORM, um filtro pelo tenant_id do contexto autenticado.
5. IF uma operação tenta ler ou escrever um registro cujo tenant_id difere do tenant_id do contexto autenticado, THEN THE Serviço_Tenant SHALL recusar a operação e retornar o código de status HTTP 404.
6. THE Serviço_Tenant SHALL validar no backend a consistência do tenant_id de toda entidade antes de persistir alterações.
7. WHERE o Usuario possui o papel Super Admin, THE Serviço_Tenant SHALL permitir operações administrativas entre tenants apenas em endpoints administrativos explicitamente designados.
8. THE Sistema SHALL manter testes automatizados que verifiquem a ausência de acesso a dados entre tenants distintos.
9. WHEN uma operação de acesso a dados entre tenants é tentada e recusada, THE Serviço_Auditoria SHALL registrar o evento no AuditLog.

### Requirement 7: Gestão de usuários

**User Story:** Como administrador, quero gerenciar os usuários do meu tenant, para que eu controle quem tem acesso e com quais papéis.

#### Acceptance Criteria

1. WHEN um administrador autorizado cria um Usuario informando dados válidos, THE Serviço_Usuario SHALL persistir o Usuario vinculado ao tenant_id do contexto autenticado.
2. IF a criação de um Usuario informa um email já existente no mesmo Tenant, THEN THE Serviço_Usuario SHALL rejeitar a operação e retornar erro de conflito.
3. WHEN um administrador autorizado atualiza os dados ou papéis de um Usuario do próprio Tenant, THE Serviço_Usuario SHALL persistir as alterações.
4. WHEN um administrador autorizado desativa um Usuario, THE Serviço_Usuario SHALL marcar o Usuario como inativo e invalidar seus Refresh_Token ativos.
5. WHILE um Usuario está inativo, THE Serviço_Autenticacao SHALL recusar tentativas de autenticação desse Usuario.
6. WHEN um administrador autorizado reativa um Usuario, THE Serviço_Usuario SHALL marcar o Usuario como ativo.
7. WHEN um administrador autorizado lista usuários, THE Serviço_Usuario SHALL retornar uma Pagina_de_Resultados restrita ao tenant_id do contexto autenticado.
8. WHEN uma ação de criação, atualização, desativação, reativação ou alteração de papéis de Usuario é concluída, THE Serviço_Auditoria SHALL registrar o evento no AuditLog.

### Requirement 8: Cadastro de Clientes

**User Story:** Como usuário operacional, quero cadastrar e manter Clientes, para que eu registre as empresas atendidas pelo meu tenant.

#### Acceptance Criteria

1. WHEN um Usuario autorizado cria um Cliente informando razão social, CNPJ e demais campos válidos, THE Serviço_Cliente SHALL persistir o Cliente vinculado ao tenant_id do contexto autenticado.
2. THE Serviço_Cliente SHALL armazenar por Cliente os campos: razão social, nome fantasia, CNPJ, inscrição estadual, telefone, email, endereço, cidade, estado, CEP, observações e status.
3. THE Serviço_Cliente SHALL restringir o status de um Cliente aos valores ativo, inativo e bloqueado.
4. IF a criação ou atualização de um Cliente informa um CNPJ com formato inválido, THEN THE Serviço_Cliente SHALL rejeitar a operação e retornar erro de validação.
5. IF a criação de um Cliente informa um CNPJ já existente no mesmo Tenant, THEN THE Serviço_Cliente SHALL rejeitar a operação e retornar erro de conflito.
6. WHEN um Usuario autorizado atualiza um Cliente do próprio Tenant, THE Serviço_Cliente SHALL persistir as alterações informadas.
7. WHEN um Usuario autorizado consulta um Cliente por identificador, THE Serviço_Cliente SHALL retornar o Cliente somente se pertencer ao tenant_id do contexto autenticado.
8. WHEN um Usuario autorizado lista Clientes, THE Serviço_Cliente SHALL retornar uma Pagina_de_Resultados restrita ao tenant_id do contexto autenticado, com suporte a paginação e ordenação.
9. WHEN um Usuario autorizado altera o status de um Cliente, THE Serviço_Cliente SHALL persistir o novo status.

### Requirement 9: Cadastro de Locais (pontos de instalação)

**User Story:** Como usuário operacional, quero cadastrar Locais vinculados a um Cliente, para que eu identifique os pontos de instalação onde os serviços serão prestados.

#### Acceptance Criteria

1. WHEN um Usuario autorizado cria um Local informando um Cliente existente e dados válidos, THE Serviço_Local SHALL persistir o Local vinculado ao Cliente e ao tenant_id do contexto autenticado.
2. THE Serviço_Local SHALL armazenar por Local os campos: nome, endereço, responsável, telefone, email, observações e status.
3. THE Serviço_Local SHALL associar cada Local a exatamente um Cliente, e cada Cliente SHALL poder possuir múltiplos Locais.
4. IF a criação de um Local referencia um Cliente inexistente ou pertencente a outro Tenant, THEN THE Serviço_Local SHALL rejeitar a operação e retornar erro de validação.
5. WHEN um Usuario autorizado atualiza um Local do próprio Tenant, THE Serviço_Local SHALL persistir as alterações informadas.
6. WHEN um Usuario autorizado consulta um Local por identificador, THE Serviço_Local SHALL retornar o Local somente se pertencer ao tenant_id do contexto autenticado.
7. WHEN um Usuario autorizado lista Locais de um Cliente, THE Serviço_Local SHALL retornar uma Pagina_de_Resultados restrita ao tenant_id do contexto autenticado, com suporte a paginação e ordenação.
8. WHEN um Usuario autorizado altera o status de um Local, THE Serviço_Local SHALL persistir o novo status.

### Requirement 10: Auditoria de ações sensíveis

**User Story:** Como responsável de conformidade, quero registrar ações sensíveis, para que eu possa rastrear quem fez o quê e quando.

#### Acceptance Criteria

1. WHEN uma ação sobre Usuarios, papéis ou permissões é concluída, THE Serviço_Auditoria SHALL registrar no AuditLog o identificador do ator, o tenant_id, o tipo de ação, o recurso afetado e o horário.
2. WHEN um evento de login (sucesso ou falha) ocorre, THE Serviço_Auditoria SHALL registrar o evento no AuditLog.
3. THE Serviço_Auditoria SHALL associar cada registro de AuditLog ao tenant_id do contexto em que a ação ocorreu.
4. THE Serviço_Auditoria SHALL persistir os registros de AuditLog de forma somente-adição, sem permitir alteração de registros existentes por operações de negócio.
5. WHEN um administrador autorizado consulta o AuditLog, THE Serviço_Auditoria SHALL retornar uma Pagina_de_Resultados restrita ao tenant_id do contexto autenticado.

### Requirement 11: Observabilidade e proteção de dados sensíveis em logs

**User Story:** Como operador da plataforma, quero logs estruturados e seguros, para que eu diagnostique problemas sem expor segredos.

#### Acceptance Criteria

1. THE Sistema SHALL emitir logs de aplicação em formato JSON estruturado.
2. THE Sistema SHALL excluir dos logs senhas, tokens, segredos e credenciais.
3. WHEN uma requisição é processada, THE Sistema SHALL incluir no log um identificador de correlação e o tenant_id do contexto, quando disponível.
4. IF um erro não tratado ocorre durante o processamento de uma requisição, THEN THE Sistema SHALL registrar o erro com contexto suficiente para diagnóstico, sem incluir dados sensíveis.

### Requirement 12: Contrato de API, validação e escala

**User Story:** Como consumidor da API, quero contratos de dados validados e consultas escaláveis, para que a integração seja segura e eficiente mesmo com muitos tenants.

#### Acceptance Criteria

1. THE Sistema SHALL expor operações de negócio por meio de DTOs distintos das entidades de persistência.
2. WHEN uma requisição contém entrada inválida em relação ao DTO esperado, THE Sistema SHALL rejeitar a requisição e retornar o código de status HTTP 400 com detalhes de validação.
3. THE Sistema SHALL aplicar todas as regras de negócio e de autorização no backend, independentemente de validações realizadas pelo frontend.
4. WHEN uma operação de listagem é solicitada, THE Sistema SHALL retornar uma Pagina_de_Resultados com tamanho de página limitado a, no máximo, 100 registros por requisição.
5. THE Sistema SHALL definir índices de banco de dados sobre tenant_id nas entidades de negócio e sobre os campos utilizados em filtros e ordenações de listagem.
