# Requirements Document

## Introduction

Este documento especifica a **FASE 2 (Phase 2)** da plataforma **EasyPanel** — a plataforma SaaS de gestão de outsourcing de impressão e ativos de TI. A Fase 2 tem como tema **"Windows Client + Registro + Heartbeat + Descoberta + SNMP + Impressoras + Contadores"** e cobre tanto o **backend** quanto o **agente Windows (Windows Client)**.

Este spec **constrói sobre a fundação já entregue e concluída na Fase 1**, documentada no spec `easypanel-print-outsourcing-platform`. Os recursos da Fase 1 **não são redefinidos aqui** e são premissas assumidas como existentes e disponíveis: infraestrutura containerizada (Docker Compose, PostgreSQL, Redis, MinIO, Caddy, OpenTelemetry/Serilog), isolamento multi-tenant (entidade Tenant, ITenantContext, filtros globais de consulta do EF + interceptor de escrita no SaveChanges, acesso cross-tenant → 404), autenticação de usuários (ASP.NET Identity, JWT ≤15min + refresh tokens opacos rotativos, preparação para MFA, fluxos de senha, bloqueio), RBAC (8 papéis com permissões granulares `recurso.acao` avaliadas no backend), AuditLog somente-adição, rate limiting, tratamento de erros via ProblemDetails, e CRUD de Clientes e Locais. A hierarquia existente é: Tenant → Cliente → Local; a Fase 2 estende essa hierarquia com Local → Windows_Client / Impressora.

