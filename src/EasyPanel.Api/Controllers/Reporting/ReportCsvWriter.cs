using System.Text;

namespace EasyPanel.Api.Controllers.Reporting;

/// <summary>
/// Escreve CSV UTF-8 a partir de cabeçalhos e linhas já formatadas (Fase 9 —
/// R5). Campos contendo vírgula, aspas ou quebra de linha são colocados
/// entre aspas, com aspas internas duplicadas (RFC 4180).
/// </summary>
internal static class ReportCsvWriter
{
    public static byte[] Write(IReadOnlyList<string> headers, IEnumerable<IReadOnlyList<string>> rows)
    {
        var builder = new StringBuilder();
        AppendRow(builder, headers);
        foreach (var row in rows)
        {
            AppendRow(builder, row);
        }

        return Encoding.UTF8.GetBytes(builder.ToString());
    }

    private static void AppendRow(StringBuilder builder, IReadOnlyList<string> fields)
    {
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            builder.Append(Escape(fields[i]));
        }

        builder.Append("\r\n");
    }

    private static string Escape(string field)
    {
        if (field.IndexOfAny([',', '"', '\r', '\n']) < 0)
        {
            return field;
        }

        return $"\"{field.Replace("\"", "\"\"")}\"";
    }
}
