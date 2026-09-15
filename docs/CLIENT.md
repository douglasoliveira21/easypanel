# Agente Windows — EasyPanel (Fases 2 e 4)

O Agente Windows é uma **solução-satélite** independente do backend, localizada em
`src/windows-client/` (`EasyPanel.WindowsClient.slnx`). Ele descobre e monitora
impressoras via SNMP e reporta os dados (contadores + suprimentos) à API HTTP. Tem
versionamento, build e distribuição próprios; consome apenas os endpoints públicos
de agente da API.

## Projetos

| Projeto | Responsabilidade |
|---------|------------------|
| `EasyPanel.WindowsClient` | Host Worker Service: instância única (mutex), Guardian (auto-recovery) supervisionando `Net_Monitoring_Service` e `Communication_Service` (Fase 4), fila local offline (SQLite), reenvio com backoff, cliente HTTP real (`HttpBackendClient`, Fase 4) com autenticação/renovação de token e validação TLS, auto-update assinado |
| `EasyPanel.WindowsClient.Snmp` | Coleta SNMP real (v1/v2c via `Lextm.SharpSnmpLib`, Fase 4; v3 previsto), drivers por fabricante (HP, Canon, Epson, Brother, Kyocera, Xerox, Ricoh, Konica Minolta, Lexmark) + driver genérico, seleção de driver, descoberta e leitura de níveis de suprimento (Printer-MIB padrão) |
| `EasyPanel.WindowsClient.Tests` | Testes de unidade do Guardian, instância única, fila/reenvio, redação de segredos, verificação/rollback de atualização, drivers/descoberta SNMP, cliente HTTP (autenticação/renovação/reautenticação), `Net_Monitoring_Service`/`Communication_Service` e um teste de ponta a ponta do ciclo completo |

## Ciclo de vida (R1/R2; orquestração real — Fase 4)

- **Instância única (R2.1):** um mutex global garante uma única instância por
  máquina; uma segunda encerra graciosamente.
- **Guardian (R2.2):** supervisiona os serviços internos registrados e reinicia os
  que pararam, registrando o reinício. Até a Fase 4, a lista de serviços
  supervisionados era **vazia** (nenhum ciclo de coleta rodava de fato); a Fase 4
  registra os dois serviços reais:
  - **`Net_Monitoring_Service`:** ciclo periódico que obtém a configuração vigente
    (`GET /client/config`, incluindo as impressoras monitoráveis do Local), sonda
    cada uma via SNMP, monta a submissão de coleta (contadores + suprimentos) e a
    enfileira na fila local — nunca envia diretamente ao backend.
  - **`Communication_Service`:** drena periodicamente a fila local para o backend,
    reaproveitando o `QueueResender` da Fase 2 sem alterá-lo.
  - Ambos começam "não saudáveis" (nenhum laço rodando); a primeira varredura do
    Guardian os inicia via `RestartAsync` — o mesmo mecanismo que os reinicia se
    pararem depois.
- **Serviço Windows (R1):** instalável como Windows Service (execução silenciosa/
  GPO); em desenvolvimento roda como console. Logs e configuração locais.

## Configuração (`appsettings.json`, seção `Agent`)

| Chave | Descrição |
|-------|-----------|
| `BackendBaseUrl` | URL HTTPS base da API |
| `ClientId` / `ClientSecret` | Credenciais próprias emitidas no registro |
| `ProvisioningKey` | Chave de provisionamento do Local (primeiro registro) |
| `UniqueId` | Identificador único e persistente do agente |
| `LocalQueuePath` | Caminho do SQLite da fila offline |
| `GuardianIntervalSeconds` / `ResendIntervalSeconds` / `HeartbeatIntervalSeconds` | Intervalos operacionais |

Segredos vêm de configuração protegida da máquina — nunca versionados.

## Fluxo operacional

1. **Registro (R3):** com a `ProvisioningKey`, chama `POST /api/v1/client/register`
   e recebe `ClientId`/`ClientSecret` e o primeiro par de tokens. *(Fluxo ainda
   operacional/manual — o `HttpBackendClient` da Fase 4 assume `ClientId`/
   `ClientSecret` já provisionados em configuração; não chama `/register`.)*
