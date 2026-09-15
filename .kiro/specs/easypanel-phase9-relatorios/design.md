# Design Document — EasyPanel (FASE 9: Relatórios e Dashboards)

## Overview

A Fase 9 introduz um módulo novo, `EasyPanel.Modules.Reporting`, mas **sem
nenhuma entidade persistida** — diferente de todas as fases anteriores, esta
fase não grava nada novo no banco; apenas consulta e agrega dados que já
existem em `Modules.Monitoring` (`Printer`, `PrinterCounter`),
`Modules.Alerting` (`Alert`), `Modules.Ticketing` (`Ticket`),
`Modules.Inventory` (via `IInventoryMovementService.
ListBelowMinimumAsync`) e `Modules.Billing` (`Invoice`). Por isso **não há
migração nesta fase** — só DTOs, interfaces de serviço e as implementações
de consulta em `Infrastructure.Reporting`.

Escopo: um painel de visão geral (estado corrente), três relatórios
agregados por intervalo de datas (consumo de impressão, faturamento, SLA de
chamados), e exportação em CSV dos três relatórios. Sem cache (Redis não é
introduzido nesta fase — ver `requirements.md`, questão em aberto 2, decidida
por "não introduzir").

## Modelo de dados (`EasyPanel.Modules.Reporting`)

Nenhuma entidade. Apenas DTOs de agregação e um enum espelhado:

```csharp
// Espelha Modules.Monitoring.CounterType (mesmos valores), quarto enum
// espelhado da plataforma (depois de ContractCounterType e
// BillingCounterType), pelo mesmo motivo: Modules.Reporting não referencia
// Modules.Monitoring.
public enum ReportingCounterType { BlackAndWhite = 0, Color = 1, A3 = 2, A4 = 3, Scan = 4, Other = 99 }
```

### Painel (`DashboardOverviewDto`)

```csharp
public sealed record DashboardOverviewDto(
    IReadOnlyDictionary<string, long> PrintersByStatus,
    IReadOnlyDictionary<string, long> AlertsByState,        // todos os Alertas, por State
    IReadOnlyDictionary<string, long> OpenAlertsBySeverity,  // State != Resolved, por Severity
    IReadOnlyDictionary<string, long> TicketsByStatus,
    long LowStockItemCount,
    IReadOnlyDictionary<string, long> InvoicesByStatus,
    decimal InvoicesPendingTotalAmount,                       // soma de TotalAmount em Rascunho+Emitida
    DateTimeOffset GeneratedAt);
```

Dicionários com chave `string` (nome do enum, ex. `"Online"`, `"Aberto"`) —
todas as chaves possíveis do enum aparecem, mesmo com contagem zero (R1.3).

### Relatório de consumo (`PrintConsumptionReportDto`)

```csharp
public sealed record PrintConsumptionReportQuery(
    DateTimeOffset StartDate, DateTimeOffset EndDate, Guid? CustomerId, Guid? LocationId);

public sealed record PrintConsumptionRowDto(
    Guid CustomerId, Guid LocationId, Guid PrinterId, ReportingCounterType CounterType, long Consumed);

public sealed record PrintConsumptionReportDto(
    IReadOnlyList<PrintConsumptionRowDto> Rows, DateTimeOffset StartDate, DateTimeOffset EndDate);
```

**Cálculo (R2.1):** mesma técnica da Fase 8 (`BillingClosingService.
CalculateConsumptionAsync`) — leitura de referência de fim do intervalo
menos leitura de referência de início, por (Impressora, CounterType), nunca
negativa. Reimplementada em `Infrastructure.Reporting` (não extraída para um
utilitário compartilhado nesta fase — duplicação pequena e auto-contida,
preferível a acoplar `Modules.Reporting`/`Modules.Billing` ou criar uma
abstração prematura para dois usos). Impressoras sem nenhuma leitura de
referência disponível são omitidas do resultado (mesmo critério da R2.5 da
Fase 8).

### Relatório de faturamento (`BillingReportDto`)

