# Implementation Plan — EasyPanel (FASE 4: Suprimentos)

## Overview

Constrói sobre as Fases 2 e 3, concluídas e verificadas. Duas frentes avançam em
sequência lógica (backend primeiro, pois o agente depende do contrato de ingestão e
do `ClientConfig` estendidos): domínio/persistência → processamento/alertas →
consulta/API → cliente HTTP do agente → orquestração do agente (`Net_Monitoring_Service`/
`Communication_Service`) → segurança → documentação. Cada tarefa mantém a solução
(backend e Windows Client) compilável com **zero warnings** e todos os testes verdes
antes de ser marcada `[x]`.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2"], "description": "Domínio: entidades de suprimento, novo PrinterEventType, extensão do contrato de ingestão e de ClientConfig" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "description": "Persistência: EF configurations e migração" },
    { "wave": 3, "tasks": ["3.1", "3.2"], "description": "Processamento (CollectionProcessor + detecção de limiar) e ClientConfigService" },
    { "wave": 4, "tasks": ["4.1", "4.2"], "description": "Serviço de consulta/previsão/limiares e permissões" },
    { "wave": 5, "tasks": ["5.1", "5.2"], "description": "Endpoints da API" },
    { "wave": 6, "tasks": ["6.1"], "description": "HttpBackendClient (cliente HTTP real do agente)" },
    { "wave": 7, "tasks": ["7.1", "7.2"], "description": "Net_Monitoring_Service e Communication_Service (agente)" },
    { "wave": 8, "tasks": ["8.1"], "description": "Wiring do agente (Program.cs) e testes de integração de ponta a ponta do agente" },
    { "wave": 9, "tasks": ["9.1"], "description": "Auditoria e segurança consolidadas" },
    { "wave": 10, "tasks": ["10.1", "10.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Domínio e extensão de contratos
- [x] 1.1 Entidades de suprimento e novo tipo de evento (backend)
  - `SupplyReading : TenantEntity` e `SupplyThreshold : TenantEntity` em
    `EasyPanel.Modules.Monitoring` (mesmo módulo da Fase 2, sem novo projeto)
  - `PrinterEventType.SupplyLow = 4` em `Enums.cs`
  - DTOs de domínio (`SupplyReadingDto`, `SupplyLevelDto`, `SupplyThresholdDto`),
    requests (`SetSupplyThresholdRequest`) e `ISupplyService` (interface, sem
    implementação ainda — implementada na tarefa 4.1)
  - `SupplyErrors` (padrão de `MonitoringErrors`)
  - _Requirements: R3.1, R4.1, R4.2_
- [x] 1.2 Extensão do contrato de ingestão e do `ClientConfig`
  - `SubmittedSupply(string Label, int Percent)` em `IngestionContracts.cs`;
    `ClientCollectionSubmission` ganha `IReadOnlyList<SubmittedSupply> Supplies`
  - `SubmittedSupplyDto` e extensão de `ClientCollectionRequest` em
    `ClientIngestionController.cs` (API), mapeando para o record de domínio
  - `MonitoredPrinter(Guid PrinterId, string Ip, string? Protocolo, int? Porta,
    string? Fabricante)`; `ClientConfig` ganha `IReadOnlyList<MonitoredPrinter>
    MonitoredPrinters`; `ClientConfigResponse`/`ClientConfigController` espelham a
    extensão
  - Build compilável (implementações existentes ainda não populam os novos campos —
    tarefas 3.x)
  - _Requirements: R1.2, R2.1, R2.2, R2.3_

- [x] 2. Persistência
- [x] 2.1 EF configurations e `AppDbContext`
  - `SupplyReadingConfiguration`/`SupplyThresholdConfiguration` em
    `Infrastructure/Persistence/Configurations/` (índices: `(TenantId, PrinterId,
    Label, TimestampTicks)` em SupplyReading; `(TenantId, PrinterId, Label)` único
    em SupplyThreshold)
  - `DbSet<SupplyReading>`/`DbSet<SupplyThreshold>` em `AppDbContext`
  - _Requirements: R3.1, R6.6_
- [x] 2.2 Migração `AddSupplies`
  - `dotnet ef migrations add AddSupplies` cobrindo as duas novas tabelas e índices
  - Confirmar `has-pending-model-changes` limpo; build + testes de integração
    (SQLite `EnsureCreated`) continuam verdes
  - _Requirements: R6.6_

- [x] 3. Processamento e configuração do agente
- [x] 3.1 `CollectionProcessor`: persistência de suprimento e detecção de limiar
  - Substituir `DeserializeCounters` por `DeserializePayload` (envelope
    `{Counters, Supplies}`; ausência de `Supplies` no JSON → lista vazia)
  - Em `HandleSuccessAsync`: persistir `SupplyReading` por item; resolver o limiar
    em cascata (Impressora+Rótulo → Tenant+Rótulo → Tenant geral → padrão de
    plataforma, configurável em `MonitoringOptions` ou options próprias); gravar
    `PrinterEvent.SupplyLow` apenas na transição acima→abaixo do limiar
  - Testes de integração: leitura acima do limiar não gera evento; cruzamento
    gera evento uma vez; leituras repetidas abaixo do limiar não duplicam evento;
    voltar acima e cruzar de novo gera novo evento; leitura sem limiar configurado
    usa o padrão de plataforma
  - _Requirements: R2.4, R4.3, R4.4_
- [x] 3.2 `ClientConfigService`: `MonitoredPrinters`
  - Consulta `Printer` do `LocationId` do agente autenticado
    (`MonitoringEnabled = true`, `Status != Disabled`), mapeando para
    `MonitoredPrinter`
  - Teste de integração: agente de um Local só recebe as impressoras daquele Local;
    impressora desabilitada/monitoramento desligado não aparece
  - _Requirements: R1.2_

- [x] 4. Serviço de consulta, previsão e limiares
- [x] 4.1 `SupplyService` (implementação de `ISupplyService`)
  - `GetCurrentLevelsAsync`: última leitura de cada rótulo por impressora, com
    `ForecastDepletionAt` (regressão linear simples sobre as últimas N leituras;
    `null` quando amostra insuficiente ou sem queda observada — R5.2)
  - `ListHistoryAsync`: cursor pagination por `TimestampTicks`, filtro por rótulo
  - `ListThresholdsAsync`/`SetThresholdAsync` (upsert por `PrinterId`+`Label`)/
    `DeleteThresholdAsync`, com auditoria via `IAuditLogger` em create/update/delete
  - Testes de integração: previsão indisponível com < 2 leituras; previsão calculada
    corretamente para uma série sintética conhecida (verificar contra valor
    esperado); upsert de limiar; cursor pagination do histórico; isolamento
    cross-tenant → 404
  - _Requirements: R3.3, R3.4, R4.1, R4.2, R5.1, R5.2, R5.3, R6.3, R6.5_
- [x] 4.2 Catálogo e mapeamento de permissões
  - `supply.view`, `supply.manage` em `Permissions.All`; mapear em
    `RolePermissions` (Administrador: ambas; Operacional/Técnico/Supervisor/
    Financeiro: `supply.view`)
  - Teste existente de consistência catálogo↔mapa continua verde
  - _Requirements: R6.1, R6.2_

- [x] 5. Endpoints da API
- [x] 5.1 `SuppliesController`
  - `GET /api/v1/printers/{printerId}/supplies` (níveis atuais + previsão,
    `supply.view`); `GET /api/v1/printers/{printerId}/supplies/history` (cursor,
    `supply.view`)
  - Testes de integração equivalentes ao padrão de `CountersController`
  - _Requirements: R3.3, R3.4, R6.4_
- [x] 5.2 `SupplyThresholdsController`
  - `GET /api/v1/supply-thresholds` (`supply.view`), `POST /api/v1/supply-thresholds`
    (`supply.manage`), `DELETE /api/v1/supply-thresholds/{id}` (`supply.manage`)
  - _Requirements: R4.1, R4.2, R6.3, R6.4_

- [x] 6. Cliente HTTP do agente
- [x] 6.1 `HttpBackendClient : IBackendClient`
  - `IBackendClient` ganha `GetConfigAsync`; `AgentConfig`/`AgentMonitoredPrinter`
    (modelos locais do agente, sem depender de `Modules.Monitoring`)
  - Gerência de token em memória (`SemaphoreSlim`): autentica via
    `POST /client/token` (client_id/secret de `AgentOptions`), renova via
    `POST /client/token/refresh`, reautentica do zero se a renovação falhar; uma
    tentativa de reautenticação + retry em resposta 401
  - `SubmitCollectionAsync`/`GetConfigAsync` retornam falha (`false`/`null`) em erro
    de rede/5xx, sem lançar; nenhum segredo em log (reusa `SecretRedactor`)
  - Testes unitários com `HttpMessageHandler` fake: autenticação inicial, renovação
    de token antes de expirar, reautenticação após 401, falha de rede não lança
  - _Requirements: R1.2, R1.6_

- [x] 7. Orquestração do agente
- [x] 7.1 `NetMonitoringService : ISupervisedService`
  - Laço interno por `CollectionIntervalSeconds` (da última `AgentConfig`, com
    fallback à última válida em caso de falha de obtenção — R1.2)
  - Por `MonitoredPrinter`: sonda + seleciona driver + coleta (`PrinterDiscoveryService`/
    `ISnmpCollector`) → `DeviceReading`; falha de uma impressora não interrompe as
    demais (R1.5)
  - Mapeia `DeviceReading` → `ClientCollectionRequest` (contadores + `SupplyLevels`
    → `SubmittedSupply`); gera `IdempotencyKey` nova por submissão; enfileira via
    `ILocalQueue` (nunca chama `SubmitCollectionAsync` diretamente)
  - `IsHealthy`/`RestartAsync` (Guardian)
  - Testes: ciclo com 2 impressoras (uma falha, uma sucesso) enfileira 1 item;
    payload enfileirado contém contadores e suprimentos; sem segredo SNMP no
    payload; falha de `GetConfigAsync` mantém a config anterior
  - _Requirements: R1.1, R1.3, R1.5, R1.6, R2.1_
- [x] 7.2 `CommunicationService : ISupervisedService`
  - Envolve `QueueResender.ResendBatchAsync` num laço por `ResendIntervalSeconds`
    (já existente, não alterado); `IsHealthy`/`RestartAsync`
  - Teste: item enfileirado é drenado e confirmado (removido da fila) em um ciclo
  - _Requirements: R1.4_

- [x] 8. Wiring do agente e verificação de ponta a ponta
- [x] 8.1 `Program.cs` e testes de integração do agente
  - Substituir `AddSingleton<IEnumerable<ISupervisedService>>(_ => [])` pelos
    registros reais de `NetMonitoringService`/`CommunicationService`; registrar
    `IBackendClient` → `HttpBackendClient` via `AddHttpClient`
  - Teste de integração do agente ponta a ponta (fake `IBackendClient`/servidor
    HTTP local): um ciclo de `Net_Monitoring_Service` + `Communication_Service`
    resulta em uma submissão de coleta "recebida" com contadores e suprimentos
  - Build + suíte completa do `EasyPanel.WindowsClient.slnx` verdes
  - _Requirements: R1.1–R1.6_

- [x] 9. Auditoria e segurança consolidadas
- [x] 9.1 Revisão de auditoria e testes de segurança
  - Confirmar que criação/alteração/remoção de `SupplyThreshold` audita ator e
    valores antes/depois
  - Casos em `EasyPanel.SecurityTests`: cross-tenant em níveis/histórico/limiares →
    404; RBAC nega `supply.manage` a papel sem a permissão; nenhum dado de
    suprimento de outro tenant aparece em listagens
  - _Requirements: R6.2, R6.3, R6.5_

- [x] 10. Documentação e fechamento de fase
- [x] 10.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (suprimento no fluxo de coleta, `Net_Monitoring_Service`/
    `Communication_Service` deixam de ser lacuna), `docs/DATABASE.md` (novas
    tabelas/índices), `docs/API.md` (novos endpoints), `docs/SECURITY.md` (novas
    permissões), `docs/CLIENT.md` (ciclo de coleta real do agente), `README.md`,
    `HANDOFF.md` (Fase 4 concluída, próxima fase)
  - _Requirements: cobertura documental_
- [x] 10.2 Verificação final da fase
  - `dotnet build`/`dotnet test` de `EasyPanel.sln` (zero warnings, 100% verde) **e**
    de `src/windows-client/EasyPanel.WindowsClient.slnx` (zero warnings, 100% verde)
  - `dotnet ef migrations has-pending-model-changes` limpo
  - _Requirements: não-funcionais herdados_

## Notes

- `POST /client/register` e `POST /client/heartbeat` não entram no
  `HttpBackendClient` desta fase (fora de escopo, ver `design.md`).
- `Update_Service` como `ISupervisedService`, estoque físico de suprimentos e o
  `IPrinterDriver`/`PrinterReading` vestigial em `Modules.Monitoring` permanecem
  fora de escopo.
- Se a tarefa 6.1 revelar que `SecretRedactor` (agente) precisa de ajuste para
  cobrir o novo cliente HTTP, o ajuste é feito ali mesmo, sem nova tarefa.
