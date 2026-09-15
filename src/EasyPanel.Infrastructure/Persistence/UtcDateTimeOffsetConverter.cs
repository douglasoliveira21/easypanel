using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace EasyPanel.Infrastructure.Persistence;

/// <summary>
/// Conversor de valor que normaliza <see cref="DateTimeOffset"/> para UTC na
/// escrita e o materializa em UTC na leitura, garantindo que todos os carimbos
/// temporais persistidos sejam consistentes independentemente do fuso local
/// (design "Estratégia de tipos": timestamps em UTC).
/// </summary>
public sealed class UtcDateTimeOffsetConverter : ValueConverter<DateTimeOffset, DateTimeOffset>
{
    public UtcDateTimeOffsetConverter()
        : base(
            value => value.ToUniversalTime(),
            value => value.ToUniversalTime())
    {
    }
}
