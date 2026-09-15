using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Customers;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="ICustomerService"/> sobre o <see cref="AppDbContext"/>
/// (R8). Diferente da gestão de usuários, <see cref="Customer"/> é uma
/// <c>TenantEntity</c> e conta com o isolamento automático do ORM:
///
/// <list type="bullet">
///   <item><b>Leitura</b> (get/list): o filtro global de consulta por
///     <c>TenantId</c> restringe automaticamente as linhas ao tenant corrente
///     (R8.7/R8.8). Um cliente de outro tenant simplesmente não aparece na
///     consulta → mapeado para <see cref="CustomerErrors.NotFound"/> (HTTP 404),
///     sem revelar existência (não-vazamento).</item>
///   <item><b>Escrita</b> (create): o interceptor de <c>SaveChanges</c> carimba o
///     <c>TenantId</c> a partir do <see cref="ITenantContext"/> em entidades
///     <c>Added</c> (R8.1). Um guarda explícito verifica a existência de tenant e
///     retorna <see cref="CustomerErrors.NoTenantContext"/> (validação limpa) em
///     vez de deixar o interceptor lançar <c>CrossTenantAccessException</c>.</item>
/// </list>
///
/// <para><b>CNPJ (R8.4/R8.5).</b> O CNPJ é validado (formato + dígitos
/// verificadores) e normalizado (somente dígitos) por <see cref="CnpjValidator"/>
/// antes de qualquer escrita. A unicidade por tenant é verificada por consulta
/// (já escopada pelo filtro global) sobre o valor normalizado, com o índice único
/// composto <c>(TenantId, Cnpj)</c> como backstop no banco.</para>
///
/// <para><b>Ordenação da listagem.</b> Ordena por <c>RazaoSocial</c> (coluna
/// indexada e traduzível tanto no PostgreSQL quanto no SQLite dos testes),
/// evitando a limitação de ORDER BY sobre <c>DateTimeOffset</c> observada em
/// tarefas anteriores. O desempate por <c>Id</c> torna a ordem determinística.</para>
/// </summary>
public sealed class CustomerService : ICustomerService
{
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;

    /// <summary>
    /// Cria o serviço com o contexto de persistência scoped e o contexto de
    /// tenant da requisição corrente.
    /// </summary>
    public CustomerService(AppDbContext context, ITenantContext tenantContext)
    {
        _context = context;
        _tenantContext = tenantContext;
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> CreateAsync(CreateCustomerRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ct.ThrowIfCancellationRequested();

        // O tenant vem sempre do contexto (R8.1/R6.3). Guarda explícito para uma
        // falha de validação limpa em vez de deixar o interceptor lançar.
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null && !_tenantContext.IsSuperAdmin)
        {
            return Result.Failure<CustomerDto>(CustomerErrors.NoTenantContext);
        }

        if (string.IsNullOrWhiteSpace(req.RazaoSocial))
        {
            return Result.Failure<CustomerDto>(CustomerErrors.MissingRazaoSocial);
        }

        if (!IsDefinedStatus(req.Status))
        {
            return Result.Failure<CustomerDto>(CustomerErrors.InvalidStatus);
        }

        // Valida e normaliza o CNPJ (R8.4). Somente dígitos são persistidos.
        if (!CnpjValidator.TryNormalize(req.Cnpj, out var normalizedCnpj))
        {
            return Result.Failure<CustomerDto>(CustomerErrors.InvalidCnpj);
        }

        // Unicidade de CNPJ por tenant (R8.5). O filtro global já escopa a consulta
        // ao tenant corrente; o índice único é o backstop no banco.
        var exists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Cnpj == normalizedCnpj, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return Result.Failure<CustomerDto>(CustomerErrors.DuplicateCnpj);
        }

