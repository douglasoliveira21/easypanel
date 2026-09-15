using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Monitoring;

/// <summary>
/// Implementação de <see cref="IPrinterService"/> sobre o <see cref="AppDbContext"/>
/// (R9/R10). <see cref="Printer"/> é uma <c>TenantEntity</c>: leituras são
/// auto-escopadas pelo filtro global (cross-tenant → 404) e escritas têm o
/// <c>TenantId</c> carimbado pelo interceptor. Criação/edição e movimentações são
/// auditadas (R9/R10); a movimentação registra histórico append-only em
/// <see cref="PrinterMovement"/>.
/// </summary>
public sealed class PrinterService : IPrinterService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ICurrentUserAccessor _currentUser;
    private readonly IAuditLogger _auditLogger;

    public PrinterService(
        AppDbContext context,
        ITenantContext tenantContext,
        ICurrentUserAccessor currentUser,
        IAuditLogger auditLogger)
    {
        _context = context;
        _tenantContext = tenantContext;
        _currentUser = currentUser;
        _auditLogger = auditLogger;
    }

    /// <inheritdoc />
    public async Task<Result<PrinterDto>> CreateAsync(CreatePrinterRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (_tenantContext.TenantId is null && !_tenantContext.IsSuperAdmin)
        {
            return Result.Failure<PrinterDto>(MonitoringErrors.Unauthorized);
        }

        // Cliente e local devem existir no tenant corrente (auto-escopados) e o
        // local deve pertencer ao cliente informado (R9.3).
        var location = await _context.Set<EasyPanel.Modules.Customers.Location>()
            .FirstOrDefaultAsync(l => l.Id == request.LocationId && l.CustomerId == request.CustomerId, ct)
            .ConfigureAwait(false);

        if (location is null)
        {
            return Result.Failure<PrinterDto>(MonitoringErrors.InvalidLocation);
        }

        var printer = new Printer
        {
            Id = Guid.NewGuid(),
            CustomerId = request.CustomerId,
            LocationId = request.LocationId,
            Fabricante = request.Fabricante,
            Modelo = request.Modelo,
            NumeroSerie = request.NumeroSerie,
            Patrimonio = request.Patrimonio,
            Ip = request.Ip,
            Mac = request.Mac,
            Hostname = request.Hostname,
            Protocolo = request.Protocolo,
            Porta = request.Porta,
            Status = PrinterStatus.Unknown,
            MonitoringEnabled = request.MonitoringEnabled,
            InstalledAt = DateTimeOffset.UtcNow,
            Observacoes = request.Observacoes,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _context.Set<Printer>().Add(printer);

        // Registro inicial de instalação no histórico (R10.1).
        _context.Set<PrinterMovement>().Add(new PrinterMovement
        {
            Id = Guid.NewGuid(),
            TenantId = printer.TenantId,
            PrinterId = printer.Id,
            Operation = MovementOperation.Install,
            ToLocationId = printer.LocationId,
            ActorUserId = _currentUser.UserId,
            OccurredAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
        });

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync("printer.create", printer, ct).ConfigureAwait(false);

        return Result.Success(MapToDto(printer));
    }

    /// <inheritdoc />
    public async Task<Result<PrinterDto>> UpdateAsync(Guid id, UpdatePrinterRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var printer = await _context.Set<Printer>()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        if (printer is null)
        {
            return Result.Failure<PrinterDto>(MonitoringErrors.NotFound);
        }

        printer.Fabricante = request.Fabricante;
        printer.Modelo = request.Modelo;
        printer.NumeroSerie = request.NumeroSerie;
        printer.Patrimonio = request.Patrimonio;
        printer.Ip = request.Ip;
        printer.Mac = request.Mac;
        printer.Hostname = request.Hostname;
        printer.Protocolo = request.Protocolo;
        printer.Porta = request.Porta;
        printer.MonitoringEnabled = request.MonitoringEnabled;
        printer.Observacoes = request.Observacoes;
        printer.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync("printer.edit", printer, ct).ConfigureAwait(false);

        return Result.Success(MapToDto(printer));
    }

    /// <inheritdoc />
    public async Task<Result<PrinterDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var printer = await _context.Set<Printer>()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        return printer is null
            ? Result.Failure<PrinterDto>(MonitoringErrors.NotFound)
            : Result.Success(MapToDto(printer));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<PrinterDto>>> ListAsync(PrinterQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);

        var q = _context.Set<Printer>().AsNoTracking();

        if (query.CustomerId is { } customerId)
        {
            q = q.Where(p => p.CustomerId == customerId);
        }

        if (query.LocationId is { } locationId)
        {
            q = q.Where(p => p.LocationId == locationId);
        }

        if (!string.IsNullOrWhiteSpace(query.Fabricante))
        {
            q = q.Where(p => p.Fabricante == query.Fabricante);
        }

        if (!string.IsNullOrWhiteSpace(query.Modelo))
        {
            q = q.Where(p => p.Modelo == query.Modelo);
        }

        if (query.Status is { } status)
        {
            q = q.Where(p => p.Status == status);
        }

        if (query.MonitoringEnabled is { } enabled)
        {
            q = q.Where(p => p.MonitoringEnabled == enabled);
        }

        var totalCount = await q.LongCountAsync(ct).ConfigureAwait(false);

        var page = query.Page;
        var skip = (page.Page - 1) * page.PageSize;

        // Ordenação determinística por colunas traduzíveis (evita ORDER BY sobre
        // DateTimeOffset). NumeroSerie pode ser nulo; Id desempata.
        var printers = await q
            .OrderBy(p => p.Fabricante)
            .ThenBy(p => p.Modelo)
            .ThenBy(p => p.Id)
            .Skip(skip)
            .Take(page.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = printers.Select(MapToDto).ToList();

        return Result.Success(new PagedResult<PrinterDto>(items, page.Page, page.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<PrinterDto>> MoveAsync(
        Guid id,
        MovementOperation operation,
        Guid? toLocationId,
        CancellationToken ct)
    {
        var printer = await _context.Set<Printer>()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            .ConfigureAwait(false);

        if (printer is null)
        {
            return Result.Failure<PrinterDto>(MonitoringErrors.NotFound);
        }

        var now = DateTimeOffset.UtcNow;
        var fromLocationId = printer.LocationId;

        switch (operation)
        {
            case MovementOperation.Transfer:
                if (toLocationId is not { } target)
                {
                    return Result.Failure<PrinterDto>(MonitoringErrors.InvalidLocation);
                }

                // Local de destino deve existir no tenant corrente (R10.4).
                var exists = await _context.Set<EasyPanel.Modules.Customers.Location>()
                    .AnyAsync(l => l.Id == target, ct)
                    .ConfigureAwait(false);
                if (!exists)
                {
                    return Result.Failure<PrinterDto>(MonitoringErrors.InvalidLocation);
                }

                printer.LocationId = target;
                break;

            case MovementOperation.Disable:
                printer.MonitoringEnabled = false;
                printer.Status = PrinterStatus.Disabled;
                break;

            case MovementOperation.Reactivate:
                printer.MonitoringEnabled = true;
                printer.Status = PrinterStatus.Unknown;
                break;

            case MovementOperation.Collect:
                printer.MonitoringEnabled = false;
                printer.Status = PrinterStatus.Disabled;
                break;

            case MovementOperation.Install:
                if (toLocationId is { } installTarget)
                {
                    printer.LocationId = installTarget;
                }

                break;

            default:
                return Result.Failure<PrinterDto>(MonitoringErrors.InvalidLocation);
        }

        printer.UpdatedAt = now;

        // Histórico append-only da movimentação (R10.2/R10.3).
        _context.Set<PrinterMovement>().Add(new PrinterMovement
        {
            Id = Guid.NewGuid(),
            TenantId = printer.TenantId,
            PrinterId = printer.Id,
            Operation = operation,
            FromLocationId = fromLocationId,
            ToLocationId = operation == MovementOperation.Transfer || operation == MovementOperation.Install
                ? printer.LocationId
                : null,
            ActorUserId = _currentUser.UserId,
            OccurredAt = now,
            CreatedAt = now,
        });

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        await AuditAsync($"printer.move.{operation}".ToLowerInvariant(), printer, ct).ConfigureAwait(false);

        return Result.Success(MapToDto(printer));
    }

    private Task AuditAsync(string action, Printer printer, CancellationToken ct) =>
        _auditLogger.LogAsync(
            new AuditEntry
            {
                ActorUserId = _currentUser.UserId,
                TenantId = printer.TenantId,
                Action = action,
                ResourceType = nameof(Printer),
                ResourceId = printer.Id.ToString(),
                Result = AuditResult.Success,
            },
            ct);

    private static PrinterDto MapToDto(Printer p) => new(
        p.Id,
        p.TenantId,
        p.CustomerId,
        p.LocationId,
        p.Fabricante,
        p.Modelo,
        p.NumeroSerie,
        p.Patrimonio,
        p.Ip,
        p.Mac,
        p.Hostname,
        p.Protocolo,
        p.Porta,
        p.Status,
        p.MonitoringEnabled,
        p.InstalledAt,
        p.Observacoes,
        p.CreatedAt,
        p.UpdatedAt);
}
