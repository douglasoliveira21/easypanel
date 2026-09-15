# Implementation Plan — EasyPanel (FASE 8: Fechamento e Faturamento)

## Overview

Constrói sobre a Fase 2 (Impressoras/Contadores) e a Fase 7 (Contratos),
concluídas e verificadas, sem reabrir nenhuma delas — Impressora e Contrato
são referenciados apenas por `Guid`; a resolução de contrato reusa
`IContractService.ResolveApplicableAsync` sem alterá-lo. Cada tarefa mantém
a solução compilável com **zero warnings** e todos os testes verdes antes
de ser marcada `[x]`.

## Task Dependency Graph

```json
{
  "waves": [
    { "wave": 1, "tasks": ["1.1", "1.2"], "description": "Domínio: módulo Billing (entidades, contratos) e catálogo de permissões" },
    { "wave": 2, "tasks": ["2.1", "2.2"], "description": "Persistência: EF configurations e migração" },
    { "wave": 3, "tasks": ["3.1", "3.2"], "description": "Serviços de fechamento e de fatura" },
    { "wave": 4, "tasks": ["4.1", "4.2"], "description": "Endpoints da API" },
    { "wave": 5, "tasks": ["5.1"], "description": "Auditoria e segurança consolidadas" },
    { "wave": 6, "tasks": ["6.1", "6.2"], "description": "Documentação e fechamento de fase" }
  ]
}
```

## Tasks

- [x] 1. Domínio e permissões
- [x] 1.1 Módulo `EasyPanel.Modules.Billing`
  - Novo projeto `src/EasyPanel.Modules.Billing` (referenciando apenas
    `EasyPanel.Shared.Kernel`), adicionado a `EasyPanel.sln`
  - Enums `InvoiceStatus`, `BillingCounterType` (espelha
    `Monitoring.CounterType`/`Contracts.ContractCounterType`, sem
    referência de módulo)
  - Entidades `TenantEntity`: `BillingClosing`, `Invoice`,
    `InvoiceLineItem`
  - DTOs (`BillingClosingDto`, `InvoiceDto`, `InvoiceLineItemDto`), requests
    (`ExecuteBillingClosingRequest`, `ChangeInvoiceStatusRequest`),
    `InvoiceQuery` e `IBillingClosingService`/`IInvoiceService`
    (interfaces)
  - `BillingErrors` (padrão de `ContractErrors`/`TicketingErrors`)
  - _Requirements: R1.1, R4.2, R4.3, R5.1_
- [x] 1.2 Catálogo e mapeamento de permissões
  - `fechamento.view`, `fechamento.manage` em `Permissions.All`
  - Mapear em `RolePermissions`: `Financeiro`/`Administrador` → ambas;
    demais papéis → nenhuma
  - Teste existente de consistência catálogo↔mapa continua verde
  - _Requirements: R7.1, R7.2_

- [x] 2. Persistência
- [x] 2.1 EF configurations e `AppDbContext`
  - `BillingClosingConfiguration`, `InvoiceConfiguration`,
    `InvoiceLineItemConfiguration` em
    `Infrastructure/Persistence/Configurations/` (índices conforme
    `design.md`, incluindo o índice único `(TenantId, Year, Month)` de
    `BillingClosing`)
  - `DbSet<T>` de cada entidade em `AppDbContext`
  - _Requirements: R7.6_
- [x] 2.2 Migração `AddBilling`
  - `dotnet ef migrations add AddBilling` cobrindo as 3 novas tabelas e
    índices — **verificar antes de gerar** se todas as colunas `*Ticks`
    necessárias para comparação de período (`PeriodStartTicks`/
    `PeriodEndTicks`/`GeneratedAtTicks`/`ExecutedAtTicks`) já estão nas
    entidades (lição da Fase 7: gerar a migração cedo demais obrigou a
    reverter e regenerar manualmente)
  - Confirmar `has-pending-model-changes` limpo; build + testes de
    integração (SQLite `EnsureCreated`) continuam verdes
  - _Requirements: R7.6_

