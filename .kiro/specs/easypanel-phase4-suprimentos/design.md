# Design Document — EasyPanel (FASE 4: Suprimentos)

## Overview

A Fase 4 tem duas frentes que precisam avançar juntas, porque uma depende da outra
para ser observável de ponta a ponta:

1. **Backend**: persistência de suprimento (`SupplyReading`), limiar configurável
   por (Impressora, rótulo) com fallback por Tenant (`SupplyThreshold`), detecção de
   cruzamento de limiar gravando `PrinterEventType.SupplyLow` (reusando o
   `AlertEngine` da Fase 3 sem alterá-lo), previsão de troca por regressão linear, e
   os endpoints/RBAC correspondentes.
2. **Agente Windows**: as duas peças de orquestração que a Fase 2 previu mas nunca
   implementou — `Net_Monitoring_Service` (ciclo periódico de SNMP → submissão) e
   `Communication_Service` (drenagem da fila local) — mais um `HttpBackendClient`
   real (hoje só existe a interface `IBackendClient`), para que a submissão de
   coleta (agora incluindo suprimento) realmente saia do agente e chegue ao backend.

Sem a frente 2, a frente 1 nunca recebe dado nenhum — por isso ambas estão nesta
mesma fase (decisão já validada com o usuário, ver `requirements.md`).

## Parte A — Backend

### Modelo de dados (novo, em `Modules.Monitoring` — mesmo módulo do restante da
Fase 2, não um módulo novo: suprimento é uma extensão natural de impressora/coleta,
sem o desacoplamento que justificou `Modules.Alerting` ser separado)

#### `SupplyReading : TenantEntity` (somente-adição)

| Campo | Tipo | Notas |
|---|---|---|
| PrinterId | Guid | |
| Label | string | rótulo normalizado (ex.: `toner-preto`) |
| Percent | int | 0–100 |
| Timestamp / TimestampTicks | DateTimeOffset / long | mesmo padrão de `PrinterCounter` |
| WindowsClientId | Guid? | |
| CollectionId | Guid? | |

#### `SupplyThreshold : TenantEntity`

| Campo | Tipo | Notas |
|---|---|---|
| PrinterId | Guid? | null = padrão do Tenant (todas as impressoras) |
| Label | string? | null = aplica a qualquer rótulo não coberto por uma linha mais específica |
| ThresholdPercent | int | 0–100 |

**Resolução em cascata** (do mais específico ao mais genérico), usada tanto na
detecção de cruzamento quanto na consulta de "limiar vigente" exposta na API:

1. `(PrinterId = p, Label = l)` — limiar específico da impressora para aquele rótulo.
2. `(PrinterId = null, Label = l)` — padrão do Tenant para aquele rótulo.
3. `(PrinterId = null, Label = null)` — padrão geral do Tenant.
4. Padrão de plataforma (`SupplyOptions.DefaultThresholdPercent`, configurável, ex.: 10) quando nada foi configurado pelo Tenant.

Um único índice único `(TenantId, PrinterId, Label)` (com `PrinterId`/`Label` nulos
tratados como valor coringa pela aplicação, não pelo banco — Postgres permite
múltiplos NULLs num índice único porque NULL não é igual a NULL) garante no máximo
uma linha por combinação.

### Extensão de `PrinterEvent` — novo tipo de evento

`PrinterEventType.SupplyLow = 4` (próximo valor livre do enum já existente). Nenhuma
mudança de schema além do novo valor (a coluna já é `int`). O `AlertEngine` da Fase
3 já consulta `PrinterEvent` por `(TenantId, Type, OccurredAt)` e por
`CreatedAtTicks` — um `AlertRule` com `EventTypesCsv` contendo `4` passa a casar
automaticamente, sem nenhuma mudança em `Modules.Alerting`/`Infrastructure.Alerting`.

### Extensão do contrato de ingestão

`ClientCollectionSubmission` (domínio) e `ClientCollectionRequest`/`SubmittedCounterDto`
(API) ganham um campo irmão de `Counters`:

```csharp
public sealed record SubmittedSupply(string Label, int Percent);

// ClientCollectionSubmission ganha:
IReadOnlyList<SubmittedSupply> Supplies
```

