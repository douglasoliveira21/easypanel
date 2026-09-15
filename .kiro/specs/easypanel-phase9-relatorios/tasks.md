# Implementation Plan — EasyPanel (FASE 9: Relatórios e Dashboards)

## Overview

Constrói sobre as Fases 2, 3, 5, 6 e 8 (Contadores, Alertas, Estoque,
Chamados/SLA, Faturamento), concluídas e verificadas, sem reabrir nenhuma
delas — apenas consulta os dados existentes. **Não há migração nesta fase**
(nenhuma entidade nova persistida) — confirmado no `design.md` que os
índices já existentes cobrem os agrupamentos necessários. Cada tarefa
mantém a solução compilável com **zero warnings** e todos os testes verdes
antes de ser marcada `[x]`.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2"], "description": "Domínio: módulo Reporting (DTOs, interfaces) e catálogo de permissões" },
    { "wave": 2, "tasks": ["2.1", "2.2", "2.3", "2.4"], "description": "Serviços de agregação: painel e os três relatórios" },
    { "wave": 3, "tasks": ["3.1", "3.2"], "description": "Endpoints da API e exportação CSV" },
    { "wave": 4, "tasks": ["4.1"], "description": "Segurança consolidada" },
    { "wave": 5, "tasks": ["5.1", "5.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Domínio e permissões
- [x] 1.1 Módulo `EasyPanel.Modules.Reporting`
  - Novo projeto `src/EasyPanel.Modules.Reporting` (referenciando apenas
    `EasyPanel.Shared.Kernel`), adicionado a `EasyPanel.sln`
  - Enums `ReportingCounterType` (espelha `Monitoring.CounterType`) e
    `ReportingInvoiceStatus` (espelha `Billing.InvoiceStatus`), sem
    referência de módulo
  - DTOs (`DashboardOverviewDto`, `PrintConsumptionRowDto`/
    `PrintConsumptionReportDto`, `BillingReportRowDto`/`BillingReportDto`,
    `SlaComplianceBreakdownDto`/`SlaReportDto`), queries
    (`PrintConsumptionReportQuery`, `BillingReportQuery`,
    `SlaReportQuery`) e `IDashboardService`/`IPrintConsumptionReportService`/
    `IBillingReportService`/`ISlaReportService` (interfaces)
  - `ReportingErrors` (padrão de `BillingErrors`/`ContractErrors`):
    `InvalidDateRange`, `DateRangeTooLarge`
  - _Requirements: R1.1, R2.1, R3.1, R4.1_
- [x] 1.2 Catálogo e mapeamento de permissões
  - `relatorio.view` em `Permissions.All`
  - Mapear em `RolePermissions`: `Administrador`/`Financeiro`/`Supervisor`
    → `relatorio.view`; demais papéis → nenhuma
  - Teste existente de consistência catálogo↔mapa continua verde
  - _Requirements: R6.1, R6.2_

- [x] 2. Serviços de agregação
- [x] 2.1 `DashboardService`
  - `GetOverviewAsync`: contagem de `Printer` por `Status`; contagem de
    `Alert` por `State` (todos) e por `Severity` (só `State != Resolved`);
    contagem de `Ticket` por `Status`; `LowStockItemCount` via
    `IInventoryMovementService.ListBelowMinimumAsync` (usar `TotalCount`,
    sem paginar tudo); contagem de `Invoice` por `Status` e soma de
    `TotalAmount` em Rascunho+Emitida
  - Todas as chaves do enum aparecem no dicionário, mesmo com zero (R1.3)
  - Testes de integração: tenant vazio retorna zeros em todas as chaves;
    contagens corretas com dados variados; isolamento cross-tenant
  - _Requirements: R1.1–R1.3_
- [x] 2.2 `PrintConsumptionReportService`
  - `GetAsync`: valida `EndDate >= StartDate` e intervalo ≤ 366 dias;
    reimplementa a técnica de cálculo de consumo da Fase 8 (leitura de fim
    menos leitura de início do intervalo, por Impressora/CounterType, nunca
    negativa); filtro opcional por Cliente/Local; agrupa por Cliente/Local
  - Testes de integração: cálculo correto com/sem leitura anterior ao
    início do intervalo; filtro por Cliente/Local; `EndDate < StartDate` →
    400; intervalo > 366 dias → 400; isolamento cross-tenant
  - _Requirements: R2.1–R2.3, R6.5_
- [x] 2.3 `BillingReportService`
  - `GetAsync`: valida datas (mesmas regras de 2.2); filtra `Invoice` cujo
    período se sobrepõe ao intervalo solicitado (por `PeriodStartTicks`/
    `PeriodEndTicks`); filtro opcional por `Status`; agrupa por Cliente;
    calcula `GrandTotal`
  - Testes de integração: sobreposição parcial de período é incluída;
    filtro por status; total calculado corretamente; isolamento
    cross-tenant
  - _Requirements: R3.1–R3.3, R6.5_
- [x] 2.4 `SlaReportService`
  - `GetAsync`: valida datas (mesmas regras de 2.2); filtra `Ticket` por
    `CreatedAtTicks` no intervalo; agrega `FirstResponseCompliance`/
    `ResolutionCompliance` em Cumprido/Violado/Pendente; calcula taxa de
    cumprimento (denominador exclui Pendente)
  - Testes de integração: agregação correta; intervalo sem chamados
    retorna zeros (R4.3); isolamento cross-tenant
  - _Requirements: R4.1–R4.3, R6.5_

- [x] 3. Endpoints da API e exportação CSV
- [x] 3.1 Controllers de consulta
  - `GET api/v1/reports/dashboard` (`relatorio.view`)
  - `GET api/v1/reports/print-consumption`, `GET api/v1/reports/billing`,
    `GET api/v1/reports/sla` (`relatorio.view`)
  - _Requirements: R1.1, R2.1–R2.3, R3.1–R3.3, R4.1–R4.3, R6.3_
- [x] 3.2 Exportação CSV
  - Helper `ReportCsvWriter` em `EasyPanel.Api` (escreve CSV UTF-8 a partir
    de cabeçalhos + linhas, com escaping de vírgula/aspas/quebra de linha)
  - `GET api/v1/reports/print-consumption/export`, `GET api/v1/reports/
    billing/export`, `GET api/v1/reports/sla/export` (`relatorio.view`,
    mesmos parâmetros dos endpoints de consulta, `text/csv`)
  - Verificação de que o CSV contém as mesmas linhas/filtros da consulta
    agregada correspondente (R5.1) e codificação UTF-8 (R5.2) consolidada
    na suíte de segurança da Task 4.1, via HTTP real, em vez de uma suíte
    de integração separada (evita duplicar o setup de `WebApplicationFactory`)
  - _Requirements: R5.1, R5.2_

- [x] 4. Segurança consolidada
- [x] 4.1 Testes de segurança
  - `ReportingIsolationTests` (7 casos) em `EasyPanel.SecurityTests`:
    painel/relatório de SLA de um tenant não vazam dados de outro
    (contagens/totais não incluem o outro tenant); exportação CSV do
    relatório de SLA verificada como fiel à consulta JSON correspondente e
    com `Content-Type: text/csv` em UTF-8 (R5.1/R5.2, consolidado aqui em
    vez de suíte de integração separada — ver Task 3.2); RBAC nega
    `relatorio.view` a Operacional (painel) e Técnico (relatório de
    faturamento); caminho positivo confirma que Financeiro acessa o painel
    e que um Supervisor (criado sob demanda via `POST /api/v1/users`, não
    seedado por padrão na fábrica compartilhada) acessa o relatório de
    consumo de impressão
  - _Requirements: R6.2, R6.3_

- [x] 5. Documentação e fechamento de fase
- [x] 5.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (novo módulo `Modules.Reporting`, sem
    entidades/migração), `docs/API.md` (novos endpoints de consulta e
    exportação), `docs/SECURITY.md` (nova permissão, decisão de não
    auditar leituras), `README.md`, `HANDOFF.md` (Fase 9 concluída,
    próxima fase, decisão de não introduzir cache Redis)
  - _Requirements: cobertura documental_
- [x] 5.2 Verificação final da fase
  - `dotnet build`/`dotnet test` de `EasyPanel.sln`: 0 avisos, 148 unit + 59
    security + 356 integration — 100% verde
  - `EasyPanel.WindowsClient.slnx` (Agente Windows, não tocado nesta fase):
    build confirmado com 0 avisos, baseline inalterado
  - `dotnet ef migrations has-pending-model-changes`: limpo (confirmado
    continuar limpo mesmo sem migração nova, já que nenhuma entidade foi
    adicionada nesta fase)
  - _Requirements: não-funcionais herdados_

## Notes

- Excel/PDF, relatórios agendados, construtor ad-hoc, dashboards
  customizáveis, alertas sobre limiares, read models/cache Redis
  permanecem fora de escopo (ver `requirements.md`/`design.md`).
- `EasyPanel.Modules.Reporting` não referencia nenhum outro módulo de
  domínio; toda leitura cruzada acontece em `EasyPanel.Infrastructure.Reporting`.
- Nenhuma operação desta fase é auditada (decisão confirmada no
  `requirements.md` — consultas de leitura, mesmo padrão de qualquer
  listagem `GET` já existente na plataforma).
