using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Tenancy;
using EasyPanel.Shared.Kernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EasyPanel.UnitTests.Infrastructure;

/// <summary>
/// Unit tests que verificam as convenções centralizadas do <see cref="AppDbContext"/>:
/// chave primária <see cref="Guid"/> em <see cref="BaseEntity"/>, enums persistidos
/// como <c>int</c> e <see cref="DateTimeOffset"/> normalizado para UTC
/// (design "Estratégia de tipos", R1).
///
/// As convenções são inspecionadas no modelo materializado, sem depender de uma
/// conexão viva com o PostgreSQL.
/// </summary>
public class AppDbContextConventionsTests
{
    private enum SampleStatus
    {
        Active = 0,
        Inactive = 1,
        Blocked = 2,
    }

    private sealed class SampleEntity : BaseEntity
    {
        public SampleStatus Status { get; set; }

        public SampleStatus? OptionalStatus { get; set; }

        public string Name { get; set; } = string.Empty;
    }

    /// <summary>
    /// Contexto de teste que reutiliza as convenções do <see cref="AppDbContext"/>
    /// e adiciona uma entidade de amostra para exercitá-las.
    /// </summary>
    private sealed class TestDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
        : AppDbContext(options, tenantContext)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SampleEntity>();
            base.OnModelCreating(modelBuilder);
        }
    }

    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test")
            .Options;

        using var context = new TestDbContext(options, NullTenantContext.Instance);
        return context.Model;
    }

    [Fact]
    public void BaseEntity_PrimaryKey_IsGuid()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(SampleEntity))!;

        var idProperty = entity.FindProperty(nameof(BaseEntity.Id))!;

        Assert.Equal(typeof(Guid), idProperty.ClrType);
        Assert.True(idProperty.IsPrimaryKey());
    }

    [Fact]
    public void BaseEntity_Id_IsNotStoreGenerated()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(SampleEntity))!;

        var idProperty = entity.FindProperty(nameof(BaseEntity.Id))!;

        Assert.Equal(ValueGenerated.Never, idProperty.ValueGenerated);
    }

    [Fact]
    public void Enum_IsPersistedAsInt()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(SampleEntity))!;

        var statusProperty = entity.FindProperty(nameof(SampleEntity.Status))!;

        Assert.Equal(typeof(int), statusProperty.GetProviderClrType());
    }

    [Fact]
    public void NullableEnum_IsPersistedAsNullableInt()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(SampleEntity))!;

        var statusProperty = entity.FindProperty(nameof(SampleEntity.OptionalStatus))!;

        Assert.Equal(typeof(int?), statusProperty.GetProviderClrType());
    }

    [Fact]
    public void DateTimeOffset_HasUtcConverter()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(SampleEntity))!;

        var createdAt = entity.FindProperty(nameof(BaseEntity.CreatedAt))!;

        var converter = createdAt.GetValueConverter();
        Assert.IsType<UtcDateTimeOffsetConverter>(converter);
    }

    [Fact]
    public void UtcConverter_NormalizesToUtc()
    {
        var converter = new UtcDateTimeOffsetConverter();

        var nonUtc = new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.FromHours(-3));
        var stored = (DateTimeOffset)converter.ConvertToProvider(nonUtc)!;

        Assert.Equal(TimeSpan.Zero, stored.Offset);
        Assert.Equal(nonUtc.UtcDateTime, stored.UtcDateTime);
    }
}