`IngestionService.SubmitAsync` passa a serializar um **envelope** em
`Collection.CollectedData` em vez do array de contadores solto:

```csharp
private sealed record CollectionPayload(
    IReadOnlyList<SubmittedCounter> Counters,
    IReadOnlyList<SubmittedSupply> Supplies);
```

`CollectionProcessor.DeserializeCounters` é substituído por
`DeserializePayload` (desserializa o envelope; ausência de `Supplies` no JSON — dado
antigo ou agente ainda não atualizado — resolve para lista vazia, `R2.3`).

### Processamento — `CollectionProcessor.HandleSuccessAsync`

Para cada `SubmittedSupply` da coleta bem-sucedida:

1. Persiste um `SupplyReading` (somente-adição, mesmo padrão de `PrinterCounter`).
2. Busca a última leitura **anterior** (antes desta) do mesmo `(PrinterId, Label)`
   (`MaxBy TimestampTicks` abaixo do novo registro) e o limiar vigente resolvido em
   cascata.
3. Se a leitura nova está `<=` limiar e a leitura anterior (quando existe) estava
   `>` limiar (ou não existe leitura anterior — primeira leitura já abaixo do
   limiar também dispara, é a leitura mais alarmante), grava um `PrinterEvent`
   `SupplyLow` com `PrinterId` e `Detail` textual (`"{label}: {percent}% (limiar {threshold}%)"`).
4. Se não existe leitura anterior abaixo do limiar mas a atual está acima, ou se a
   transição já foi capturada, nenhum evento novo é gravado — a regra de transição
   evita reabrir o mesmo alerta a cada leitura (mesma garantia que R2.5 do
   `AlertEngine` já dá no lado do consumo, mas aqui evitamos até *gerar* o evento
   redundante).

### Extensão de `ClientConfig` (agente) com impressoras monitoráveis

```csharp
public sealed record MonitoredPrinter(
    Guid PrinterId, string Ip, string? Protocolo, int? Porta, string? Fabricante);

// ClientConfig ganha:
IReadOnlyList<MonitoredPrinter> MonitoredPrinters
```

`ClientConfigService.GetAsync` passa a consultar `Printer` do Local do agente
autenticado (`WHERE LocationId = ctx.LocationId AND MonitoringEnabled AND Status !=
Disabled`), mapeando para `MonitoredPrinter`. Esta é a lista que o
`Net_Monitoring_Service` do agente usa para saber **o quê** consultar via SNMP e
**sob qual `PrinterId`** reportar — sem isso o agente teria que adivinhar a
correspondência entre IP descoberto e impressora cadastrada.

### Consulta, previsão e limiares — `ISupplyService`

```csharp
public sealed record SupplyLevelDto(
    Guid PrinterId, string Label, int Percent, DateTimeOffset Timestamp,
    DateTimeOffset? ForecastDepletionAt);

public sealed record SupplyThresholdDto(
    Guid Id, Guid? PrinterId, string? Label, int ThresholdPercent);

public interface ISupplyService
{
    Task<Result<IReadOnlyList<SupplyLevelDto>>> GetCurrentLevelsAsync(Guid printerId, CancellationToken ct);
    Task<Result<CursorPage<SupplyReadingDto>>> ListHistoryAsync(Guid printerId, string? label, string? cursor, int pageSize, CancellationToken ct);
    Task<Result<PagedResult<SupplyThresholdDto>>> ListThresholdsAsync(PageRequest page, CancellationToken ct);
    Task<Result<SupplyThresholdDto>> SetThresholdAsync(Guid? printerId, string? label, int thresholdPercent, CancellationToken ct);
    Task<Result> DeleteThresholdAsync(Guid id, CancellationToken ct);
}
```

**Previsão (`ForecastDepletionAt`, R5)**: regressão linear simples sobre as últimas
`N` (configurável, padrão 10) `SupplyReading` do mesmo `(PrinterId, Label)`,
ordenadas por tempo — `x` = tempo em dias desde a primeira leitura da amostra, `y` =
percentual. Calcula a inclinação (`slope`, percentual/dia) pelo método dos mínimos
quadrados. Se `slope >= 0` (sem queda observada) ou menos de duas leituras
distintas, retorna `null` (indisponível — R5.2). Caso contrário,
`ForecastDepletionAt = agora + (percentual_atual / -slope)` dias.