A Fase 2 entrega: (a) um **agente Windows** independente (Windows Service em C# .NET 10 Worker Service) capaz de se instalar, autenticar, descobrir e monitorar impressoras via SNMP e reportar dados ao backend com resiliência offline; (b) os **endpoints de backend** para registro, heartbeat, coleta, upload, configuração e atualização do agente, com autenticação própria do cliente distinta dos JWT de usuário; (c) as **entidades de negócio** Windows_Client, Impressora, PrinterCounter, PrinterEvent, Coleta e o histórico de movimentação de impressoras; (d) o **pipeline de ingestão assíncrono** (CLIENT → API → FILA → WORKERS → PROCESSAMENTO → DB → EVENTOS) com idempotência; e (e) a **segurança específica do cliente** e o **auto-update assinado** do agente.

### Fora de escopo (adiado para fases futuras)

As capacidades a seguir **NÃO** fazem parte desta Fase 2 e serão detalhadas em specs futuras:

- Motor completo de **alertas / regras** e notificações. A Fase 2 apenas **registra o estado de heartbeat ausente e condições de falha de coleta** como dados, para que a Fase 3 os consuma; o motor de regras de alerta **não** é construído aqui.
- **Suprimentos e estoque**, trocas, chamados (tickets), contratos, fechamento, faturamento, relatórios, dashboards, webhooks e portal do cliente → Fase 3+.
- **Implementação completa de SNMP v3** — a Fase 2 suporta SNMP v1 e v2c e apenas **arquiteta** para v3 (extensibilidade), sem entregar a implementação v3.
- **Telas de frontend (React)** — os requisitos permanecem focados em **API/dados**; a UI é uma preocupação de apresentação construída separadamente.

### Princípios não funcionais herdados da Fase 1 (válidos na Fase 2)

- O **isolamento multi-tenant é inegociável**: Windows_Client, Impressora, PrinterCounter, PrinterEvent, Coleta e demais entidades de negócio são escopadas por Tenant. A autenticação própria do cliente resolve o Tenant/Local a partir da **identidade autenticada do cliente**, nunca de valores de tenant fornecidos na requisição.
- O **backend é a autoridade**: validar toda entrada, usar DTOs, logs JSON estruturados sem segredos (nunca registrar credenciais SNMP/tokens), auditar ações sensíveis (ajuste de contador, movimentação de impressora, registro de cliente). Idempotência e rate limiting aplicam-se aos endpoints do cliente.
- **Projetado para grande escala** (100k impressoras, milhões de contadores/eventos): paginação, indexação, paginação por cursor onde necessário, e processamento assíncrono.
- **Reuso dos padrões da Fase 1**: padrão Result/Error, PagedResult/PageRequest, mapeamento ProblemDetails, modelo de permissões RBAC (novas permissões como `printer.view/create/edit/move/monitor`, `counter.view/adjust`, `client.view/manage`) e o AuditLog.

## Glossary

- **EasyPanel**: A plataforma SaaS completa objeto deste produto.
- **Sistema**: O backend do EasyPanel (ASP.NET Core Web API) responsável por aplicar regras de negócio e segurança. Termo genérico usado quando nenhum subsistema específico é mais preciso.
- **Tenant**: Entidade organizacional que representa uma instância isolada de dados. Toda entidade de negócio pertence a exatamente um Tenant. (Definida na Fase 1.)
- **tenant_id**: Identificador do Tenant, obrigatório em todas as entidades de negócio. (Definido na Fase 1.)
- **Cliente**: Empresa atendida por um Tenant (entidade de negócio da Fase 1), à qual pertencem Locais.
- **Local**: Ponto de instalação físico pertencente a um Cliente (entidade da Fase 1). Um Windows_Client pertence a exatamente um Local.
- **Usuario**: Pessoa autenticável no Sistema via JWT de usuário (Fase 1), distinta do Windows_Client.
- **RBAC**: Controle de Acesso Baseado em Papéis (Fase 1), estendido na Fase 2 com novas permissões de impressora, contador e cliente.
- **Permissao**: Autorização granular no formato `recurso.acao` avaliada no backend (Fase 1).
- **AuditLog**: Registro persistente somente-adição de eventos sensíveis (Fase 1), reutilizado na Fase 2.
- **ProblemDetails**: Formato de resposta de erro RFC 7807 usado pelo Sistema (Fase 1).
- **Pagina_de_Resultados**: Subconjunto ordenado e limitado de registros retornado por listagens (PagedResult da Fase 1).
- **Paginacao_por_Cursor**: Esquema de paginação baseado em cursor (chave estável), usado para grandes volumes de contadores e eventos.
- **Windows_Client**: Aplicação agente instalada em ambiente Windows do Cliente, executada como Windows Service (.NET 10 Worker Service), responsável por descobrir e monitorar impressoras e reportar dados ao backend. Também chamado de "agente" ou "cliente". É uma entidade de negócio escopada por Tenant e vinculada a um Local.
- **Serviço_Registro_Cliente**: Subsistema de backend responsável por registrar Windows_Clients e emitir suas credenciais.
- **Serviço_Autenticacao_Cliente**: Subsistema de backend responsável pela autenticação própria do Windows_Client (credenciais/tokens do cliente, distintos dos JWT de usuário).
- **Serviço_Heartbeat**: Subsistema de backend responsável por receber e registrar os heartbeats dos Windows_Clients e o estado de heartbeat ausente.
- **Serviço_Ingestao**: Subsistema de backend responsável por receber submissões de coleta/upload, aplicar idempotência e enfileirar para processamento.
- **Serviço_Processamento**: Subsistema de backend (Worker) responsável por consumir a fila e persistir contadores, status e eventos.
- **Serviço_Impressora**: Subsistema de backend responsável pelo cadastro, consulta, movimentação e ciclo de vida de Impressoras.
- **Serviço_Contador**: Subsistema de backend responsável por persistir e consultar PrinterCounters e aplicar as regras de validação de contador.
- **Serviço_Configuracao_Cliente**: Subsistema de backend responsável por prover a configuração operacional ao Windows_Client.
- **Serviço_Atualizacao_Cliente**: Subsistema de backend responsável por prover pacotes de atualização do Windows_Client com metadados de integridade.
- **Serviço_Auditoria**: Subsistema de backend responsável por registrar eventos no AuditLog (Fase 1).
- **Serviço_RateLimit**: Subsistema de backend responsável por limitar a taxa de requisições (Fase 1), aplicado também aos endpoints do cliente.
- **Net_Monitoring_Service**: Serviço interno do Windows_Client responsável pela descoberta e monitoramento de rede (SNMP/USB).
- **Communication_Service**: Serviço interno do Windows_Client responsável pela comunicação HTTPS com o backend e pela fila local.
- **Update_Service**: Serviço interno do Windows_Client responsável por obter, validar e aplicar atualizações do agente.
- **Guardian_Service**: Serviço interno do Windows_Client responsável por supervisionar os demais serviços internos e reiniciar serviços parados.
- **Fila_Local**: Armazenamento local persistente do Windows_Client (SQLite) usado para reter dados quando o backend está indisponível.
- **Heartbeat**: Mensagem periódica enviada pelo Windows_Client ao backend contendo estado e metadados do agente.
- **Descoberta**: Processo executado pelo Net_Monitoring_Service para localizar impressoras na rede por IP, faixa de IP, sub-rede, broadcast, SNMP ou USB.
- **SNMP**: Simple Network Management Protocol, usado para coletar dados das impressoras. A Fase 2 suporta v1 e v2c.
- **IPrinterDriver**: Abstração (driver/adaptador) por fabricante que interpreta OIDs/dados SNMP em atributos normalizados de impressora, sem embutir especificidades de fabricante no núcleo.
- **Impressora**: Entidade de negócio que representa um equipamento de impressão monitorado, pertencente a Tenant → Cliente → Local.
- **Patrimonio**: Identificador de ativo (asset tag) atribuído a uma Impressora pela organização.
- **PrinterCounter**: Entidade de negócio que representa uma leitura de contador de uma Impressora em um instante.
- **Tipo_de_Contador**: Categoria de contador (P&B, colorido, A3, A4, digitalização e outros configuráveis).
- **PrinterEvent**: Entidade de negócio que representa um evento relevante do ciclo de vida ou operação de uma Impressora (movimentação, mudança de status, falha de coleta, heartbeat ausente).
- **Coleta**: Entidade de negócio (Collection) que representa uma execução de coleta de dados por um Windows_Client sobre uma ou mais Impressoras, com início, fim, resultado e erros.
- **Chave_de_Idempotencia**: Identificador único de uma Coleta usado para garantir que o reenvio da mesma coleta não duplique dados.
- **Historico_de_Movimentacao**: Registro somente-adição das mudanças de ciclo de vida de uma Impressora (instalação, transferência, recolhimento, desativação, reativação).
- **DTO**: Data Transfer Object; contrato de dados de entrada/saída da API, distinto das entidades de persistência (Fase 1).

## Requirements

### Requirement 1: Ciclo de vida do agente Windows como serviço

**User Story:** Como responsável de TI do Cliente, quero que o agente Windows opere como um serviço confiável e autônomo, para que o monitoramento ocorra sem intervenção manual.

#### Acceptance Criteria

1. THE Windows_Client SHALL executar como um Windows Service com inicialização automática junto ao sistema operacional Windows.
2. WHEN o processo do Windows_Client é encerrado de forma anormal, THE Windows_Client SHALL ser reiniciado automaticamente pelo mecanismo de recuperação do serviço.
3. THE Windows_Client SHALL suportar instalação silenciosa sem interação do usuário final.
4. THE Windows_Client SHALL suportar distribuição e instalação por política de grupo (GPO).
5. IF uma segunda instância do Windows_Client é iniciada no mesmo host enquanto uma instância já está em execução, THEN THE Windows_Client SHALL encerrar a segunda instância mantendo apenas uma instância ativa.
6. THE Windows_Client SHALL registrar logs de operação em armazenamento local do host.
7. THE Windows_Client SHALL ler sua configuração operacional a partir de armazenamento de configuração local do host.
8. THE Windows_Client SHALL prover um modo de diagnóstico que reporte o estado dos serviços internos e a conectividade com o backend.

### Requirement 2: Separação de serviços internos e supervisão (Guardian)

**User Story:** Como operador da plataforma, quero que o agente seja composto por serviços internos supervisionados, para que uma falha isolada seja recuperada sem parar o agente inteiro.

#### Acceptance Criteria

1. THE Windows_Client SHALL organizar suas responsabilidades em serviços internos distintos: Net_Monitoring_Service, Communication_Service, Update_Service e Guardian_Service.
2. WHILE o Windows_Client está em execução, THE Guardian_Service SHALL monitorar periodicamente o estado dos demais serviços internos.
3. IF o Guardian_Service detecta que um serviço interno supervisionado está parado, THEN THE Guardian_Service SHALL reiniciar o serviço interno parado.
4. WHEN o Guardian_Service reinicia um serviço interno, THE Windows_Client SHALL registrar o evento de reinício no log local.

### Requirement 3: Identidade e registro do agente

**User Story:** Como administrador do Tenant, quero registrar cada agente Windows de forma identificável, para que eu saiba qual agente pertence a qual Local.

#### Acceptance Criteria

1. THE Windows_Client SHALL possuir um identificador único e persistente que o distinga de qualquer outro Windows_Client.
2. WHEN um Windows_Client submete uma requisição de registro válida ao endpoint `POST /api/v1/client/register`, THE Serviço_Registro_Cliente SHALL criar uma entidade Windows_Client vinculada ao Local e ao tenant_id resolvidos a partir da credencial de registro autenticada.
3. THE Serviço_Registro_Cliente SHALL permitir múltiplos Windows_Clients vinculados ao mesmo Cliente.
4. WHEN o registro de um Windows_Client é concluído com sucesso, THE Serviço_Autenticacao_Cliente SHALL emitir ao Windows_Client credenciais de autenticação próprias, distintas dos JWT de Usuario.
5. IF uma requisição de registro apresenta credencial de registro inválida ou expirada, THEN THE Serviço_Registro_Cliente SHALL recusar o registro e retornar o código de status HTTP 401.
6. WHEN um registro de Windows_Client é concluído, THE Serviço_Auditoria SHALL registrar o evento no AuditLog com o identificador do Windows_Client, o tenant_id e o Local.
7. THE Serviço_Registro_Cliente SHALL armazenar por Windows_Client os campos: identificador único, hostname, versão do agente, Local associado, tenant_id, data de registro e estado.

### Requirement 4: Autenticação própria do agente

**User Story:** Como responsável de segurança, quero que o agente Windows use autenticação própria e de curta duração, para que o comprometimento de um agente tenha impacto limitado.

#### Acceptance Criteria

1. WHEN um Windows_Client apresenta suas credenciais próprias válidas, THE Serviço_Autenticacao_Cliente SHALL emitir um token de acesso de curta duração associado ao Windows_Client.
2. THE Serviço_Autenticacao_Cliente SHALL resolver o tenant_id e o Local de toda requisição do Windows_Client a partir da identidade autenticada do Windows_Client, e não a partir de valores fornecidos na requisição.
3. IF uma requisição do Windows_Client apresenta token de cliente ausente, inválido ou expirado, THEN THE Serviço_Autenticacao_Cliente SHALL recusar a requisição e retornar o código de status HTTP 401.
4. WHEN o token de acesso do Windows_Client expira, THE Serviço_Autenticacao_Cliente SHALL permitir a obtenção de um novo token de acesso mediante apresentação de uma credencial de renovação válida do Windows_Client.
5. THE Windows_Client SHALL armazenar suas credenciais e tokens em armazenamento local protegido, sem persistir senhas em texto claro.
6. IF uma requisição do Windows_Client tenta acessar ou submeter dados associados a um tenant_id ou Local distintos dos resolvidos pela sua identidade autenticada, THEN THE Serviço_Autenticacao_Cliente SHALL recusar a operação e retornar o código de status HTTP 404.

### Requirement 5: Comunicação segura e resiliência offline

**User Story:** Como responsável de segurança e de operação, quero que o agente comunique-se apenas por HTTPS de saída e opere temporariamente offline, para que não haja portas de entrada e nenhum dado seja perdido durante quedas de conectividade.

#### Acceptance Criteria

1. THE Windows_Client SHALL comunicar-se com o backend exclusivamente por conexões HTTPS iniciadas pelo próprio Windows_Client (tráfego de saída), sem abrir portas de entrada no host.
2. WHEN o Windows_Client estabelece uma conexão com o backend, THE Windows_Client SHALL validar o certificado TLS do backend antes de transmitir dados.
3. IF a validação do certificado TLS do backend falha, THEN THE Windows_Client SHALL abortar a transmissão e registrar o evento no log local.
4. WHILE o backend está inacessível, THE Communication_Service SHALL persistir na Fila_Local os dados de coleta pendentes de envio.
5. WHEN a conectividade com o backend é restabelecida, THE Communication_Service SHALL transmitir os itens pendentes da Fila_Local ao backend.
6. IF o envio de um item da Fila_Local falha, THEN THE Communication_Service SHALL aplicar nova tentativa segundo uma política de retry com espera crescente.
7. THE Windows_Client SHALL excluir dos logs locais quaisquer credenciais, tokens e credenciais SNMP.

### Requirement 6: Heartbeat periódico e detecção de ausência

**User Story:** Como operador da plataforma, quero receber heartbeats periódicos dos agentes e registrar quando um agente para de responder, para que a Fase 3 possa gerar alertas com base nesse estado.

#### Acceptance Criteria

1. WHILE o Windows_Client está em execução e autenticado, THE Windows_Client SHALL enviar um heartbeat ao endpoint `POST /api/v1/client/heartbeat` a cada 60 segundos.
2. THE Windows_Client SHALL incluir em cada heartbeat: identificador do Windows_Client, versão do agente, hostname, timestamp, estado, quantidade de impressoras, e horário da última coleta.
3. WHEN o Serviço_Heartbeat recebe um heartbeat válido, THE Serviço_Heartbeat SHALL atualizar o horário do último heartbeat e o estado reportado do Windows_Client correspondente.
4. IF o Serviço_Heartbeat não recebe um heartbeat de um Windows_Client dentro de um limite de tempo configurável, THEN THE Serviço_Heartbeat SHALL marcar o Windows_Client com o estado de heartbeat ausente e registrar um PrinterEvent do tipo heartbeat ausente para consumo posterior.
5. WHEN um Windows_Client em estado de heartbeat ausente volta a enviar um heartbeat válido, THE Serviço_Heartbeat SHALL restaurar o estado do Windows_Client para ativo.

### Requirement 7: Descoberta de impressoras

**User Story:** Como usuário operacional, quero que o agente descubra impressoras na rede por múltiplos métodos, para que eu selecione quais equipamentos monitorar.

#### Acceptance Criteria

1. THE Net_Monitoring_Service SHALL suportar descoberta de impressoras por IP individual, faixa de IP, sub-rede, broadcast, SNMP e USB.
2. WHEN uma descoberta é solicitada segundo a configuração vigente, THE Net_Monitoring_Service SHALL produzir uma lista de impressoras candidatas com os atributos identificáveis de cada candidata.
3. WHEN uma impressora candidata é testada, THE Net_Monitoring_Service SHALL reportar o resultado do teste de comunicação com a candidata.
4. WHEN o resultado de uma descoberta é reportado ao endpoint `POST /api/v1/client/collect`, THE Serviço_Ingestao SHALL associar as impressoras candidatas ao tenant_id e ao Local resolvidos pela identidade autenticada do Windows_Client.
5. WHERE uma impressora candidata é marcada como ignorada, THE Net_Monitoring_Service SHALL excluir a candidata ignorada das descobertas subsequentes até nova instrução de configuração.
6. WHEN uma impressora candidata é selecionada e registrada, THE Serviço_Impressora SHALL criar a Impressora correspondente com monitoramento habilitável.

### Requirement 8: Coleta SNMP e arquitetura de drivers por fabricante

**User Story:** Como usuário operacional, quero que o agente colete dados das impressoras via SNMP usando drivers específicos por fabricante, para que dados de fabricantes distintos sejam normalizados de forma extensível.

#### Acceptance Criteria

1. THE Net_Monitoring_Service SHALL coletar dados de impressoras via SNMP nas versões v1 e v2c.
2. THE Net_Monitoring_Service SHALL estruturar a interpretação dos dados SNMP por meio da abstração IPrinterDriver, com implementações por fabricante para HP, Canon, Epson, Brother, Kyocera, Xerox, Ricoh, Konica e Lexmark.
3. THE Net_Monitoring_Service SHALL manter as especificidades de fabricante restritas às implementações de IPrinterDriver, sem embutir especificidades de fabricante no núcleo de coleta.
4. WHERE um novo fabricante é adicionado por meio de uma nova implementação de IPrinterDriver, THE Net_Monitoring_Service SHALL passar a interpretar dados desse fabricante sem alteração do núcleo de coleta.
5. WHEN dados SNMP de uma impressora estão disponíveis, THE Net_Monitoring_Service SHALL coletar, dentre os disponíveis: fabricante, modelo, número de série, hostname, IP, MAC, uptime, status, contadores, toner, cilindro (drum), unidades, bandejas, papel e erros.
6. THE Net_Monitoring_Service SHALL estruturar o suporte a SNMP de forma a permitir a adição futura de SNMP v3 sem redesenho do núcleo de coleta.
7. THE Windows_Client SHALL excluir credenciais SNMP de qualquer log local ou dado transmitido para fins de diagnóstico.

### Requirement 9: Cadastro e consulta de Impressoras

**User Story:** Como usuário operacional, quero cadastrar e consultar impressoras com seus atributos, para que eu gerencie o parque de impressão.

#### Acceptance Criteria

1. THE Serviço_Impressora SHALL armazenar por Impressora os campos: identificador, tenant_id, Cliente, Local, fabricante, modelo, número de série, patrimônio, IP, MAC, hostname, protocolo, porta, status, monitoramento ativo, data de instalação, data de registro e observações.
2. THE Serviço_Impressora SHALL restringir o status de uma Impressora aos valores online, offline, desconhecido, desabilitado e sem-comunicação.
3. WHEN um Usuario autorizado cria uma Impressora informando dados válidos, THE Serviço_Impressora SHALL persistir a Impressora vinculada ao Local, ao Cliente e ao tenant_id do contexto autenticado.
4. WHEN um Usuario autorizado consulta uma Impressora por identificador, THE Serviço_Impressora SHALL retornar a Impressora somente se pertencer ao tenant_id do contexto autenticado.
5. WHEN um Usuario autorizado lista Impressoras, THE Serviço_Impressora SHALL retornar uma Pagina_de_Resultados restrita ao tenant_id do contexto autenticado, com suporte a filtros, busca, ordenação e paginação.
6. WHEN um Usuario autorizado solicita exportação de uma listagem de Impressoras, THE Serviço_Impressora SHALL retornar os dados da listagem restritos ao tenant_id do contexto autenticado, respeitando os filtros aplicados.
7. WHEN um Usuario autorizado executa uma ação em lote sobre um conjunto de Impressoras do próprio Tenant, THE Serviço_Impressora SHALL aplicar a ação a cada Impressora do conjunto que pertença ao tenant_id do contexto autenticado.
8. WHEN uma operação de criação ou edição de Impressora é concluída, THE Serviço_Auditoria SHALL registrar o evento no AuditLog.

### Requirement 10: Movimentação e ciclo de vida de Impressoras

**User Story:** Como usuário operacional, quero registrar a movimentação e o ciclo de vida das impressoras com histórico permanente, para que eu rastreie onde cada equipamento esteve e seu estado ao longo do tempo.

#### Acceptance Criteria

1. THE Serviço_Impressora SHALL suportar as operações de ciclo de vida: instalar, transferir, recolher, desabilitar e reativar uma Impressora.
2. WHEN uma operação de ciclo de vida é concluída sobre uma Impressora do próprio Tenant, THE Serviço_Impressora SHALL registrar uma entrada no Historico_de_Movimentacao com o tipo de operação, o Local de origem, o Local de destino quando aplicável, o ator e o horário.
3. THE Serviço_Impressora SHALL persistir o Historico_de_Movimentacao de forma somente-adição, sem permitir exclusão de entradas existentes.
4. WHEN uma Impressora é transferida para outro Local do mesmo Tenant, THE Serviço_Impressora SHALL atualizar o Local vigente da Impressora preservando as entradas anteriores do Historico_de_Movimentacao.
5. WHEN uma Impressora é desabilitada, THE Serviço_Impressora SHALL definir o status da Impressora como desabilitado e suspender a coleta associada.
6. WHEN uma Impressora desabilitada é reativada, THE Serviço_Impressora SHALL restaurar a Impressora para um status monitorável.
7. WHEN uma operação de movimentação ou de ciclo de vida é concluída, THE Serviço_Auditoria SHALL registrar o evento no AuditLog.

### Requirement 11: Contadores de impressora

**User Story:** Como usuário financeiro, quero manter o histórico de contadores das impressoras com integridade, para que a base de faturamento futura seja confiável.

#### Acceptance Criteria

1. THE Serviço_Contador SHALL armazenar por PrinterCounter os campos: identificador da Impressora, timestamp, tipo de contador, valor, origem, identificador do Windows_Client e identificador da Coleta.
2. THE Serviço_Contador SHALL restringir a origem de um PrinterCounter aos valores automático, manual e API.
3. THE Serviço_Contador SHALL suportar os tipos de contador P&B, colorido, A3, A4 e digitalização, e permitir tipos de contador adicionais configuráveis.
4. THE Serviço_Contador SHALL persistir os PrinterCounters de forma somente-adição, mantendo o histórico completo.
5. IF um PrinterCounter recebido para uma Impressora e tipo de contador possui valor inferior ao último valor registrado para a mesma Impressora e tipo, THEN THE Serviço_Contador SHALL rejeitar a leitura como decréscimo inválido.
6. WHEN um Usuario autorizado registra um ajuste administrativo de contador que reduz um valor, THE Serviço_Contador SHALL persistir o ajuste e registrar o evento no AuditLog com o ator, o valor anterior, o novo valor e a justificativa.
7. WHEN um Usuario autorizado consulta o histórico de contadores de uma Impressora, THE Serviço_Contador SHALL retornar os registros por meio de Paginacao_por_Cursor restritos ao tenant_id do contexto autenticado.

### Requirement 12: Registro de coletas e tratamento de falhas

**User Story:** Como operador da plataforma, quero que cada coleta seja registrada com seu resultado e erros, para que falhas sejam rastreadas e reprocessadas.

#### Acceptance Criteria

1. THE Serviço_Ingestao SHALL armazenar por Coleta os campos: horário de início, horário de fim, Windows_Client, Impressora, resultado, erros, dados coletados e timestamp.
2. WHEN uma Coleta é recebida com resultado de sucesso, THE Serviço_Processamento SHALL persistir os dados coletados como PrinterCounters e atualização de status da Impressora correspondente.
3. IF uma Coleta é recebida com resultado de falha, THEN THE Serviço_Ingestao SHALL registrar o erro, incrementar a contagem de tentativas e registrar um PrinterEvent de falha de coleta para consumo posterior.
4. WHEN uma Coleta com falha é elegível a nova tentativa, THE Serviço_Processamento SHALL reprocessá-la segundo uma política de retry.
5. WHEN uma Coleta é concluída, THE Serviço_Ingestao SHALL atualizar o horário da última coleta do Windows_Client correspondente.

### Requirement 13: Idempotência da ingestão

**User Story:** Como operador da plataforma, quero que o reenvio de uma mesma coleta não duplique dados, para que a resiliência offline do agente não corrompa a base.

#### Acceptance Criteria

1. THE Serviço_Ingestao SHALL exigir uma Chave_de_Idempotencia única em cada submissão de Coleta aos endpoints `POST /api/v1/client/collect` e `POST /api/v1/client/upload`.
2. WHEN uma Coleta é recebida com uma Chave_de_Idempotencia ainda não processada, THE Serviço_Ingestao SHALL aceitar a submissão e enfileirá-la para processamento.
3. IF uma Coleta é recebida com uma Chave_de_Idempotencia já processada, THEN THE Serviço_Ingestao SHALL reconhecer a submissão como duplicada e não criar registros adicionais de PrinterCounter ou Coleta.
4. WHEN uma submissão duplicada é reconhecida, THE Serviço_Ingestao SHALL retornar uma resposta de sucesso equivalente à da submissão original.

### Requirement 14: Pipeline de processamento assíncrono

**User Story:** Como operador da plataforma, quero que a ingestão de coletas seja processada de forma assíncrona, para que o agente não seja bloqueado e a plataforma escale para grandes volumes.

#### Acceptance Criteria

1. WHEN o Serviço_Ingestao aceita uma submissão de Coleta válida, THE Serviço_Ingestao SHALL enfileirá-la para processamento assíncrono e retornar ao Windows_Client sem aguardar a persistência final.
2. WHILE existem itens enfileirados, THE Serviço_Processamento SHALL consumir os itens da fila e persistir os PrinterCounters, o status de Impressora e os PrinterEvents resultantes.
3. IF o processamento de um item enfileirado falha, THEN THE Serviço_Processamento SHALL aplicar nova tentativa segundo uma política de retry sem descartar o item antes de esgotar as tentativas configuradas.
4. WHEN um item enfileirado é processado com sucesso, THE Serviço_Processamento SHALL persistir o resultado em uma operação atômica que preserve a idempotência definida no Requirement 13.

### Requirement 15: Configuração operacional do agente

**User Story:** Como administrador do Tenant, quero que o agente obtenha sua configuração do backend, para que o comportamento de descoberta e coleta seja gerenciado centralmente.

#### Acceptance Criteria

1. WHEN um Windows_Client autenticado requisita `GET /api/v1/client/config`, THE Serviço_Configuracao_Cliente SHALL retornar a configuração operacional vigente correspondente ao Windows_Client, restrita ao seu tenant_id e Local.
2. THE Serviço_Configuracao_Cliente SHALL incluir na configuração os parâmetros de intervalo de coleta, alvos de descoberta e impressoras ignoradas.
3. WHEN o Windows_Client recebe uma nova configuração, THE Windows_Client SHALL aplicar os parâmetros recebidos aos ciclos subsequentes de descoberta e coleta.
4. IF a obtenção da configuração falha, THEN THE Windows_Client SHALL continuar operando com a última configuração válida conhecida.

### Requirement 16: Auto-atualização segura do agente

**User Story:** Como responsável de TI, quero que o agente se atualize automaticamente de forma segura, para que correções e melhorias sejam distribuídas sem intervenção e sem risco de pacotes adulterados.

#### Acceptance Criteria

1. WHEN um Windows_Client autenticado requisita `GET /api/v1/client/update`, THE Serviço_Atualizacao_Cliente SHALL retornar os metadados da versão disponível, incluindo versão, localização do pacote e valores de integridade (hash e assinatura).
2. WHEN o Update_Service obtém um pacote de atualização, THE Update_Service SHALL validar a assinatura e o hash do pacote antes de aplicá-lo.
3. IF a validação da assinatura ou do hash do pacote de atualização falha, THEN THE Update_Service SHALL recusar a aplicação da atualização e manter a versão em execução.
4. IF a aplicação de uma atualização validada resulta em falha de inicialização do Windows_Client, THEN THE Update_Service SHALL reverter (rollback) para a versão anterior em execução.
5. WHEN uma atualização é aplicada com sucesso, THE Windows_Client SHALL passar a reportar a nova versão do agente nos heartbeats subsequentes.

### Requirement 17: Autorização, escala e segurança dos endpoints do cliente

**User Story:** Como responsável de segurança, quero que os endpoints do cliente e as novas operações de negócio respeitem RBAC, rate limiting e escala, para que a Fase 2 mantenha as garantias não funcionais da Fase 1.

#### Acceptance Criteria

1. THE Serviço_Autorizacao SHALL prover as permissões granulares `printer.view`, `printer.create`, `printer.edit`, `printer.move`, `printer.monitor`, `counter.view`, `counter.adjust`, `client.view` e `client.manage`.
2. WHEN um Usuario autenticado requisita uma operação de Impressora, contador ou Windows_Client, THE Serviço_Autorizacao SHALL autorizar a operação somente se algum Papel do Usuario possuir a Permissao correspondente.
3. THE Serviço_RateLimit SHALL aplicar limites de taxa de requisições aos endpoints do cliente por Windows_Client e por endpoint.
4. IF um limite de taxa configurado é excedido em um endpoint do cliente, THEN THE Serviço_RateLimit SHALL recusar a requisição e retornar o código de status HTTP 429.
5. THE Sistema SHALL expor as operações do cliente e de negócio da Fase 2 por meio de DTOs distintos das entidades de persistência.
6. WHEN uma requisição a um endpoint da Fase 2 contém entrada inválida em relação ao DTO esperado, THE Sistema SHALL rejeitar a requisição e retornar o código de status HTTP 400 com detalhes de validação.
7. THE Sistema SHALL definir índices de banco de dados sobre tenant_id e sobre os campos de filtro, ordenação e cursor das entidades Windows_Client, Impressora, PrinterCounter, PrinterEvent e Coleta.
