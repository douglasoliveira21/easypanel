namespace EasyPanel.Modules.Identity;

/// <summary>
/// Catálogo de permissões granulares da plataforma (R5.2), no formato
/// <c>recurso.acao</c> (ex.: <c>customer.view</c>, <c>user.manage</c>).
///
/// Cada permissão autoriza uma operação específica sobre um recurso. Os papéis
/// (ver <see cref="Roles"/>) associam-se a conjuntos destas permissões via
/// <see cref="RolePermissions"/>. A avaliação efetiva de autorização é feita no
/// backend (tarefa 4.2), sempre a partir deste catálogo — nunca do frontend
/// (R5.6).
///
/// Escopo da Fase 1: recursos de Clientes, Locais, Usuários e Auditoria. Recursos
/// adicionais (impressoras, contratos, estoque, etc.) entram em fases futuras e
/// serão acrescentados aqui.
/// </summary>
public static class Permissions
{
    /// <summary>Visualizar/consultar Clientes (R8.7, R8.8).</summary>
    public const string CustomerView = "customer.view";

    /// <summary>Criar Clientes (R8.1).</summary>
    public const string CustomerCreate = "customer.create";

    /// <summary>Editar Clientes existentes, incluindo alteração de status (R8.6, R8.9).</summary>
    public const string CustomerEdit = "customer.edit";

    /// <summary>Excluir Clientes. Não utilizada na Fase 1 (o produto trabalha com
    /// status ativo/inativo/bloqueado em vez de exclusão física), mas incluída no
    /// catálogo para completude e uso futuro.</summary>
    public const string CustomerDelete = "customer.delete";

    /// <summary>Visualizar/consultar Locais (R9.6, R9.7).</summary>
    public const string LocationView = "location.view";

    /// <summary>Criar Locais (R9.1).</summary>
    public const string LocationCreate = "location.create";

    /// <summary>Editar Locais existentes, incluindo alteração de status (R9.5, R9.8).</summary>
    public const string LocationEdit = "location.edit";

    /// <summary>Excluir Locais. Não utilizada na Fase 1; incluída para completude
    /// e uso futuro.</summary>
    public const string LocationDelete = "location.delete";

    /// <summary>Consultar usuários do próprio tenant (R7.7).</summary>
    public const string UserView = "user.view";

    /// <summary>Gerir usuários: criar, atualizar, (des)ativar e atribuir papéis
    /// (R7.1–R7.6). Permissão administrativa sensível.</summary>
    public const string UserManage = "user.manage";

    /// <summary>Consultar o log de auditoria do próprio tenant (R10.5).</summary>
    public const string AuditView = "audit.view";

    // --- Fase 2: Monitoramento (R17.1) ---

    /// <summary>Visualizar/consultar impressoras do tenant (Fase 2 — R9).</summary>
    public const string PrinterView = "printer.view";

    /// <summary>Cadastrar impressoras (Fase 2 — R9.3).</summary>
    public const string PrinterCreate = "printer.create";

    /// <summary>Editar impressoras existentes (Fase 2 — R9).</summary>
    public const string PrinterEdit = "printer.edit";

    /// <summary>Movimentar/gerir ciclo de vida de impressoras (Fase 2 — R10).</summary>
    public const string PrinterMove = "printer.move";

    /// <summary>Consultar dados de monitoramento/eventos de impressoras (Fase 2).</summary>
    public const string PrinterMonitor = "printer.monitor";

    /// <summary>Visualizar contadores de impressoras (Fase 2 — R11.7).</summary>
    public const string CounterView = "counter.view";

    /// <summary>Aplicar ajustes administrativos de contador (Fase 2 — R11.6).</summary>
    public const string CounterAdjust = "counter.adjust";

    /// <summary>Visualizar agentes Windows registrados (Fase 2 — R3).</summary>
    public const string ClientView = "client.view";

    /// <summary>Gerir agentes/chaves de provisionamento (Fase 2 — R3.2).</summary>
    public const string ClientManage = "client.manage";

    // --- Fase 3: Alertas e Notificações (R8.1) ---

    /// <summary>Visualizar/consultar regras de alerta, alertas e histórico de notificação (Fase 3 — R1, R3, R6).</summary>
    public const string AlertView = "alert.view";

    /// <summary>Gerir regras de alerta e silenciamentos (Fase 3 — R1, R7).</summary>
    public const string AlertManage = "alert.manage";

    /// <summary>Reconhecer e resolver alertas (Fase 3 — R3.3/R3.4).</summary>
    public const string AlertAcknowledge = "alert.acknowledge";