### Endpoints (API)

Convenção idêntica às Fases 1–3: `[RequirePermission]`, `MapFailure(Error)`,
`PagedResult`/cursor conforme volume.

- `GET /api/v1/printers/{printerId}/supplies` — níveis atuais + previsão (`supply.view`)
- `GET /api/v1/printers/{printerId}/supplies/history` — histórico por cursor, filtro `label` (`supply.view`)
- `GET /api/v1/supply-thresholds` — lista limiares configurados do tenant (`supply.view`)
- `POST /api/v1/supply-thresholds` — cria/atualiza um limiar (upsert por `PrinterId`+`Label`) (`supply.manage`)
- `DELETE /api/v1/supply-thresholds/{id}` — remove um limiar específico, voltando à cascata (`supply.manage`)

### Permissões

`supply.view`, `supply.manage` — mapeadas em `RolePermissions` seguindo o padrão já
usado para `counter.*`/`printer.*` (Administrador: ambas; Operacional/Técnico/
Supervisor/Financeiro: `supply.view`; Estoque: nenhuma nesta fase — estoque de
consumíveis físicos é Fase 5).

## Parte B — Agente Windows

### `HttpBackendClient : IBackendClient`

`IBackendClient` ganha um segundo método:

```csharp
public interface IBackendClient
{
    Task<bool> SubmitCollectionAsync(string idempotencyKey, string payloadJson, CancellationToken ct);
    Task<AgentConfig?> GetConfigAsync(CancellationToken ct); // null = falha (R15.4: mantém última config válida)
}

public sealed record AgentMonitoredPrinter(Guid PrinterId, string Ip, string? Protocolo, int? Porta, string? Fabricante);
public sealed record AgentConfig(
    int CollectionIntervalSeconds,
    IReadOnlyList<string> DiscoveryTargets,
    IReadOnlyList<string> IgnoredPrinters,
    IReadOnlyList<AgentMonitoredPrinter> MonitoredPrinters);
```

`HttpBackendClient` usa um `HttpClient` (`BaseUrl = AgentOptions.BackendBaseUrl`) e
gerencia o par de tokens internamente:

- Estado em memória: `AccessToken`, `RefreshToken`, `ExpiresAt` (protegidos por um
  `SemaphoreSlim` — `Net_Monitoring_Service` e `Communication_Service` chamam o
  mesmo cliente concorrentemente).
- `EnsureAuthenticatedAsync`: se não há token ou `ExpiresAt` está a menos de 30s de
  expirar, tenta `POST /client/token/refresh` com o refresh token guardado; se não
  há refresh token ou a renovação falha (401), autentica do zero via
  `POST /client/token` com `AgentOptions.ClientId`/`ClientSecret` (já assumidos como
  provisionados — o fluxo de `POST /client/register` no primeiro uso **não** entra
  nesta fase, é pré-requisito operacional já coberto pela Fase 2).
- Toda chamada de negócio (`GetConfigAsync`, `SubmitCollectionAsync`) chama
  `EnsureAuthenticatedAsync` antes, envia `Authorization: Bearer <token>`; uma
  resposta 401 dispara uma única tentativa de reautenticação + retry da chamada.
- Erros de rede/5xx retornam `false`/`null` (sinal de falha transitória para o
  chamador decidir retry — mesma semântica já documentada em `IBackendClient`).
- Validação de certificado TLS é a padrão do `HttpClient`/.NET (não desabilitada em
  nenhum ponto) — aborta e loga em caso de falha (R5.2/R5.3 da Fase 2, já vigentes).

### `Net_Monitoring_Service` (`NetMonitoringService : ISupervisedService`)

Laço interno próprio (não é `BackgroundService` — é supervisionado pelo `Guardian`,
que já tem seu próprio laço de varredura; `NetMonitoringService` roda seu próprio
timer interno e expõe `IsHealthy`/`RestartAsync` para o Guardian):

1. A cada `CollectionIntervalSeconds` (da última `AgentConfig` obtida, com fallback
   para o valor local quando a obtenção falhar — R15.4 já vigente):
