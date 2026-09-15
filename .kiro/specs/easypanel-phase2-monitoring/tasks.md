# Implementation Plan — EasyPanel (FASE 2: Monitoramento)

## Overview

Este plano implementa a Fase 2 de forma incremental e testável, construindo sobre a fundação da Fase 1 (isolamento multi-tenant, Identity/JWT/RBAC, AuditLog, rate limiting, ProblemDetails, `Result`/`PagedResult`, Docker/Redis/MinIO). Cada tarefa mantém o projeto compilável e é validada por testes antes de avançar. A ordem respeita as dependências: contratos/entidades → persistência → autenticação do cliente → registro/heartbeat → ingestão/idempotência/pipeline → impressoras/movimentação/contadores → config/update → agente Windows → segurança/escala/documentação. Requisitos em `requirements.md` e design em `design.md`.

## Task Dependency Graph

As "waves" agrupam tarefas executáveis em paralelo, respeitando as dependências.

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1"], "description": "Projeto do módulo Monitoring e entidades/contratos" },
    { "wave": 2, "tasks": ["1.2", "1.3"], "description": "Configurações EF + migração; DbSets", "dependsOn": ["1.1"] },
    { "wave": 3, "tasks": ["2.1", "2.2"], "description": "Autenticação própria do cliente (esquema + serviço)", "dependsOn": ["1.2"] },
    { "wave": 4, "tasks": ["3.1", "3.2"], "description": "Registro e heartbeat do agente", "dependsOn": ["2.1", "2.2"] },
    { "wave": 5, "tasks": ["4.1", "4.2", "4.3"], "description": "Ingestão, idempotência e pipeline assíncrono", "dependsOn": ["3.1"] },
    { "wave": 6, "tasks": ["5.1", "5.2"], "description": "Impressoras: cadastro/consulta e movimentação", "dependsOn": ["1.2"] },
    { "wave": 7, "tasks": ["6.1"], "description": "Contadores (não-decréscimo, ajuste, cursor)", "dependsOn": ["5.1", "4.2"] },
    { "wave": 8, "tasks": ["7.1", "7.2"], "description": "Config e auto-update do cliente (backend)", "dependsOn": ["2.1"] },
    { "wave": 9, "tasks": ["8.1", "8.2", "8.3", "8.4"], "description": "Windows Client: host, Guardian, comunicação/fila, SNMP/drivers, update", "dependsOn": ["3.1", "4.1", "7.1"] },
    { "wave": 10, "tasks": ["9.1", "9.2", "9.3"], "description": "RBAC/rate limiting/escala, testes de segurança e documentação", "dependsOn": ["5.1", "6.1", "8.1"] }
  ]
}
```

## Tasks

- [x] 1. Módulo de Monitoramento: entidades, contratos e persistência
- [x] 1.1 Criar o projeto `EasyPanel.Modules.Monitoring` e as entidades/contratos
  - Criar o projeto e referências (Shared.Kernel); registrar na solução e na regra de dependência
  - Criar entidades: `WindowsClient : TenantEntity` (UniqueId, CustomerId, LocationId, Hostname, AgentVersion, State, LastHeartbeatAt, LastCollectionAt, SecretHash), `Printer : TenantEntity` (campos de R9.1 + enum `PrinterStatus`), `PrinterCounter`, `PrinterEvent`, `Collection`, `PrinterMovement`
  - Criar enums (`WindowsClientState`, `PrinterStatus`, `CounterSource`, `CounterType` configurável, `PrinterEventType`, `MovementOperation`, `CollectionResult`)
  - Criar DTOs de domínio + interfaces de serviço (I*Service) e a abstração `IPrinterDriver`
  - _Requirements: R3.7, R8.2, R9.1, R9.2, R11.1, R11.2, R11.3, R12.1_

- [x] 1.2 Configurar persistência (EF Core) das entidades de monitoramento
  - Criar `IEntityTypeConfiguration<>` para cada entidade na Infrastructure, com índices de R17.7 (incluindo `UNIQUE (TenantId, IdempotencyKey)` e chave de cursor `(TenantId, PrinterId, CounterType, Timestamp)`)
  - Adicionar os `DbSet<>` ao `AppDbContext`; garantir que as `TenantEntity` recebam o filtro global e o interceptor de escrita
  - Criar a migração `AddMonitoring` e validar ausência de pending model changes
  - _Requirements: R6.4, R9.1, R11.4, R13.1, R17.7_

- [x] 1.3 Criar tabela de deduplicação de ingestão
  - Criar entidade/config `IngestionDedup (TenantId, IdempotencyKey)` com índice único e a migração correspondente
  - Escrever unit tests das convenções de mapeamento (PK Guid, enums como int, timestamps UTC)
  - _Requirements: R13.1, R13.3_

- [x] 2. Autenticação própria do agente Windows
- [x] 2.1 Implementar o esquema de autenticação do cliente (ClientBearer)
  - Criar `IClientContext`/`ClientContext` scoped (ClientId, TenantId, LocationId) e o middleware/handler de autenticação do cliente
  - Emitir/validar token de cliente reutilizando `ITokenService` com audience dedicado (`easypanel:client`), ≤ 15 min
  - Resolver tenant/local sempre da identidade do cliente; acesso cross-tenant/local → 404
  - Escrever unit tests da resolução de tenant/local e da rejeição cross-tenant
  - _Requirements: R4.1, R4.2, R4.3, R4.6_

- [x] 2.2 Implementar `IClientAuthService` (autenticação e renovação)
  - Validar credenciais próprias (secret hasheado) e emitir par de tokens; renovar via credencial de renovação
  - Rejeitar credenciais/tokens inválidos/expirados → 401
  - Escrever integration tests de auth e refresh do cliente
  - _Requirements: R4.1, R4.4, R4.5_

- [x] 3. Registro e heartbeat do agente
- [x] 3.1 Implementar registro do cliente (`POST /api/v1/client/register`)
  - Validar a credencial de registro (chave de provisionamento do Local), criar o `WindowsClient` vinculado a Tenant/Local, emitir credenciais próprias
  - Permitir múltiplos clients por Cliente; auditar o registro no AuditLog
  - Rejeitar credencial de registro inválida/expirada → 401
  - Escrever integration tests: registro válido, múltiplos clients, credencial inválida → 401, auditoria
  - _Requirements: R3.2, R3.3, R3.4, R3.5, R3.6, R3.7_

- [x] 3.2 Implementar heartbeat e detecção de ausência
  - `POST /api/v1/client/heartbeat`: atualizar LastHeartbeatAt/State a partir do payload (versão, hostname, timestamp, estado, nº de impressoras, última coleta)
  - Criar `HeartbeatMonitor` (worker periódico) que marca `HeartbeatMissing` após limite configurável e gera `PrinterEvent` de heartbeat ausente; restaura `Active` ao retornar
  - Escrever integration tests: heartbeat atualiza estado; ausência gera evento; retorno restaura estado
  - _Requirements: R6.1, R6.2, R6.3, R6.4, R6.5_

- [x] 4. Ingestão, idempotência e pipeline assíncrono
- [x] 4.1 Implementar ingestão idempotente (`collect`/`upload`)
  - `POST /api/v1/client/collect` e `/upload`: exigir `IdempotencyKey`; deduplicar via `IngestionDedup`; enfileirar e retornar ack sem aguardar persistência
  - Chave duplicada → resposta de sucesso equivalente sem recriar dados
  - Escrever integration tests: submissão nova enfileira; reenvio idempotente não duplica; ack equivalente
  - _Requirements: R13.1, R13.2, R13.3, R13.4, R14.1_

- [x] 4.2 Implementar o worker de processamento (Hangfire/Redis)
  - Criar o projeto/host `EasyPanel.Workers` (Hangfire sobre Redis) e o `CollectionProcessingWorker`
  - Consumir a fila e persistir `PrinterCounter`s + status de `Printer` + `PrinterEvent`s em operação atômica idempotente; retry com backoff sem descarte prematuro
  - Escrever integration tests do processamento e do retry
  - _Requirements: R14.2, R14.3, R14.4_

- [x] 4.3 Registrar coletas e tratar falhas
  - Persistir `Collection` (início, fim, client, impressora, resultado, erros, dados, timestamp); atualizar última coleta do client
  - Falha de coleta → registrar erro, incrementar tentativas, gerar `PrinterEvent` de falha para consumo posterior; reprocessar elegíveis
  - Escrever integration tests: sucesso persiste contadores; falha registra evento e reprocessa
  - _Requirements: R12.1, R12.2, R12.3, R12.4, R12.5_

- [x] 5. Impressoras: cadastro, consulta e movimentação
- [x] 5.1 Implementar `IPrinterService` e endpoints de parque
  - CRUD + consulta com filtros/busca/ordenação/paginação, exportação e ações em lote, tudo restrito ao tenant
  - Enum `PrinterStatus` (online/offline/desconhecido/desabilitado/sem-comunicação); auditar criação/edição
  - `PrintersController` com permissões `printer.*`; DTOs + validadores
  - Escrever integration tests: CRUD, isolamento (cross-tenant → 404), paginação, ação em lote restrita ao tenant
  - _Requirements: R9.1, R9.2, R9.3, R9.4, R9.5, R9.6, R9.7, R9.8_

- [x] 5.2 Implementar movimentação e ciclo de vida com histórico
  - Operações instalar/transferir/recolher/desabilitar/reativar; `PrinterMovement` append-only (origem/destino/ator/horário)
  - Transferência atualiza Local vigente preservando histórico; desabilitar suspende coleta; reativar restaura; auditar
  - Escrever integration tests: cada operação registra histórico; histórico não é apagável; transferência preserva entradas
  - _Requirements: R10.1, R10.2, R10.3, R10.4, R10.5, R10.6, R10.7_

- [x] 6. Contadores
- [x] 6.1 Implementar `ICounterService` (histórico, não-decréscimo, ajuste, cursor)
  - Persistir `PrinterCounter` append-only; validar não-decréscimo por (impressora, tipo); origem automático/manual/API; tipos configuráveis
  - Ajuste administrativo que reduz valor: permitir com auditoria (ator, valor anterior/novo, justificativa)
  - Consulta de histórico por cursor pagination restrita ao tenant
  - Escrever unit/integration tests: rejeição de decréscimo, ajuste auditado, cursor pagination, isolamento
  - _Requirements: R11.4, R11.5, R11.6, R11.7_

- [x] 7. Configuração e atualização do agente (backend)
- [x] 7.1 Implementar `GET /api/v1/client/config`
  - Retornar configuração vigente do client (intervalo de coleta, alvos de descoberta, impressoras ignoradas), restrita a tenant/local
  - Escrever integration tests: config retornada por identidade do client; isolamento
  - _Requirements: R15.1, R15.2_

- [x] 7.2 Implementar `GET /api/v1/client/update`
  - Retornar metadados da versão disponível (versão, localização no MinIO, hash e assinatura)
  - Escrever integration tests dos metadados de atualização
  - _Requirements: R16.1_

- [x] 8. Windows Client (agente)
- [x] 8.1 Criar o host do agente (Worker Service) com Guardian e ciclo de vida
  - Criar `EasyPanel.WindowsClient` (.NET 10 Worker Service): instância única (mutex), instalação silenciosa/GPO, auto-recovery, logs/config locais, diagnóstico
  - Organizar serviços internos (Net/Communication/Update/Guardian); Guardian supervisiona e reinicia serviços parados, registrando o reinício
  - Escrever unit tests do Guardian (detecção/reinício) e do lock de instância única
  - _Requirements: R1.1–R1.8, R2.1, R2.2, R2.3, R2.4_

- [x] 8.2 Implementar comunicação segura e fila local (offline)
  - HTTPS de saída com validação de certificado TLS; abortar e logar em falha de validação
  - `LocalQueue` (SQLite): persistir coletas quando offline; reenviar com backoff ao restabelecer; nunca logar credenciais/tokens/SNMP
  - Escrever unit tests da fila local (persistência offline + reenvio) e da redaction de segredos em logs
  - _Requirements: R5.1, R5.2, R5.3, R5.4, R5.5, R5.6, R5.7_

- [x] 8.3 Implementar descoberta e coleta SNMP com drivers por fabricante
  - `NetMonitoringService`: descoberta por IP/faixa/subnet/broadcast/SNMP/USB; testar/selecionar/ignorar candidatas
  - `ISnmpCollector` (v1/v2c) + `IPrinterDriver` por fabricante (HP, Canon, Epson, Brother, Kyocera, Xerox, Ricoh, Konica, Lexmark); núcleo agnóstico; preparado para v3
  - Reportar candidatas/coletas ao backend associadas a tenant/local pela identidade do client
  - Escrever unit tests da seleção de driver por probe e da normalização de leitura
  - _Requirements: R7.1–R7.6, R8.1, R8.2, R8.3, R8.4, R8.5, R8.6, R8.7_

- [x] 8.4 Implementar auto-update assinado com rollback
  - `Update_Service`: obter pacote, validar assinatura + hash antes de aplicar; recusar pacote inválido mantendo a versão atual
  - Aplicar, reiniciar e verificar saúde; em falha de inicialização, rollback para a versão anterior; reportar nova versão nos heartbeats
  - Escrever unit tests: validação de assinatura/hash, recusa de pacote adulterado, rollback simulado
  - _Requirements: R16.2, R16.3, R16.4, R16.5_

- [x] 9. Segurança, escala e fechamento
- [x] 9.1 Estender RBAC e rate limiting para a Fase 2
  - Adicionar permissões `printer.view/create/edit/move/monitor`, `counter.view/adjust`, `client.view/manage` ao catálogo e ao seed de papéis
  - Aplicar rate limiting aos endpoints do cliente por Windows_Client e por endpoint (429 ao exceder); DTOs distintos e validação → 400
  - Escrever tests: autorização por permissão nas novas operações; 429 no endpoint do cliente
  - _Requirements: R17.1, R17.2, R17.3, R17.4, R17.5, R17.6_

- [x] 9.2 Escrever suíte de testes de isolamento multi-tenant da Fase 2
  - Cliente/usuário do Tenant A não acessa impressoras/coletas/contadores do Tenant B (→404); tentativa auditada
  - Idempotência sob concorrência; não-decréscimo de contador; ausência de segredos/SNMP em logs
  - _Requirements: R4.6, R9.4, R11.5, R13, R17.2_

- [x] 9.3 Validar build, testes e documentação da Fase 2
  - Rodar build e a suíte completa (unit + integration + security) com Testcontainers; corrigir falhas; validar migrações
  - Atualizar/estender `ARCHITECTURE.md`, `DATABASE.md`, `API.md`, `SECURITY.md`, `DEPLOYMENT.md` e `CLIENT.md` (novo — instalação/operação do agente)
  - _Requirements: R17.7_

## Notes

- Cada tarefa mantém o projeto compilável e é validada por testes antes de avançar. Nenhuma fase avança se a atual estiver quebrada.
- Testes de integração usam `WebApplicationFactory` + Testcontainers/SQLite, reutilizando os padrões da Fase 1.
- Itens arquitetados mas não implementados na Fase 2 (sinalizados no design): SNMP v3 (só extensibilidade), motor de alertas/notificações (heartbeat ausente e falha de coleta são gravados como PrinterEvent para a Fase 3), UI React.
- Decisão de implementação (4.2): a fila de processamento de coletas foi implementada como worker de polling sobre o próprio PostgreSQL (`CollectionProcessingWorker` + `ICollectionProcessor`), mantendo um único deployable. A abstração `ICollectionProcessor` permite trocar por Hangfire/Redis sem alterar o pipeline. Idempotência garantida por `IngestionDedup` + índice único em `Collection(TenantId, IdempotencyKey)`.
- O Windows Client é uma solução-satélite independente, consumindo apenas a API HTTP; distribuição/versionamento próprios.
- Sem dados fake: seeds limitam-se a permissões/papéis e credenciais de provisionamento necessárias ao registro.
