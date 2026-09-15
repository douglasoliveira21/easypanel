using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Fábrica de design-time usada pelas ferramentas do EF Core (dotnet ef) para
/// criar o <see cref="AppDbContext"/> ao gerar migrações, sem depender do host
/// da aplicação nem de uma conexão viva com o banco.
///
/// A cadeia de conexão pode ser fornecida pela variável de ambiente
/// <c>EASYPANEL_DESIGNTIME_CONNECTION</c>; caso ausente, usa um placeholder
/// que é suficiente para o scaffolding de migrações (nenhuma conexão é aberta).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("EASYPANEL_DESIGNTIME_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=easypanel;Username=easypanel;Password=easypanel";

        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(
            connectionString,
            npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

        // As ferramentas de design-time não têm um contexto de requisição; um
        // ITenantContext inerte é suficiente pois o filtro/interceptor de tenant
        // não afetam o schema gerado nas migrações.
        return new AppDbContext(optionsBuilder.Options, NullTenantContext.Instance);
    }
}
