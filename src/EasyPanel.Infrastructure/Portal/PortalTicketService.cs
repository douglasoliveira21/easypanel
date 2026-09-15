using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Portal;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Modules.Ticketing;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Portal;

/// <summary>
/// Implementação de <see cref="IPortalTicketService"/> (Fase 10 — R4). Restringe
/// toda consulta ao Cliente do <see cref="ICustomerContext"/>, além do filtro
/// global de tenant já aplicado a <see cref="Ticket"/> (herdado).
/// </summary>
public sealed class PortalTicketService : IPortalTicketService
{
    private const int MaxPageSize = 100;

    private readonly AppDbContext _context;
    private readonly ICustomerContext _customerContext;

    public PortalTicketService(AppDbContext context, ICustomerContext customerContext)
    {
        _context = context;
        _customerContext = customerContext;
    }

    /// <inheritdoc />
    public async Task<Result<PortalCursorPage<PortalTicketSummaryDto>>> ListAsync(
        string? cursor, int pageSize, CancellationToken ct)
    {
        var size = pageSize is <= 0 or > MaxPageSize ? MaxPageSize : pageSize;

        if (_customerContext.CustomerId is not { } customerId)
        {
            return Result.Success(new PortalCursorPage<PortalTicketSummaryDto>(Array.Empty<PortalTicketSummaryDto>(), null));
        }

        var q = _context.Tickets.AsNoTracking().Where(t => t.CustomerId == customerId);

        if (TryDecodeCursor(cursor, out var cursorTicks))
        {
            q = q.Where(t => t.CreatedAtTicks < cursorTicks);
        }

        var rows = await q
            .OrderByDescending(t => t.CreatedAtTicks)
            .ThenByDescending(t => t.Id)
            .Take(size + 1)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        string? nextCursor = null;
        if (rows.Count > size)
        {
            nextCursor = EncodeCursor(rows[size - 1].CreatedAtTicks);
            rows = rows.Take(size).ToList();
        }

        var items = rows.Select(ToSummaryDto).ToList();

        return Result.Success(new PortalCursorPage<PortalTicketSummaryDto>(items, nextCursor));
    }

    /// <inheritdoc />
    public async Task<Result<PortalTicketDetailDto>> GetAsync(Guid ticketId, CancellationToken ct)
    {
        if (_customerContext.CustomerId is not { } customerId)
        {
            return Result.Failure<PortalTicketDetailDto>(PortalErrors.NotFound);
        }

        // Chamado de outro Cliente (mesmo tenant) ou inexistente: 404 uniforme
        // (R4.2, não-enumeração).
        var ticket = await _context.Tickets.AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == ticketId && t.CustomerId == customerId, ct)
            .ConfigureAwait(false);
        if (ticket is null)
        {
            return Result.Failure<PortalTicketDetailDto>(PortalErrors.NotFound);
        }

        // Apenas interações do tipo Comentário são expostas — mudanças de
        // status/atribuição são detalhe operacional interno (ver TicketContracts).
        var comments = await _context.TicketInteractions.AsNoTracking()
            .Where(i => i.TicketId == ticketId && i.Type == TicketInteractionType.Comentario)
            .OrderBy(i => i.OccurredAtTicks)
            .ThenBy(i => i.Id)
            .Select(i => new PortalTicketCommentDto(i.OccurredAt, i.Comment ?? string.Empty))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var attachments = await _context.TicketAttachments.AsNoTracking()
            .Where(a => a.TicketId == ticketId)
            .OrderBy(a => a.UploadedAtTicks)
            .ThenBy(a => a.Id)
            .Select(a => new PortalTicketAttachmentDto(a.Id, a.FileName, a.ContentType))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return Result.Success(new PortalTicketDetailDto(
            ticket.Id,
            ticket.Title,
            ticket.Description,
            (PortalTicketStatus)(int)ticket.Status,
            ticket.CreatedAt,
            comments,
            attachments));
    }

    private static PortalTicketSummaryDto ToSummaryDto(Ticket ticket) => new(
        ticket.Id, ticket.Title, (PortalTicketStatus)(int)ticket.Status, ticket.CreatedAt);

    private static string EncodeCursor(long ticks) => Convert.ToBase64String(BitConverter.GetBytes(ticks));

    private static bool TryDecodeCursor(string? cursor, out long ticks)
    {
        ticks = 0;
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return false;
        }

        try
        {
            var bytes = Convert.FromBase64String(cursor);
            if (bytes.Length != sizeof(long))
            {
                return false;
            }

            ticks = BitConverter.ToInt64(bytes);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