```csharp
public sealed record BillingReportQuery(DateTimeOffset StartDate, DateTimeOffset EndDate, InvoiceStatus? Status);

public sealed record BillingReportRowDto(Guid CustomerId, long InvoiceCount, decimal TotalAmount);

public sealed record BillingReportDto(IReadOnlyList<BillingReportRowDto> Rows, long TotalInvoiceCount, decimal GrandTotal);
```

**Filtro (R3.1):** Faturas cujo período `[PeriodStartTicks, PeriodEndTicks]`
se sobrepõe ao intervalo solicitado (`PeriodStartTicks <= endTicks &&
PeriodEndTicks >= startTicks`, mesma técnica de sobreposição de vigência já
usada em `ContractScopeService`, Fase 7), agrupado por `CustomerId`.
`InvoiceStatus` é reaproveitado de `Modules.Billing` diretamente no DTO de
consulta (não é uma entidade, é seguro referenciar o enum de outro módulo
aqui como faria qualquer consumidor externo do contrato público de
`Modules.Billing` — mas, para manter `Modules.Reporting` sem dependência de
projeto sobre `Modules.Billing`, o enum é recebido como `int?` na fronteira
do serviço e mapeado internamente; ver nota de implementação abaixo).

### Relatório de SLA (`SlaReportDto`)

```csharp
public sealed record SlaReportQuery(DateTimeOffset StartDate, DateTimeOffset EndDate);

public sealed record SlaComplianceBreakdownDto(long Cumprido, long Violado, long Pendente, decimal ComplianceRate);

public sealed record SlaReportDto(long TotalTickets, SlaComplianceBreakdownDto FirstResponse, SlaComplianceBreakdownDto Resolution);
```

**Cálculo (R4.1/R4.2):** filtra `Ticket` por `CreatedAtTicks` no intervalo;
agrupa `FirstResponseCompliance`/`ResolutionCompliance` (enum já existente
em `Modules.Ticketing`) em Cumprido/Violado/Pendente; `ComplianceRate =
Cumprido / (Cumprido + Violado)` quando o denominador é maior que zero,
senão `0`.

## Serviços de domínio

```csharp
public interface IDashboardService
{
    Task<Result<DashboardOverviewDto>> GetOverviewAsync(CancellationToken ct);
}

public interface IPrintConsumptionReportService
{
    Task<Result<PrintConsumptionReportDto>> GetAsync(PrintConsumptionReportQuery query, CancellationToken ct);
}

public interface IBillingReportService
{
    Task<Result<BillingReportDto>> GetAsync(BillingReportQuery query, CancellationToken ct);
}

public interface ISlaReportService
{
    Task<Result<SlaReportDto>> GetAsync(SlaReportQuery query, CancellationToken ct);
}
```

Todas as implementações (`Infrastructure.Reporting`) validam
`EndDate >= StartDate` (senão `ReportingErrors.InvalidDateRange`, 400) e
`(EndDate - StartDate).TotalDays <= 366` (senão
`ReportingErrors.DateRangeTooLarge`, 400) — R6.5. `IDashboardService` não
tem parâmetros de data (é sempre "agora").

**Nota de implementação (mapeamento de enum entre módulos):** para não criar
uma referência de projeto de `Modules.Reporting` para `Modules.Billing`
apenas por causa de `InvoiceStatus` no filtro do relatório de faturamento, o
DTO de consulta usa o próprio `InvoiceStatus` de `Modules.Billing` — isso é
aceitável porque `IBillingReportService` já vive em `Infrastructure.
Reporting`, que referencia ambos os módulos de qualquer forma (mesmo padrão
de "Infrastructure cruza módulos" usado em todas as fases anteriores); o que
a regra de dependência proíbe é um **módulo de domínio** referenciar outro,
não a Infrastructure. `BillingReportQuery` fica definido em `Modules.
Reporting` mas com o campo tipado como `Modules.Billing.InvoiceStatus?` —
isso obrigaria `Modules.Reporting` a referenciar `Modules.Billing`. Para
evitar isso mantendo o filtro, `Modules.Reporting` define seu próprio enum
`ReportingInvoiceStatus` (quinto enum espelhado, mesmos valores de
`InvoiceStatus`), convertido em `Infrastructure.Reporting`.

