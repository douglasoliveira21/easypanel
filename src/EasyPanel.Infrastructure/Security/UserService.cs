using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Auditing;
using EasyPanel.Modules.Identity;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Pagination;
using EasyPanel.Shared.Kernel.Results;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EasyPanel.Infrastructure.Security;

/// <summary>
/// Implementação de <see cref="IUserService"/> sobre o ASP.NET Core Identity
/// (<see cref="UserManager{TUser}"/>) e o <see cref="AppDbContext"/> (R7).
///
/// <para><b>Isolamento por tenant explícito (R7.1/R7.3/R7.7).</b> Diferente das
/// entidades de negócio, <see cref="ApplicationUser"/> não é uma <c>TenantEntity</c>
/// e, portanto, não recebe filtro global de ORM. Toda leitura/escrita de usuário é
/// restringida ao tenant do <see cref="ITenantContext"/> por comparação explícita de
/// <c>TenantId</c> — exatamente como faz a consulta da trilha de auditoria. Um
/// usuário de outro tenant é indistinguível de um inexistente
/// (<see cref="UserErrors.NotFound"/> → HTTP 404), evitando enumeração cross-tenant
/// (não-vazamento).</para>
///
/// <para><b>Senha e política (R3.7/R2.8).</b> A criação usa
/// <see cref="UserManager{TUser}.CreateAsync(TUser,string)"/>, de modo que a senha é
/// validada contra a política e hasheada (PBKDF2) pelo Identity. A senha jamais é
/// persistida em texto claro nem incluída na auditoria (R11.2).</para>
///
/// <para><b>Desativação (R7.4).</b> Marca <c>IsActive = false</c> e chama
/// <see cref="ITokenService.RevokeAllForUserAsync"/> para invalidar as sessões
/// ativas do usuário; a recusa de login enquanto inativo é aplicada pelo
/// <see cref="IAuthService"/> (R7.5).</para>
/// </summary>
public sealed class UserService : IUserService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AppDbContext _context;
    private readonly ITenantContext _tenantContext;
    private readonly ITokenService _tokenService;
    private readonly IAuditLogger _auditLogger;
    private readonly ICurrentUserAccessor _currentUser;

    /// <summary>
    /// Cria o serviço com o gerenciador de usuários, o contexto de persistência
    /// scoped, o contexto de tenant, o serviço de tokens, o logger de auditoria e
    /// o acessor do usuário atuante.
    ///
    /// <para>O <paramref name="currentUser"/> é opcional: composições sem pipeline
    /// HTTP (testes de serviço, tarefas de fundo) podem não registrar um
    /// <see cref="ICurrentUserAccessor"/> — nesse caso o serviço usa o
    /// <see cref="NullCurrentUserAccessor"/> e a auditoria segue funcionando (R7.8),
    /// apenas sem <c>ActorUserId</c>. Em produção, o acessor baseado em
    /// <c>HttpContext</c> resolve o ator a partir do claim <c>sub</c> (R6.3).</para>
    /// </summary>
    public UserService(
        UserManager<ApplicationUser> userManager,
        AppDbContext context,
        ITenantContext tenantContext,
        ITokenService tokenService,
        IAuditLogger auditLogger,
        ICurrentUserAccessor? currentUser = null)
    {
        _userManager = userManager;
        _context = context;
        _tenantContext = tenantContext;
        _tokenService = tokenService;
        _auditLogger = auditLogger;
        _currentUser = currentUser ?? NullCurrentUserAccessor.Instance;
    }

    /// <inheritdoc />
    public async Task<Result<UserDto>> CreateAsync(CreateUserRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ct.ThrowIfCancellationRequested();

        // O tenant do novo usuário vem sempre do contexto (R6.3/R7.1), nunca do
        // chamador. Sem tenant resolvido, não há a que vincular o usuário.
        var tenantId = _tenantContext.TenantId;
        if (tenantId is null)
        {
            return Result.Failure<UserDto>(UserErrors.NoTenantContext);
        }

        // Papéis devem pertencer ao catálogo fixo (R5.1); rejeita desconhecidos
        // antes de qualquer escrita.
        if (!AreKnownRoles(req.Roles))
        {
            return Result.Failure<UserDto>(UserErrors.UnknownRole);
        }

        // Vínculo usuário↔Cliente (Fase 10 — R1.1/R1.2/R1.3): valida antes de
        // qualquer escrita e resolve o CustomerId efetivo a persistir.
        var customerValidation = await ValidateCustomerAsync(req.Roles, req.CustomerId, tenantId.Value, ct)
            .ConfigureAwait(false);
        if (customerValidation.IsFailure)
        {
            return Result.Failure<UserDto>(customerValidation.Error);
        }

        // Unicidade de email por tenant (R7.2): pré-checagem explícita por
        // NormalizedEmail + TenantId. O índice único composto é o backstop no banco.
        var normalizedEmail = _userManager.NormalizeEmail(req.Email);
        var exists = await _context.Users
            .AsNoTracking()
            .AnyAsync(u => u.TenantId == tenantId && u.NormalizedEmail == normalizedEmail, ct)
            .ConfigureAwait(false);

        if (exists)
        {
            return Result.Failure<UserDto>(UserErrors.DuplicateEmail);
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            // O ASP.NET Identity impõe unicidade GLOBAL de UserName (via
            // UserValidator), enquanto a unicidade de email é POR TENANT (índice
            // composto (TenantId, NormalizedEmail); RequireUniqueEmail=false). Se
            // usássemos UserName = Email, o mesmo email não poderia existir em
            // tenants distintos — contrariando R7.2/design. Por isso o UserName é
            // qualificado pelo tenant ("{tenantId}:{email}"), preservando a
            // unicidade global exigida pelo Identity e mapeando-a exatamente à
            // unicidade por tenant do email. O Email exposto permanece o valor
            // legível; o login resolve por email (FindByEmailAsync), não por
            // UserName, portanto o formato interno do UserName não afeta a
            // autenticação.
            UserName = BuildUserName(tenantId.Value, req.Email),
            Email = req.Email,
            CustomerId = customerValidation.Value,
            IsActive = true,
            MfaEnabled = false,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        // CreateAsync aplica a política de senha (R3.7) e hasheia via PBKDF2 (R2.8).
        var created = await _userManager.CreateAsync(user, req.Password).ConfigureAwait(false);
        if (!created.Succeeded)
        {
            return Result.Failure<UserDto>(MapCreateFailure(created));
        }

        if (req.Roles.Count > 0)
        {
            var rolesResult = await _userManager.AddToRolesAsync(user, req.Roles).ConfigureAwait(false);
            if (!rolesResult.Succeeded)
            {
                // Papel válido no catálogo mas ainda não semeado, ou outra falha de
                // atribuição: desfaz o usuário recém-criado para não deixar estado
                // parcial e reporta erro de papel.
                await _userManager.DeleteAsync(user).ConfigureAwait(false);
                return Result.Failure<UserDto>(UserErrors.UnknownRole);
            }
        }

        var dto = await BuildDtoAsync(user).ConfigureAwait(false);

        // Auditoria da criação (R7.8). NewValues capturado com redaction (nunca a
        // senha/hash — R11.2). O ActorUserId é enriquecido a partir do
        // ICurrentUserAccessor (claim `sub` do token — R6.3).
        await AuditAsync(
            AuditActions.UserCreate,
            tenantId,
            user.Id,
            oldValues: null,
            newValues: dto,
            ct).ConfigureAwait(false);

        return Result.Success(dto);
    }

    /// <inheritdoc />
    public async Task<Result<UserDto>> UpdateAsync(Guid id, UpdateUserRequest req, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(req);
        ct.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;

        var user = await FindInTenantAsync(id, tenantId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure<UserDto>(UserErrors.NotFound);
        }

        if (!AreKnownRoles(req.Roles))
        {
            return Result.Failure<UserDto>(UserErrors.UnknownRole);
        }

        // Vínculo usuário↔Cliente (Fase 10 — R1.1/R1.2/R1.3): revalida a cada
        // atualização, já que os papéis desejados podem estar mudando.
        var customerValidation = await ValidateCustomerAsync(req.Roles, req.CustomerId, tenantId!.Value, ct)
            .ConfigureAwait(false);
        if (customerValidation.IsFailure)
        {
            return Result.Failure<UserDto>(customerValidation.Error);
        }

        var before = await BuildDtoAsync(user).ConfigureAwait(false);

        // Alteração de email: valida unicidade por tenant (R7.2) quando muda.
        var normalizedEmail = _userManager.NormalizeEmail(req.Email);
        if (!string.Equals(user.NormalizedEmail, normalizedEmail, StringComparison.Ordinal))
        {
            var collision = await _context.Users
                .AsNoTracking()
                .AnyAsync(
                    u => u.TenantId == tenantId && u.NormalizedEmail == normalizedEmail && u.Id != user.Id,
                    ct)
                .ConfigureAwait(false);

            if (collision)
            {
                return Result.Failure<UserDto>(UserErrors.DuplicateEmail);
            }

            user.Email = req.Email;
            // UserName mantém-se qualificado pelo tenant para preservar a
            // unicidade global do Identity mapeada à unicidade por tenant do email.
            user.UserName = BuildUserName(tenantId!.Value, req.Email);
        }

        user.MfaEnabled = req.MfaEnabled;
        user.CustomerId = customerValidation.Value;

        var updated = await _userManager.UpdateAsync(user).ConfigureAwait(false);
        if (!updated.Succeeded)
        {
            return Result.Failure<UserDto>(MapCreateFailure(updated));
        }

        // Reconcilia papéis: adiciona ausentes, remove excedentes.
        var rolesChanged = await ReconcileRolesAsync(user, req.Roles).ConfigureAwait(false);

        var after = await BuildDtoAsync(user).ConfigureAwait(false);

        // Auditoria da atualização (R7.8), com old/new redigidos.
        await AuditAsync(
            AuditActions.UserUpdate,
            tenantId,
            user.Id,
            oldValues: before,
            newValues: after,
            ct).ConfigureAwait(false);

        // Alteração de papéis é auditada como evento próprio (R7.8).
        if (rolesChanged)
        {
            await AuditAsync(
                AuditActions.UserRolesUpdate,
                tenantId,
                user.Id,
                oldValues: new RolesSnapshot(before.Roles),
                newValues: new RolesSnapshot(after.Roles),
                ct).ConfigureAwait(false);
        }

        return Result.Success(after);
    }

    /// <inheritdoc />
    public async Task<Result> DeactivateAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;

        var user = await FindInTenantAsync(id, tenantId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        if (user.IsActive)
        {
            user.IsActive = false;
            var updated = await _userManager.UpdateAsync(user).ConfigureAwait(false);
            if (!updated.Succeeded)
            {
                return Result.Failure(UserErrors.InvalidData);
            }
        }

        // Invalida as sessões ativas do usuário desativado (R7.4). Idempotente.
        await _tokenService.RevokeAllForUserAsync(user.Id, ct).ConfigureAwait(false);

        await AuditAsync(AuditActions.UserDeactivate, tenantId, user.Id, ct).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result> ReactivateAsync(Guid id, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;

        var user = await FindInTenantAsync(id, tenantId, ct).ConfigureAwait(false);
        if (user is null)
        {
            return Result.Failure(UserErrors.NotFound);
        }

        if (!user.IsActive)
        {
            user.IsActive = true;
            var updated = await _userManager.UpdateAsync(user).ConfigureAwait(false);
            if (!updated.Succeeded)
            {
                return Result.Failure(UserErrors.InvalidData);
            }
        }

        await AuditAsync(AuditActions.UserReactivate, tenantId, user.Id, ct).ConfigureAwait(false);

        return Result.Success();
    }

    /// <inheritdoc />
    public async Task<Result<PagedResult<UserDto>>> ListAsync(PageRequest query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        ct.ThrowIfCancellationRequested();

        var tenantId = _tenantContext.TenantId;

        // Sem tenant resolvido: nada a listar (R7.7). Não vaza usuários de outros
        // tenants nem eventos de plataforma.
        if (tenantId is null)
        {
            return Result.Success(
                new PagedResult<UserDto>(Array.Empty<UserDto>(), query.Page, query.PageSize, TotalCount: 0));
        }

        // Restrição explícita por tenant (R7.7): usuários não têm filtro global.
        var baseQuery = _context.Users
            .AsNoTracking()
            .Where(u => u.TenantId == tenantId);

        var totalCount = await baseQuery.LongCountAsync(ct).ConfigureAwait(false);

        var skip = (query.Page - 1) * query.PageSize;

        // Ordenação determinística por NormalizedEmail: coluna traduzível em ambos
        // os provedores (PostgreSQL e SQLite dos testes), diferente de DateTimeOffset.
        var users = await baseQuery
            .OrderBy(u => u.NormalizedEmail)
            .ThenBy(u => u.Id)
            .Skip(skip)
            .Take(query.PageSize)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = new List<UserDto>(users.Count);
        foreach (var user in users)
        {
            items.Add(await BuildDtoAsync(user).ConfigureAwait(false));
        }

        return Result.Success(new PagedResult<UserDto>(items, query.Page, query.PageSize, totalCount));
    }

    /// <summary>
    /// Deriva um <c>UserName</c> globalmente único a partir do tenant e do email,
    /// mapeando a unicidade global exigida pelo Identity à unicidade por tenant do
    /// email (R7.2). Formato: <c>{tenantId}:{email}</c>.
    /// </summary>
    private static string BuildUserName(Guid tenantId, string email) => $"{tenantId:D}:{email}";

    private static bool AreKnownRoles(IReadOnlyList<string> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);

        foreach (var role in roles)
        {
            if (!Roles.All.Contains(role, StringComparer.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Localiza um usuário garantindo que pertence ao tenant informado (R7.3). Um
    /// usuário de outro tenant é tratado como inexistente (retorna <c>null</c>).
    /// Retorna a instância rastreada para permitir atualização subsequente.
    /// </summary>
    private async Task<ApplicationUser?> FindInTenantAsync(Guid id, Guid? tenantId, CancellationToken ct)
    {
        if (tenantId is null)
        {
            return null;
        }

        return await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id && u.TenantId == tenantId, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reconcilia os papéis do usuário com o conjunto desejado: adiciona os
    /// ausentes e remove os excedentes. Retorna <c>true</c> se houve qualquer
    /// alteração de papéis.
    /// </summary>
    private async Task<bool> ReconcileRolesAsync(ApplicationUser user, IReadOnlyList<string> desiredRoles)
    {
        var current = await _userManager.GetRolesAsync(user).ConfigureAwait(false);

        var toAdd = desiredRoles.Except(current, StringComparer.Ordinal).ToList();
        var toRemove = current.Except(desiredRoles, StringComparer.Ordinal).ToList();

        if (toRemove.Count > 0)
        {
            await _userManager.RemoveFromRolesAsync(user, toRemove).ConfigureAwait(false);
        }

        if (toAdd.Count > 0)
        {
            await _userManager.AddToRolesAsync(user, toAdd).ConfigureAwait(false);
        }

        return toAdd.Count > 0 || toRemove.Count > 0;
    }

    private async Task<UserDto> BuildDtoAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user).ConfigureAwait(false);

        return new UserDto(
            user.Id,
            user.Email,
            user.TenantId,
            user.CustomerId,
            user.IsActive,
            user.MfaEnabled,
            roles.OrderBy(r => r, StringComparer.Ordinal).ToList(),
            user.CreatedAt);
    }

    /// <summary>
    /// Valida o vínculo usuário↔Cliente (Fase 10 — R1.1/R1.2/R1.3) a partir dos
    /// papéis desejados e resolve o <c>CustomerId</c> efetivo a persistir.
    ///
    /// <list type="bullet">
    ///   <item>IF <paramref name="roles"/> contém <see cref="Roles.Cliente"/>:
    ///   exige exatamente esse papel (nenhum outro combinado — R1.2) e um
    ///   <paramref name="customerId"/> que exista no <paramref name="tenantId"/>
    ///   corrente (R1.1).</item>
    ///   <item>Caso contrário: o <c>CustomerId</c> efetivo é sempre <c>null</c>
    ///   (R1.3), mesmo se informado.</item>
    /// </list>
    /// </summary>
    private async Task<Result<Guid?>> ValidateCustomerAsync(
        IReadOnlyList<string> roles, Guid? customerId, Guid tenantId, CancellationToken ct)
    {
        var hasClienteRole = roles.Contains(Roles.Cliente, StringComparer.Ordinal);

        if (!hasClienteRole)
        {
            return Result.Success<Guid?>(null);
        }

        if (roles.Count > 1)
        {
            return Result.Failure<Guid?>(UserErrors.RoleConflict);
        }

        if (customerId is not { } resolvedCustomerId)
        {
            return Result.Failure<Guid?>(UserErrors.CustomerRequired);
        }

        var customerExists = await _context.Customers
            .AsNoTracking()
            .AnyAsync(c => c.Id == resolvedCustomerId && c.TenantId == tenantId, ct)
            .ConfigureAwait(false);

        if (!customerExists)
        {
            return Result.Failure<Guid?>(UserErrors.CustomerNotFound);
        }

        return Result.Success<Guid?>(resolvedCustomerId);
    }

    /// <summary>
    /// Traduz uma falha do Identity ao criar/atualizar o usuário em um
    /// <see cref="Error"/> de domínio. Falhas de política de senha viram
    /// <see cref="UserErrors.WeakPassword"/> (R3.7); as demais (email inválido,
    /// duplicidade detectada pelo store) viram <see cref="UserErrors.InvalidData"/>.
    /// </summary>
    private static Error MapCreateFailure(IdentityResult result)
    {
        var codes = result.Errors.Select(e => e.Code).ToArray();

        if (codes.Any(c => c.StartsWith("Password", StringComparison.Ordinal)))
        {
            return UserErrors.WeakPassword;
        }

        if (codes.Any(c => c.Contains("Email", StringComparison.Ordinal)))
        {
            return UserErrors.InvalidData;
        }

        return UserErrors.InvalidData;
    }

    /// <summary>
    /// Audita uma mutação sem old/new (ex.: desativação/reativação — R7.8). Apenas
    /// o resultado, o ator (não resolvível aqui), o tenant e o recurso são gravados.
    /// </summary>
    private Task AuditAsync(string action, Guid? tenantId, Guid userId, CancellationToken ct) =>
        AuditAsync<object>(action, tenantId, userId, oldValues: null, newValues: null, ct);

    private Task AuditAsync<T>(
        string action,
        Guid? tenantId,
        Guid userId,
        T? oldValues,
        T? newValues,
        CancellationToken ct)
        where T : class
    {
        var entry = new AuditEntry
        {
            // Ator resolvido do ClaimsPrincipal autenticado (claim `sub`) via
            // ICurrentUserAccessor (R7.8/R6.3). Null quando não há requisição HTTP.
            ActorUserId = _currentUser.UserId,
            TenantId = tenantId,
            Action = action,
            ResourceType = AuditActions.UserResourceType,
            ResourceId = userId.ToString(),
            OldValues = AuditValueRedactor.Serialize(oldValues),
            NewValues = AuditValueRedactor.Serialize(newValues),
            Result = AuditResult.Success,
        };

        return _auditLogger.LogAsync(entry, ct);
    }

    /// <summary>
    /// Retrato de papéis para a auditoria de alteração de papéis (R7.8), mantendo
    /// old/new legíveis como JSON.
    /// </summary>
    private sealed record RolesSnapshot(IReadOnlyList<string> Roles);
}
