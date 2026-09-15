# Produto — EasyPanel

EasyPanel é uma plataforma **SaaS multi-tenant de outsourcing de impressão e ativos
de TI**. Empresas prestadoras de serviço gerenciam, isoladas por tenant, seus
clientes, os locais de instalação, o parque de impressoras e o consumo (contadores),
com base para chamados, contratos e faturamento.

## Idioma
Todo o projeto é em **pt-BR**: código, comentários (XML-doc), documentação, mensagens
e respostas ao usuário.

## Estado por fase
- **Fase 1 (Fundação)** — concluída: infra, multi-tenancy, Identity/JWT/RBAC,
  auditoria, usuários, clientes, locais, observabilidade.
- **Fase 2 (Monitoramento)** — concluída: autenticação do agente Windows,
  registro/heartbeat, ingestão idempotente + pipeline, impressoras, contadores
  (não-decréscimo + ajuste auditado), config/update, e o Agente Windows
  (solução-satélite: SNMP + drivers por fabricante, fila offline, auto-update assinado).
- **Fases 3–10** — não iniciadas (roteiro em `HANDOFF.md`). Cada uma exige spec própria
  antes de implementar.

## Princípio de entrega
**Uma fase por vez**, sempre no fluxo **requisitos → design → tarefas → implementação**.
Não gerar tarefas de fases que ainda não têm requisitos e design aprovados.
