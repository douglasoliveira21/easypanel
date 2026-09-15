using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Tenancy;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace EasyPanel.UnitTests.Infrastructure;

/// <summary>
/// Unit tests que verificam a configuração do ASP.NET Core Identity aplicada por
/// <see cref="IdentityServiceCollectionExtensions.AddPlatformIdentity"/>:
/// política de senha (R3.7) e política de bloqueio por tentativas (R4.3/R4.4).
///
/// As opções são inspecionadas a partir do container de DI materializado, sem
/// depender de banco de dados.
/// </summary>
public class PlatformIdentityOptionsTests
{
    private static IdentityOptions BuildIdentityOptions()
    {
        var services = new ServiceCollection();

        // AddIdentityCore/AddEntityFrameworkStores exigem o AppDbContext registrado.
        services.AddSingleton<ITenantContext>(NullTenantContext.Instance);
        services.AddDbContext<AppDbContext>(options =>
            options.UseNpgsql("Host=localhost;Database=test;Username=test;Password=test"));

        services.AddPlatformIdentity();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<IdentityOptions>>().Value;
    }

    [Fact]
    public void PasswordPolicy_MatchesRequirements()
    {
        var options = BuildIdentityOptions();

        Assert.Equal(8, options.Password.RequiredLength);
        Assert.True(options.Password.RequireUppercase);
        Assert.True(options.Password.RequireLowercase);
        Assert.True(options.Password.RequireDigit);
        Assert.True(options.Password.RequireNonAlphanumeric);
    }

    [Fact]
    public void LockoutPolicy_MatchesRequirements()
    {
        var options = BuildIdentityOptions();

        Assert.Equal(5, options.Lockout.MaxFailedAccessAttempts);
        Assert.Equal(TimeSpan.FromMinutes(15), options.Lockout.DefaultLockoutTimeSpan);
        Assert.True(options.Lockout.AllowedForNewUsers);
    }

    [Fact]
    public void EmailUniqueness_IsNotGlobal()
    {
        var options = BuildIdentityOptions();

        // Unicidade de email é por tenant (índice composto), não global.
        Assert.False(options.User.RequireUniqueEmail);
    }
}
