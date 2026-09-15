namespace EasyPanel.Modules.Identity;

/// <summary>
/// Mapeamento papel→permissões da Fase 1 (R5.2). Para a Fase 1 este mapeamento é
/// mantido <b>em código</b> (fonte única de verdade): a tarefa 4.2 resolve as
/// permissões efetivas por papel a partir daqui (com cache no Redis), de modo que
/// não é necessário persistir linhas de permissão no banco em 4.1 — o seeder
/// persiste apenas os papéis (via <c>RoleManager</c>). Persistir permissões como
/// role claims seria uma alternativa aceitável, porém não requerida nesta tarefa.
///
/// Decisões de mapeamento (Fase 1 cobre apenas Clientes, Locais, Usuários e
/// Auditoria):
/// <list type="bullet">
///   <item><b>Super Admin</b>: todas as permissões do catálogo (administra a plataforma).</item>
///   <item><b>Administrador</b>: todas as permissões com escopo de tenant
///     (customer.*, location.*, user.view/manage, audit.view) — controle total no tenant.</item>
///   <item><b>Financeiro</b>: leitura de Clientes/Locais + auditoria (permissões
///     financeiras de contrato/fechamento chegam em fases futuras).</item>
///   <item><b>Operacional</b>: CRUD (sem exclusão) de Clientes e Locais; sem gestão de usuários.</item>
///   <item><b>Estoque</b>: leitura mínima de Clientes/Locais (permissões de estoque reais em fases futuras).</item>
///   <item><b>Técnico</b>: leitura de Clientes/Locais (chamados/SLA em fases futuras).</item>
///   <item><b>Supervisor</b>: leitura de Clientes/Locais + auditoria (acompanhamento).</item>
///   <item><b>Cliente</b>: exclusivamente as permissões do Portal do Cliente
///     (portal.*.view — Fase 10), restritas ao próprio Cliente vinculado
///     (<c>CustomerId</c>); nenhuma permissão administrativa/operacional do
///     tenant.</item>
/// </list>
/// </summary>
public static class RolePermissions
{
    /// <summary>
    /// Permissões efetivas por papel. Toda permissão listada aqui existe em
    /// <see cref="Permissions.All"/> (garantido por testes). Papéis sem permissões
    /// administrativas na Fase 1 (ex.: Cliente) mapeiam para um conjunto vazio.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyCollection<string>> Map =
        new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.Ordinal)
        {
            // Super Admin recebe todo o catálogo.
            [Roles.SuperAdmin] = Permissions.All.ToArray(),

            // Administrador: tudo dentro do escopo de tenant.
            [Roles.Administrador] = new[]
            {
                Permissions.CustomerView,
                Permissions.CustomerCreate,
                Permissions.CustomerEdit,
                Permissions.CustomerDelete,
                Permissions.LocationView,
                Permissions.LocationCreate,
                Permissions.LocationEdit,
                Permissions.LocationDelete,
                Permissions.UserView,
                Permissions.UserManage,
                Permissions.AuditView,
                Permissions.PrinterView,
                Permissions.PrinterCreate,
                Permissions.PrinterEdit,
                Permissions.PrinterMove,
                Permissions.PrinterMonitor,
                Permissions.CounterView,
                Permissions.CounterAdjust,
                Permissions.ClientView,
                Permissions.ClientManage,
                Permissions.AlertView,
                Permissions.AlertManage,
                Permissions.AlertAcknowledge,
                Permissions.SupplyView,
                Permissions.SupplyManage,
                Permissions.EstoqueView,
                Permissions.EstoqueManage,
                Permissions.ChamadoView,
                Permissions.ChamadoManage,
                Permissions.SlaManage,
                Permissions.ContratoView,
                Permissions.ContratoManage,
                Permissions.FechamentoView,
                Permissions.FechamentoManage,
                Permissions.RelatorioView,
            },

            // Financeiro: leitura de negócio + auditoria + contadores (faturamento) +
            // dono natural dos módulos de Contratos (Fase 7) e Fechamento/Faturamento
            // (Fase 8) — leitura e gestão completas em ambos — e acesso a
            // Relatórios/Dashboards (Fase 9).
            [Roles.Financeiro] = new[]
            {
                Permissions.CustomerView,
                Permissions.LocationView,
                Permissions.AuditView,
                Permissions.PrinterView,
                Permissions.CounterView,
                Permissions.AlertView,
                Permissions.SupplyView,
                Permissions.ContratoView,
                Permissions.ContratoManage,
                Permissions.FechamentoView,
                Permissions.FechamentoManage,
                Permissions.RelatorioView,
            },

            // Operacional: CRUD de Clientes/Locais/Impressoras (sem exclusão) e
            // movimentação/monitoramento; sem gestão de usuários.
            [Roles.Operacional] = new[]
            {
                Permissions.CustomerView,
                Permissions.CustomerCreate,
                Permissions.CustomerEdit,
                Permissions.LocationView,
                Permissions.LocationCreate,
                Permissions.LocationEdit,
                Permissions.PrinterView,
                Permissions.PrinterCreate,
                Permissions.PrinterEdit,
                Permissions.PrinterMove,
                Permissions.PrinterMonitor,
                Permissions.CounterView,
                Permissions.ClientView,
                Permissions.AlertView,
                Permissions.AlertAcknowledge,
                Permissions.SupplyView,
                Permissions.EstoqueView,
                Permissions.ChamadoView,
                Permissions.ContratoView,
            },

            // Estoque: dono natural do módulo (Fase 5) — leitura e gestão completas.
            [Roles.Estoque] = new[]
            {
                Permissions.CustomerView,
                Permissions.LocationView,
                Permissions.EstoqueView,
                Permissions.EstoqueManage,
            },

            // Técnico: leitura de negócio + impressoras/monitoramento (campo).
            [Roles.Tecnico] = new[]
            {
                Permissions.CustomerView,
                Permissions.LocationView,
                Permissions.PrinterView,
                Permissions.PrinterMonitor,
                Permissions.CounterView,
                Permissions.ClientView,
                Permissions.AlertView,
                Permissions.AlertAcknowledge,
                Permissions.SupplyView,
                Permissions.EstoqueView,
                Permissions.ChamadoView,
                Permissions.ChamadoManage,
            },

            // Supervisor: leitura + auditoria + monitoramento (acompanhamento) +
            // gestão de regras/silenciamentos de alerta.
            [Roles.Supervisor] = new[]
            {
                Permissions.CustomerView,
                Permissions.LocationView,
                Permissions.AuditView,
                Permissions.PrinterView,
                Permissions.PrinterMonitor,
                Permissions.CounterView,
                Permissions.ClientView,
                Permissions.AlertView,
                Permissions.AlertManage,
                Permissions.AlertAcknowledge,
                Permissions.SupplyView,
                Permissions.EstoqueView,
                Permissions.ChamadoView,
                Permissions.ChamadoManage,
                Permissions.SlaManage,
                Permissions.ContratoView,
                Permissions.RelatorioView,
            },

            // Cliente: exclusivamente o Portal do Cliente (Fase 10), restrito ao
            // próprio Cliente vinculado (CustomerId) — nenhuma outra permissão.
            [Roles.Cliente] = new[]
            {
                Permissions.PortalParqueView,
                Permissions.PortalChamadoView,
                Permissions.PortalFaturaView,
            },
        };

    /// <summary>
    /// Retorna as permissões efetivas do papel informado, ou um conjunto vazio se
    /// o papel não estiver mapeado. Comparação de nome ordinal (case-sensitive),
    /// coerente com os nomes canônicos de <see cref="Roles"/>.
    /// </summary>
    public static IReadOnlyCollection<string> ForRole(string roleName) =>
        Map.TryGetValue(roleName, out var permissions)
            ? permissions
            : Array.Empty<string>();
}