- [x] 3. Serviços de domínio
- [x] 3.1 `BillingClosingService`
  - `ExecuteAsync` (algoritmo completo do `design.md`: deriva período,
    valida período passado (R1.2) e ausência de fechamento prévio (R1.3),
    itera Impressoras do tenant, calcula consumo por (Impressora,
    CounterType) via leituras de referência de `PrinterCounter` (R2),
    resolve contrato via `IContractService.ResolveApplicableAsync` (R3.1),
    calcula excedente contra `ContractFranchise` (R3.3–R3.5), agrupa por
    Contrato e gera Faturas com Itens (R4), persiste o `BillingClosing` na
    mesma operação), `ListAsync` (histórico, R6.3)
  - Auditado: `billingclosing.execute`
  - Testes de integração: fechamento de período futuro/corrente → 400;
    refechamento do mesmo período → 409; consumo calculado corretamente com
    leitura anterior ao período disponível; consumo calculado com primeira
    leitura dentro do período quando não há leitura anterior (R2.3);
    consumo negativo tratado como zero sem interromper as demais
    impressoras (R2.4); impressora sem contrato aplicável é ignorada sem
    erro (R3.2); CounterType sem franquia configurada é ignorado sem erro
    (R3.4); fatura gerada só quando há excedente > 0; contrato sem
    excedente não gera fatura (R4.5); múltiplas impressoras do mesmo
    contrato consolidam numa única fatura; isolamento cross-tenant
  - _Requirements: R1.1–R1.3, R2.1–R2.5, R3.1–R3.5, R4.1–R4.5, R6.3, R7.3–R7.5_
- [x] 3.2 `InvoiceService`
  - `GetAsync` (com Itens, R6.2), `ListAsync` (filtros Cliente/Contrato/
    período/status, R6.1), `ChangeStatusAsync` (máquina de estados do
    `design.md`: Rascunho→{Emitida,Cancelada}, Emitida→Cancelada,
    Cancelada terminal)
  - Auditado: `invoice.status_change`
  - Testes de integração: transições válidas/inválidas; Cancelada é
    terminal (R5.4); consulta com itens; filtros de listagem; isolamento
    cross-tenant → 404/vazio
  - _Requirements: R5.1–R5.4, R6.1, R6.2, R7.3–R7.5_

- [x] 4. Endpoints da API
- [x] 4.1 `BillingClosingsController`
  - `POST api/v1/billing-closings` (`fechamento.manage`)
  - `GET api/v1/billing-closings` (`fechamento.view`, paginado)
  - _Requirements: R1.1–R1.3, R6.3, R7.4, R7.5_
- [x] 4.2 `InvoicesController`
  - `GET api/v1/invoices` (`fechamento.view`, paginado, filtros)
  - `GET api/v1/invoices/{id}` (`fechamento.view`, com itens)
  - `POST api/v1/invoices/{id}/status` (`fechamento.manage`)
  - _Requirements: R5.1–R5.4, R6.1, R6.2, R7.4, R7.5_

- [x] 5. Auditoria e segurança consolidadas
- [x] 5.1 Revisão de auditoria e testes de segurança
  - Confirmado que execução de fechamento e transição de status de fatura
    auditam corretamente via `IAuditLogger` em
    `BillingClosingService`/`InvoiceService`
  - `BillingIsolationTests` (8 casos) em `EasyPanel.SecurityTests`:
    cross-tenant em histórico de fechamento/faturas → lista vazia; RBAC
    nega `fechamento.manage`/`fechamento.view` a Operacional (que tem
    `contrato.view` mas nenhuma permissão de fechamento) e a Técnico (sem
    nenhuma permissão de fechamento); caminho positivo confirma que
    Financeiro (dono do módulo) executa fechamento e lista faturas
  - _Requirements: R7.2, R7.3, R7.5_

- [x] 6. Documentação e fechamento de fase
- [x] 6.1 Atualizar documentação
  - `docs/ARCHITECTURE.md` (novo módulo `Modules.Billing`),
    `docs/DATABASE.md` (novas tabelas/índices), `docs/API.md` (novos
    endpoints), `docs/SECURITY.md` (novas permissões), `README.md`,
    `HANDOFF.md` (Fase 8 concluída, próxima fase)
  - _Requirements: cobertura documental_
- [x] 6.2 Verificação final da fase
  - `dotnet build`/`dotnet test` de `EasyPanel.sln`: 0 avisos, 148 unit + 52
    security + 334 integration — 100% verde
  - `EasyPanel.WindowsClient.slnx` (Agente Windows, não tocado nesta fase):
    build confirmado com 0 avisos, baseline inalterado
  - `dotnet ef migrations has-pending-model-changes`: limpo
  - _Requirements: não-funcionais herdados_

## Notes

- Emissão fiscal, PDF, cobrança/pagamento, fechamento automático agendado e
  reabertura de período já fechado permanecem fora de escopo (ver
  `requirements.md`/`design.md`).
- `EasyPanel.Modules.Billing` não referencia `Modules.Monitoring`/
  `Modules.Contracts`; toda leitura de `PrinterCounter`, chamada a
  `ResolveApplicableAsync` e leitura de `ContractFranchise` acontece em
  `EasyPanel.Infrastructure.Billing`.
