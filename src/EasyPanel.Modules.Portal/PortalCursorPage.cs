namespace EasyPanel.Modules.Portal;

/// <summary>
/// Página baseada em cursor para as listagens do Portal do Cliente (Fase 10),
/// específica deste módulo para não referenciar tipos de cursor de outros
/// módulos (mesmo padrão de <c>Modules.Ticketing.TicketCursorPage&lt;T&gt;</c>).
/// </summary>
public sealed record PortalCursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);