    // --- Fase 4: Suprimentos (R6.1) ---

    /// <summary>Visualizar níveis, histórico e previsão de suprimento (Fase 4 — R3, R5).</summary>
    public const string SupplyView = "supply.view";

    /// <summary>Configurar limiares de suprimento (Fase 4 — R4).</summary>
    public const string SupplyManage = "supply.manage";

    // --- Fase 5: Estoque (R6.1) ---

    /// <summary>Visualizar itens, saldo, histórico e mínimos de estoque (Fase 5 — R1, R3, R5).</summary>
    public const string EstoqueView = "estoque.view";

    /// <summary>Gerir itens, registrar movimentações e configurar mínimos de estoque (Fase 5 — R1, R2, R5).</summary>
    public const string EstoqueManage = "estoque.manage";

    // --- Fase 6: Chamados/Helpdesk e SLA (R6.1) ---

    /// <summary>Visualizar chamados, histórico de interações e anexos (Fase 6 — R2, R3, R5).</summary>
    public const string ChamadoView = "chamado.view";

    /// <summary>Abrir chamados, atualizar status/atribuição, comentar e anexar arquivos (Fase 6 — R1, R2, R5).</summary>
    public const string ChamadoManage = "chamado.manage";

    /// <summary>Configurar a política de SLA por prioridade (Fase 6 — R4).</summary>
    public const string SlaManage = "sla.manage";

    // --- Fase 7: Contratos (R6.1) ---

    /// <summary>Visualizar contratos, escopo e franquias (Fase 7 — R1, R2, R3).</summary>
    public const string ContratoView = "contrato.view";

    /// <summary>Criar/editar contratos, gerir escopo e franquias, transicionar status (Fase 7 — R1, R2, R3, R5).</summary>
    public const string ContratoManage = "contrato.manage";

    // --- Fase 8: Fechamento e Faturamento (R7.1) ---

    /// <summary>Visualizar faturas, itens e histórico de fechamento (Fase 8 — R5, R6).</summary>
    public const string FechamentoView = "fechamento.view";

    /// <summary>Executar fechamento mensal e transicionar status de fatura (Fase 8 — R1, R5).</summary>
    public const string FechamentoManage = "fechamento.manage";

    // --- Fase 9: Relatórios e Dashboards (R6.1) ---

    /// <summary>Visualizar painel de visão geral, relatórios e exportação (Fase 9 — R1–R5).</summary>
    public const string RelatorioView = "relatorio.view";

    // --- Fase 10: Portal do Cliente (R6.1) ---

    /// <summary>
    /// Visualizar, no Portal do Cliente, o parque de impressoras e o histórico de
    /// contadores do próprio Cliente (Fase 10 — R3).
    /// </summary>
    public const string PortalParqueView = "portal.parque.view";

    /// <summary>
    /// Visualizar, no Portal do Cliente, os chamados do próprio Cliente
    /// (Fase 10 — R4).
    /// </summary>
    public const string PortalChamadoView = "portal.chamado.view";

    /// <summary>
    /// Visualizar, no Portal do Cliente, as faturas do próprio Cliente
    /// (Fase 10 — R5).
    /// </summary>
    public const string PortalFaturaView = "portal.fatura.view";

    /// <summary>
    /// Conjunto imutável de todas as permissões do catálogo. Lista explícita
    /// (não por reflexão) para ser amigável a analisadores e determinística.
    /// Toda permissão referenciada em <see cref="RolePermissions"/> deve constar
    /// aqui (validado por testes: sem órfãs/erros de digitação).
    /// </summary>
    public static readonly IReadOnlyList<string> All = new[]
    {
        CustomerView,
        CustomerCreate,
        CustomerEdit,
        CustomerDelete,
        LocationView,
        LocationCreate,
        LocationEdit,
        LocationDelete,
        UserView,
        UserManage,
        AuditView,
        PrinterView,
        PrinterCreate,
        PrinterEdit,
        PrinterMove,
        PrinterMonitor,
        CounterView,
        CounterAdjust,
        ClientView,
        ClientManage,
        AlertView,
        AlertManage,
        AlertAcknowledge,
        SupplyView,
        SupplyManage,
        EstoqueView,
        EstoqueManage,
        ChamadoView,
        ChamadoManage,
        SlaManage,
        ContratoView,
        ContratoManage,
        FechamentoView,
        FechamentoManage,
        RelatorioView,
        PortalParqueView,
        PortalChamadoView,
        PortalFaturaView,
    };
}