## Endpoints da API

Convenção idêntica às fases anteriores: `[RequirePermission]`, `MapFailure`.
Todos exigem `relatorio.view`.

- `GET /api/v1/reports/dashboard`
- `GET /api/v1/reports/print-consumption?startDate=&endDate=&customerId=&locationId=`
- `GET /api/v1/reports/print-consumption/export` (mesmos parâmetros; `text/csv`)
- `GET /api/v1/reports/billing?startDate=&endDate=&status=`
- `GET /api/v1/reports/billing/export`
- `GET /api/v1/reports/sla?startDate=&endDate=`
- `GET /api/v1/reports/sla/export`

**Exportação CSV (R5):** implementada inteiramente na camada `Api` (não nos
serviços de domínio) — um helper `ReportCsvWriter` (`EasyPanel.Api`) recebe
cabeçalhos + linhas já formatadas como `string[]` e escreve CSV UTF-8
(campos com vírgula/aspas/quebra de linha são colocados entre aspas, aspas
internas duplicadas). Cada endpoint `.../export` chama o mesmo serviço de
domínio do endpoint de consulta correspondente e só formata a saída
diferente — nenhuma duplicação de regra de negócio. Resposta com
`Content-Type: text/csv; charset=utf-8` e `Content-Disposition: attachment;
filename=...`.

## Permissões (RBAC)

`relatorio.view` — mapeamento confirmado no `requirements.md`:
- `Administrador`, `Financeiro`, `Supervisor`: `relatorio.view`.
- Demais papéis: nenhuma.

Não há `relatorio.manage` — não existe nada para configurar/mutar neste
módulo.

## Auditoria

**Nenhuma** — decisão confirmada no `requirements.md` (consultas de
leitura, mesmo padrão de qualquer listagem `GET` já existente na
plataforma, nenhuma das quais é auditada).

## Índices

**Nenhum índice novo necessário** — verificado que os índices já existentes
cobrem os agrupamentos desta fase: `Printer(TenantId, Status)` (Fase 2),
`Alert(TenantId, State, ...)` (Fase 3), `Ticket(TenantId, Status,
CreatedAtTicks)` (Fase 6), `Invoice(TenantId, Status)` e `Invoice(TenantId,
PeriodStartTicks, PeriodEndTicks)` (Fase 8), `PrinterCounter(TenantId,
PrinterId, CounterType, TimestampTicks)` (Fase 2). Sem migração nesta fase.

## Correctness properties

1. **Isolamento multi-tenant**: nenhuma consulta de painel/relatório
   retorna ou agrega registro de um `TenantId` diferente do contexto
   autenticado (garantido pelo filtro global de `TenantEntity`, já que
   todas as entidades-fonte são `TenantEntity`).
2. **Painel sempre com todas as chaves**: todo enum relevante (Status de
   Impressora, State/Severity de Alerta, Status de Chamado, Status de
   Fatura) aparece no dicionário correspondente do painel, mesmo com
   contagem zero.
3. **Consumo nunca negativo**: nenhuma linha do relatório de consumo tem
   `Consumed < 0` (mesmo piso em zero da Fase 8).
4. **Exportação fiel à consulta**: o CSV de um relatório contém exatamente
   as mesmas linhas, na mesma ordem, que a consulta agregada correspondente
   com os mesmos filtros.
5. **Intervalo de datas limitado**: nenhuma consulta de relatório processa
   um intervalo maior que 366 dias.

## Notas / fora de escopo

- Exportação em Excel/PDF, relatórios agendados, construtor de relatório
  ad-hoc, dashboards customizáveis, alertas sobre limiares de relatório,
  read models materializados/cache Redis: fora de escopo (ver
  `requirements.md`).
- `EasyPanel.Modules.Reporting` não referencia nenhum outro módulo de
  domínio; toda leitura cruzada (`Printer`, `PrinterCounter`, `Alert`,
  `Ticket`, `Invoice`, `IInventoryMovementService`) acontece em
  `EasyPanel.Infrastructure.Reporting`.