2. **Autenticação (R4; implementada de ponta a ponta na Fase 4):**
   `HttpBackendClient` chama `POST /api/v1/client/token` (client_id + secret) e
   guarda o par de tokens em memória; renova via `token/refresh` antes de expirar
   (margem de 30s) e reautentica do zero se a renovação falhar ou uma chamada
   receber 401 (uma tentativa de retry). Token de acesso ≤ 15 min.
3. **Configuração (R15; `MonitoredPrinters` — Fase 4):** `GET /api/v1/client/config`
   traz intervalo de coleta, alvos/ignorados de descoberta, e a lista de
   impressoras já registradas e monitoráveis do Local (id no backend + endereço),
   consumida pelo `Net_Monitoring_Service`. Falha na obtenção mantém a última
   configuração válida conhecida.
4. **Coleta (R7/R8; ciclo real — Fase 4):** o `Net_Monitoring_Service` sonda cada
   impressora monitorável por SNMP real (v1/v2c, via `Lextm.SharpSnmpLib`),
   seleciona o driver do fabricante e normaliza série/status/contadores/**níveis de
   suprimento** (Printer-MIB padrão: `prtMarkerSuppliesLevel`/`MaxCapacity`,
   índices fixos 1–6, sem exigir SNMP walk).
5. **Envio (R12/R13; Fase 4 acopla suprimento à mesma submissão):** cada leitura
   vira uma submissão com `IdempotencyKey` própria, enfileirada localmente; o
   `Communication_Service` drena a fila para `POST /api/v1/client/collect`,
   incluindo contadores e suprimentos no mesmo corpo. Offline, os itens permanecem
   na fila e são reenviados com backoff ao reconectar.
6. **Heartbeat (R6):** `POST /api/v1/client/heartbeat` periódico com versão/estado.
   *(Não chamado pelo `HttpBackendClient` desta fase — fora do escopo aprovado da
   Fase 4; ver Notas do `design.md`.)*
7. **Atualização (R16):** `GET /api/v1/client/update` traz versão/hash/assinatura;
   o pacote só é aplicado após verificação de hash **e** assinatura; falha de
   inicialização dispara rollback para a versão anterior.

## Segurança (R5)

- HTTPS de saída com **validação de certificado TLS**; falha aborta e loga.
- **Nenhum segredo** (tokens, secret, community SNMP) é gravado em fila ou log; a
  redação por padrões cobre pares chave/valor e o esquema Bearer.
- Fila local contém apenas dados de negócio da coleta, livres de segredos.
- Coleta SNMP real via `Lextm.SharpSnmpLib` (pacote fixado por versão; verificado
  sem vulnerabilidade conhecida na publicação da Fase 4).

## Build e testes

```bash
# a partir de src/windows-client/
dotnet build EasyPanel.WindowsClient.slnx
dotnet test EasyPanel.WindowsClient.slnx
```

A solução do agente é independente da `EasyPanel.sln` do backend: alterá-la não
afeta o build do backend e vice-versa.

## Itens previstos, não implementados

- SNMP v3 (apenas extensibilidade da abstração; v1/v2c implementados).
- Descoberta por USB e varredura ampla de subnet/broadcast são pontos de extensão
  do `Net_Monitoring_Service` sobre o transporte real; o núcleo de seleção/coleta e
  a descoberta por lista de alvos estão implementados e testados.
- `POST /api/v1/client/register` e `POST /api/v1/client/heartbeat` não são
  chamados pelo `HttpBackendClient` (fora do escopo aprovado da Fase 4); ambos os
  endpoints já existem no backend desde a Fase 2.
- `Update_Service` como serviço supervisionado pelo `Guardian` (o `UpdateApplier`/
  `UpdatePackageVerifier` existem e funcionam, mas não rodam em um laço próprio
  supervisionado ainda).
- SNMP walk/GETNEXT: a leitura de suprimento usa um número fixo de índices
  (`StandardOids.MaxSupplyUnits`) por GET simples, cobrindo o caso comum sem exigir
  varredura completa da tabela Printer-MIB.
