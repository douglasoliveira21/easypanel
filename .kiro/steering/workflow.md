# Fluxo de trabalho e regras de execução — EasyPanel

Regras que o usuário exige em todo o trabalho neste repositório.

## Regras inegociáveis
1. **Uma fase por vez.** Não gerar tarefas de fases sem `requirements.md` e `design.md`
   aprovados. O fluxo é sempre **requisitos → design → tarefas → implementação**.
2. **Zero warnings.** `TreatWarningsAsErrors=true`; o build deve ficar limpo.
3. **Vulnerabilidades de dependência:** corrigir SEMPRE via *pin* de versão corrigida —
   NUNCA suprimir com `WarningsNotAsErrors`. Verificar com
   `dotnet list <proj> package --vulnerable --include-transitive`.
4. **Verificação independente** após cada tarefa: build (zero warnings) + testes verdes
   antes de marcar `[x]`. Se houver migração, confirmar `has-pending-model-changes` limpo.
5. **pt-BR** em tudo.
6. **Sem dados fake:** seeds limitam-se a papéis/permissões e credenciais necessárias.
7. **Execução autônoma:** não pausar entre tarefas pedindo confirmação; marcar os
   checkboxes no `tasks.md` diretamente (`- [ ]` → `- [x]`).

## Ao iniciar uma nova fase
1. Criar a spec em `.kiro/specs/easypanel-phaseN-<nome>/` com `requirements.md` (EARS),
   `design.md`, `tasks.md` e `.config.kiro` (copiar formato de uma fase existente).
2. Validar requisitos com o usuário antes do design; design antes das tarefas.
3. Implementar tarefa a tarefa; ao final da fase, atualizar `docs/*.md` e `README.md`.

## Formato do tasks.md
Plano incremental com `## Task Dependency Graph` (waves em JSON) e tarefas assim:
```markdown
- [ ] 1. <Grupo>
- [ ] 1.1 <Tarefa acionável, só de codificação>
  - <sub-passo de implementação>
  - <sub-passo de teste>
  - _Requirements: RX.Y, RX.Z_
```
Cada tarefa mantém o projeto compilável, inclui seus próprios testes e referencia
requisitos específicos. Nenhuma fase avança quebrada.

## Referência de continuidade
`HANDOFF.md` (raiz) tem o estado detalhado, o roteiro das Fases 3–10 e os primeiros
passos para quem assume o projeto.
