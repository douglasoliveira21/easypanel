namespace EasyPanel.Modules.Customers;

/// <summary>
/// Validação e normalização de CNPJ (R8.4). O CNPJ é o identificador fiscal
/// brasileiro de empresas: 14 dígitos, dos quais os dois últimos são dígitos
/// verificadores calculados por módulo 11 sobre os 12 primeiros.
///
/// <para>Regras aplicadas:</para>
/// <list type="bullet">
///   <item><b>Normalização:</b> remove qualquer caractere não numérico
///     (pontuação, espaços), produzindo a forma canônica de 14 dígitos.</item>
///   <item><b>Comprimento:</b> exige exatamente 14 dígitos após a normalização.</item>
///   <item><b>Sequência repetida:</b> rejeita CNPJs com todos os dígitos iguais
///     (ex.: <c>00000000000000</c>), que satisfazem os dígitos verificadores mas
///     não são válidos.</item>
///   <item><b>Dígitos verificadores:</b> confere os dois dígitos verificadores
///     pelo algoritmo padrão de módulo 11.</item>
/// </list>
///
/// A entidade <see cref="Customer"/> armazena sempre a forma normalizada, de modo
/// que a unicidade por tenant (R8.5) é estável independentemente da formatação de
/// entrada.
/// </summary>
public static class CnpjValidator
{
    /// <summary>Quantidade de dígitos de um CNPJ.</summary>
    public const int Length = 14;

    // Pesos do algoritmo de módulo 11 para o primeiro e o segundo dígito
    // verificador (aplicados da esquerda para a direita).
    private static readonly int[] FirstWeights = { 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };
    private static readonly int[] SecondWeights = { 6, 5, 4, 3, 2, 9, 8, 7, 6, 5, 4, 3, 2 };

    /// <summary>
    /// Remove todos os caracteres não numéricos de <paramref name="input"/>,
    /// retornando somente os dígitos. Não valida comprimento nem dígitos
    /// verificadores — apenas canonicaliza a representação. Retorna string vazia
    /// para entrada nula/vazia.
    /// </summary>
    public static string Normalize(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return string.Empty;
        }

        Span<char> digits = input.Length <= 256 ? stackalloc char[input.Length] : new char[input.Length];
        var count = 0;

        foreach (var c in input)
        {
            if (c is >= '0' and <= '9')
            {
                digits[count++] = c;
            }
        }

        return count == 0 ? string.Empty : new string(digits[..count]);
    }

    /// <summary>
    /// Indica se <paramref name="input"/> corresponde a um CNPJ válido após a
    /// normalização: exatamente 14 dígitos, não todos iguais, e com os dois
    /// dígitos verificadores corretos (R8.4).
    /// </summary>
    public static bool IsValid(string? input) => IsNormalizedValid(Normalize(input));

    /// <summary>
    /// Tenta normalizar <paramref name="input"/> e valida o resultado. Em caso de
    /// sucesso, <paramref name="normalized"/> recebe a forma canônica de 14
    /// dígitos, pronta para persistência (R8.4). Em caso de falha,
    /// <paramref name="normalized"/> recebe string vazia.
    /// </summary>
    public static bool TryNormalize(string? input, out string normalized)
    {
        var candidate = Normalize(input);
        if (IsNormalizedValid(candidate))
        {
            normalized = candidate;
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    /// <summary>
    /// Valida um valor já normalizado (somente dígitos). Separado para evitar
    /// re-normalizar quando o chamador já dispõe da forma canônica.
    /// </summary>
    private static bool IsNormalizedValid(string digits)
    {
        if (digits.Length != Length)
        {
            return false;
        }

        if (AllDigitsEqual(digits))
        {
            return false;
        }

        var firstCheck = ComputeCheckDigit(digits, FirstWeights, FirstWeights.Length);
        if (digits[12] - '0' != firstCheck)
        {
            return false;
        }

        var secondCheck = ComputeCheckDigit(digits, SecondWeights, SecondWeights.Length);
        return digits[13] - '0' == secondCheck;
    }

    private static bool AllDigitsEqual(string digits)
    {
        var first = digits[0];
        for (var i = 1; i < digits.Length; i++)
        {
            if (digits[i] != first)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Calcula um dígito verificador por módulo 11 sobre os primeiros
    /// <paramref name="count"/> dígitos usando os <paramref name="weights"/>.
    /// </summary>
    private static int ComputeCheckDigit(string digits, int[] weights, int count)
    {
        var sum = 0;
        for (var i = 0; i < count; i++)
        {
            sum += (digits[i] - '0') * weights[i];
        }

        var remainder = sum % 11;
        return remainder < 2 ? 0 : 11 - remainder;
    }
}
