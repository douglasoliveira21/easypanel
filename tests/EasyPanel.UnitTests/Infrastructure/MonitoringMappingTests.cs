using EasyPanel.Infrastructure.Persistence;
using EasyPanel.Modules.Monitoring;
using EasyPanel.Shared.Kernel.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace EasyPanel.UnitTests.Infrastructure;

/// <summary>
/// Unit tests das convenções de mapeamento das entidades de monitoramento (Fase 2):
/// chave primária <see cref="Guid"/>, enums persistidos como <c>int</c>, carimbos
/// <see cref="DateTimeOffset"/> em UTC e presença dos índices de idempotência e de
/// cursor exigidos (R13.1, R11.7, R17.7). Inspeciona o modelo materializado, sem
/// depender de conexão viva com o PostgreSQL.
/// </summary>
public class MonitoringMappingTests
{
    private static IModel BuildModel()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test")
            .Options;

        using var context = new AppDbContext(options, NullTenantContext.Instance);
        return context.Model;
    }

    [Theory]
    [InlineData(typeof(WindowsClient))]
    [InlineData(typeof(Printer))]
    [InlineData(typeof(PrinterCounter))]
    [InlineData(typeof(PrinterEvent))]
    [InlineData(typeof(Collection))]
    [InlineData(typeof(PrinterMovement))]
    [InlineData(typeof(IngestionDedup))]
    public void Entity_PrimaryKey_IsGuid(Type clrType)
    {
        var model = BuildModel();
        var entity = model.FindEntityType(clrType)!;

        var id = entity.FindProperty(nameof(BaseEntity.Id))!;

        Assert.Equal(typeof(Guid), id.ClrType);
        Assert.True(id.IsPrimaryKey());
        Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
    }

    [Theory]
    [InlineData(typeof(Printer), nameof(Printer.Status))]
    [InlineData(typeof(WindowsClient), nameof(WindowsClient.State))]
    [InlineData(typeof(PrinterCounter), nameof(PrinterCounter.CounterType))]
    [InlineData(typeof(PrinterCounter), nameof(PrinterCounter.Source))]
    [InlineData(typeof(PrinterEvent), nameof(PrinterEvent.Type))]
    [InlineData(typeof(Collection), nameof(Collection.Result))]
    [InlineData(typeof(PrinterMovement), nameof(PrinterMovement.Operation))]
    public void Enum_IsPersistedAsInt(Type clrType, string propertyName)
    {
        var model = BuildModel();
        var entity = model.FindEntityType(clrType)!;

        var property = entity.FindProperty(propertyName)!;

        Assert.Equal(typeof(int), property.GetProviderClrType());
    }

    [Theory]
    [InlineData(typeof(Collection), nameof(Collection.StartedAt))]
    [InlineData(typeof(PrinterCounter), nameof(PrinterCounter.Timestamp))]
    [InlineData(typeof(PrinterEvent), nameof(PrinterEvent.OccurredAt))]
    public void DateTimeOffset_HasUtcConverter(Type clrType, string propertyName)
    {
        var model = BuildModel();
        var entity = model.FindEntityType(clrType)!;

        var property = entity.FindProperty(propertyName)!;

        Assert.IsType<UtcDateTimeOffsetConverter>(property.GetValueConverter());
    }

    [Fact]
    public void Collection_HasUniqueIdempotencyIndex()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(Collection))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(TenantEntity.TenantId),
                nameof(Collection.IdempotencyKey),
            }));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void IngestionDedup_HasUniqueIdempotencyIndex()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(IngestionDedup))!;

        var index = entity.GetIndexes().Single(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(TenantEntity.TenantId),
                nameof(IngestionDedup.IdempotencyKey),
            }));

        Assert.True(index.IsUnique);
    }

    [Fact]
    public void PrinterCounter_HasCursorIndex()
    {
        var model = BuildModel();
        var entity = model.FindEntityType(typeof(PrinterCounter))!;

        var hasCursorIndex = entity.GetIndexes().Any(i =>
            i.Properties.Select(p => p.Name).SequenceEqual(new[]
            {
                nameof(TenantEntity.TenantId),
                nameof(PrinterCounter.PrinterId),
                nameof(PrinterCounter.CounterType),
                nameof(PrinterCounter.TimestampTicks),
            }));

        Assert.True(hasCursorIndex);
    }
}
