using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ILocationService"/> sobre o <see cref="AppDbContext"/>
/// (R9). Como <see cref="Location"/> é uma <c>TenantEntity</c>, conta com o
/// isolamento automático do ORM:
///
/// <list type="bullet">
///   <item><b>Leitura</b> (get/list): o filtro global de consulta por
///     <c>TenantId</c> restringe automaticamente as linhas ao tenant corrente
///     (R9.6/R9.7). Um local de outro tenant não aparece na consulta → mapeado
///     para <see cref="LocationErrors.NotFound"/> (HTTP 404), sem revelar
///     existência (não-vazamento).</item>
///   <item><b>Escrita</b> (create): o interceptor de <c>SaveChanges</c> carimba o
///     <c>TenantId</c> a partir do <see cref="ITenantContext"/> em entidades
///     <c>Added</c> (R9.1). Um guarda explícito retorna
///     <see cref="LocationErrors.NoTenantContext"/> quando não há tenant.</item>
/// </list>
///
/// <para><b>Vínculo com o cliente (R9.4).</b> Na criação, o <c>CustomerId</c>
/// informado é validado contra o tenant corrente: como a consulta a
/// <c>Customers</c> já é escopada pelo filtro global, um cliente inexistente ou de
/// outro tenant simplesmente não é encontrado → <see cref="LocationErrors.CustomerNotFound"/>
/// (HTTP 400), sem revelar existência cross-tenant.</para>
///
/// <para><b>Ordenação da listagem.</b> Ordena por <c>Nome</c> (coluna indexada e
/// traduzível tanto no PostgreSQL quanto no SQLite dos testes), com desempate por
/// <c>Id</c> para determinismo.</para>
/// </summary>
public sealed class LocationService : ILocationService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Cria o serviço com o contexto de persistência scoped e o contexto de
    /// tenant da requisição corrente.
    /// </summary>
    public LocationService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<LocationDto>> CreateAsync(CreateLocationRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ct.ThrowIfCancellationRequested();

        // O tenant vem sempre do contexto (R9.1/R6.3). Guarda explícito para uma
        // falha de validação limpa em vez de deixar o interceptor lançar.
        if (_tenantContext.TenantId is null && !_tenantContext.IsSuperAdmin)
        {
            return Result.Failure<LocationDto>(LocationErrors.NoTenantContext);
        }

        if (string.IsNullOrWhiteSpace(req.Nome))
        {
            return Result.Failure<LocationDto>(LocationErrors.MissingNome);
        }

        if (!IsDefinedStatus(req.Status))
        {
            return Result.Failure<LocationDto>(LocationErrors.InvalidStatus);
        }

        // Valida o vínculo com o cliente dentro do tenant (R9.4). O filtro global
        // já escopa a consulta ao tenant corrente; cliente de outro tenant ou
        // inexistente → não encontrado.
        var customerExists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == req.CustomerId, ct)
            .ConfigureAwait(false);

        if (!customerExists)
        {
            return Result.Failure<LocationDto>(LocationErrors.CustomerNotFound);
        }

        var location = new Location
        {
            Id = Guid.NewGuid(),
            // TenantId é carimbado pelo interceptor de escrita a partir do contexto (R9.1).
            CustomerId = req.CustomerId,
            Nome = req.Nome.Trim(),
            Endereco = req.Endereco,
            Responsavel = req.Responsavel,
            Telefone = req.Telefone,
            Email = req.Email,
            Observacoes = req.Observacoes,
            Status = req.Status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _context.Locations.Add(location);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success(MapToDto(location));
    }

    /// <inheritdoc />
    public async Task<Result<LocationDto>> UpdateAsync(Guid id, UpdateLocationRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ct.ThrowIfCancellationRequested();

        // Filtro global escopa a busca ao tenant corrente; outro tenant → null → 404.
        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            .ConfigureAwait(false);

        if (location is null)
        {
            return Result.Failure<LocationDto>(LocationErrors.NotFound);
        }

        if (string.IsNullOrWhiteSpace(req.Nome))
        {
            return Result.Failure<LocationDto>(LocationErrors.MissingNome);
        }

        location.Nome = req.Nome.Trim();
        location.Endereco = req.Endereco;
        location.Responsavel = req.Responsavel;
        location.Telefone = req.Telefone;
        location.Email = req.Email;
        location.Observacoes = req.Observacoes;
        location.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success(MapToDto(location));
    }

    /// <inheritdoc />
    public async Task<Result<LocationDto>> GetAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var location = await _context.Locations
            .AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            .ConfigureAwait(false);

        return location is null
            ? Result.Failure<LocationDto>(LocationErrors.NotFound)
            : Result.Success(MapToDto(location));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<LocationDto>>> ListByCustomerAsync(
        Guid customerId,
        PageRequest query,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        // O filtro global restringe as linhas ao tenant corrente (R9.7); o
        // CustomerId filtra os locais do cliente. Cliente de outro tenant não
        // possui locais visíveis → página vazia (não-vazamento).
        var baseQuery = _context.Locations
            .AsNoTracking()
            .Where(l => l.CustomerId == customerId);

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page - 1) * query.PageSize;

        var locations = await baseQuery
            .OrderBy(l => l.Nome)
            .ThenBy(l => l.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = locations.Select(MapToDto).ToList();

        return Result.Success(new PagedResult<LocationDto>(items, query.Page, query.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<LocationDto>> ChangeStatusAsync(Guid id, LocationStatus status, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        if (!IsDefinedStatus(status))
        {
            return Result.Failure<LocationDto>(LocationErrors.InvalidStatus);
        }

        var location = await _context.Locations
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            .ConfigureAwait(false);

        if (location is null)
        {
            return Result.Failure<LocationDto>(LocationErrors.NotFound);
        }

        if (location.Status != status)
        {
            location.Status = status;
            location.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Result.Success(MapToDto(location));
    }

    /// <summary>
    /// Verifica se <paramref name="status"/> é um valor definido do enum fechado
    /// <see cref="LocationStatus"/> (R9.8), rejeitando casts arbitrários de int.
    /// </summary>
    private static bool IsDefinedStatus(LocationStatus status) =>
        status is LocationStatus.Ativo or LocationStatus.Inativo;

    private static LocationDto MapToDto(Location l) => new(
        l.Id,
        l.TenantId,
        l.CustomerId,
        l.Nome,
        l.Endereco,
        l.Responsavel,
        l.Telefone,
        l.Email,
        l.Observacoes,
        l.Status,
        l.CreatedAt,
        l.UpdatedAt);
}