2. Chama `IBackendClient.GetConfigAsync` (falha → mantém a última config em cache).
3. Para cada `MonitoredPrinter` da config: sonda o host (`ISnmpCollector.ProbeAsync`),
   seleciona o driver (`PrinterDriverSelector.Select`), coleta (`ISnmpCollector.QueryAsync`
   + `driver.Interpret`) → `DeviceReading` (contadores + `SupplyLevels`). Falha de
   uma impressora é logada e não interrompe as demais (R1.5).
4. Mapeia `DeviceReading` → `ClientCollectionRequest` (contadores: `CounterKind` →
   string do `CounterType` do backend; suprimento: `SupplyLevels` → lista de
   `SubmittedSupply`; status → `RawStatus`).
5. Gera uma `IdempotencyKey` nova (`Guid.NewGuid()`) por submissão, serializa o
   `ClientCollectionRequest` e `EnqueueAsync` na `ILocalQueue` — nunca chama
   `SubmitCollectionAsync` diretamente; toda saída passa pela fila, unificando o
   caminho com o `Communication_Service` (uma única política de retry/backoff).
6. `IsHealthy` = verdadeiro enquanto o laço interno está rodando (não travado);
   `RestartAsync` reinicia o laço interno se parado.

### `Communication_Service` (`CommunicationService : ISupervisedService`)

Envolve o `QueueResender` já existente (Fase 2, não alterado) num laço próprio:
a cada `ResendIntervalSeconds`, chama `QueueResender.ResendBatchAsync`. `IsHealthy`/
`RestartAsync` no mesmo padrão do `NetMonitoringService`.

### `Program.cs`

Substitui `AddSingleton<IEnumerable<ISupervisedService>>(_ => [])` por registros
reais de `NetMonitoringService` e `CommunicationService` (singletons, ambos
implementam `ISupervisedService`), e registra `IBackendClient` →
`HttpBackendClient` (com `HttpClient` nomeado via `AddHttpClient`). O `Guardian` já
os supervisiona sem mudança — apenas a lista deixa de vir vazia.

## Correctness properties

1. **Sem duplicação de evento de suprimento baixo**: para uma sequência de leituras
   de um `(Impressora, rótulo)` que permanece abaixo do limiar, apenas a primeira
   gera `PrinterEvent.SupplyLow` — leituras subsequentes sem transição não geram
   novos eventos (evita inundar o `AlertEngine`).
2. **Reabertura correta**: se o suprimento volta a subir acima do limiar e cai
   novamente, um novo `PrinterEvent.SupplyLow` é gravado (nova transição).
3. **Cascata de limiar determinística**: a resolução (Impressora+Rótulo → Tenant+Rótulo
   → Tenant geral → plataforma) nunca retorna mais de um valor e sempre retorna algum
   valor (o padrão de plataforma é o piso).
4. **Isolamento multi-tenant**: nenhuma leitura, limiar ou consulta cruza `TenantId`.
5. **Sem segredo em fila/log**: nenhuma submissão enfileirada ou log do agente
   contém credenciais SNMP, `ClientSecret` ou tokens (mesma garantia da Fase 2,
   revalidada porque o `Net_Monitoring_Service`/`HttpBackendClient` são código novo).
6. **Falha parcial não trava o ciclo**: uma impressora que falha ao responder SNMP,
   ou uma submissão que falha ao enviar, não impede as demais impressoras/itens da
   fila de serem processados no mesmo ciclo.

## Notas / fora de escopo

- `POST /client/register` (bootstrap de primeiras credenciais) e `POST
  /client/heartbeat` não são chamados pelo novo `HttpBackendClient` nesta fase —
  não fazem parte dos requisitos aprovados (R1 cobre config+collect). Ambos os
  endpoints já existem no backend desde a Fase 2; adicioná-los ao cliente HTTP é
  uma extensão pequena e isolada para quando forem necessários.
- `Update_Service` como `ISupervisedService` permanece fora de escopo (já
  registrado em `requirements.md`).
- Estoque físico de suprimentos, pedido automático de reposição: Fase 5.
- O `IPrinterDriver`/`PrinterReading` vestigial em `Modules.Monitoring` não é
  tocado; segue sinalizado para decisão futura do usuário.