        var customer = new Customer
        {
            Id = Guid.NewGuid(),
            // TenantId é carimbado pelo interceptor de escrita a partir do contexto
            // (R8.1). Não é atribuído aqui para não conflitar com o Super Admin.
            RazaoSocial = req.RazaoSocial.Trim(),
            NomeFantasia = req.NomeFantasia,
            Cnpj = normalizedCnpj,
            InscricaoEstadual = req.InscricaoEstadual,
            Telefone = req.Telefone,
            Email = req.Email,
            Endereco = req.Endereco,
            Cidade = req.Cidade,
            Estado = req.Estado,
            Cep = req.Cep,
            Observacoes = req.Observacoes,
            Status = req.Status,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        _context.Customers.Add(customer);
        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success(MapToDto(customer));
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> UpdateAsync(Guid id, UpdateCustomerRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ct.ThrowIfCancellationRequested();

        // Filtro global escopa a busca ao tenant corrente; outro tenant → null → 404.
        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure<CustomerDto>(CustomerErrors.NotFound);
        }

        if (string.IsNullOrWhiteSpace(req.RazaoSocial))
        {
            return Result.Failure<CustomerDto>(CustomerErrors.MissingRazaoSocial);
        }

        // Revalida e normaliza o CNPJ (R8.4); reconfere unicidade quando muda (R8.5).
        if (!CnpjValidator.TryNormalize(req.Cnpj, out var normalizedCnpj))
        {
            return Result.Failure<CustomerDto>(CustomerErrors.InvalidCnpj);
        }

        if (!string.Equals(customer.Cnpj, normalizedCnpj, StringComparison.Ordinal))
        {
            var collision = await _context.Customers
                .AsNoTracking()
                .AnyAsync(c => c.Cnpj == normalizedCnpj && c.Id != customer.Id, ct)
                .ConfigureAwait(false);

            if (collision)
            {
                return Result.Failure<CustomerDto>(CustomerErrors.DuplicateCnpj);
            }

            customer.Cnpj = normalizedCnpj;
        }

        customer.RazaoSocial = req.RazaoSocial.Trim();
        customer.NomeFantasia = req.NomeFantasia;
        customer.InscricaoEstadual = req.InscricaoEstadual;
        customer.Telefone = req.Telefone;
        customer.Email = req.Email;
        customer.Endereco = req.Endereco;
        customer.Cidade = req.Cidade;
        customer.Estado = req.Estado;
        customer.Cep = req.Cep;
        customer.Observacoes = req.Observacoes;
        customer.UpdatedAt = DateTimeOffset.UtcNow;

        await _context.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success(MapToDto(customer));
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> GetAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var customer = await _context.Customers
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);

        return customer is null
            ? Result.Failure<CustomerDto>(CustomerErrors.NotFound)
            : Result.Success(MapToDto(customer));
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<CustomerDto>>> ListAsync(PageRequest query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        // O filtro global restringe as linhas ao tenant corrente (R8.8). Sem tenant
        // (e não Super Admin), o filtro não casa nenhuma linha.
        var baseQuery = _context.Customers.AsNoTracking();

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page - 1) * query.PageSize;

        // Ordenação determinística por RazaoSocial (coluna indexada e traduzível).
        var customers = await baseQuery
            .OrderBy(c => c.RazaoSocial)
            .ThenBy(c => c.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = customers.Select(MapToDto).ToList();

        return Result.Success(new PagedResult<CustomerDto>(items, query.Page, query.PageSize, totalCount));
    }

    /// <inheritdoc />
    public async Task<Result<CustomerDto>> ChangeStatusAsync(Guid id, CustomerStatus status, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        // Rejeita valores fora do conjunto fechado (R8.3) antes de qualquer escrita.
        if (!IsDefinedStatus(status))
        {
            return Result.Failure<CustomerDto>(CustomerErrors.InvalidStatus);
        }

        var customer = await _context.Customers
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            .ConfigureAwait(false);

        if (customer is null)
        {
            return Result.Failure<CustomerDto>(CustomerErrors.NotFound);
        }

        if (customer.Status != status)
        {
            customer.Status = status;
            customer.UpdatedAt = DateTimeOffset.UtcNow;
            await _context.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return Result.Success(MapToDto(customer));
    }

    /// <summary>
    /// Verifica se <paramref name="status"/> é um valor definido do enum fechado
    /// <see cref="CustomerStatus"/> (R8.3), rejeitando casts arbitrários de int.
    /// </summary>
    private static bool IsDefinedStatus(CustomerStatus status) =>
        status is CustomerStatus.Ativo or CustomerStatus.Inativo or CustomerStatus.Bloqueado;

    private static CustomerDto MapToDto(Customer c) => new(
        c.Id,
        c.TenantId,
        c.RazaoSocial,
        c.NomeFantasia,
        c.Cnpj,
        c.InscricaoEstadual,
        c.Telefone,
        c.Email,
        c.Endereco,
        c.Cidade,
        c.Estado,
        c.Cep,
        c.Observacoes,
        c.Status,
        c.CreatedAt,
        c.UpdatedAt);
}
