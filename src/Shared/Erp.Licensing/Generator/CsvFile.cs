using System.Text;

namespace Erp.Licensing.Generator;

/// <summary>
/// Citirea și scrierea CSV pentru registrul licențelor (3.6): câmpuri între ghilimele când
/// e nevoie, ghilimelele dublate, rânduri care pot conține separatorul sau sfârșit de rând.
/// </summary>
internal static class CsvFile
{
    public static string WriteRow(IReadOnlyList<string> fields, char delimiter)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < fields.Count; i++)
        {
            if (i > 0)
            {
                sb.Append(delimiter);
            }

            sb.Append(Quote(fields[i], delimiter));
        }

        return sb.ToString();
    }

    public static IEnumerable<IReadOnlyList<string>> ReadRows(string text, char delimiter)
    {
        ArgumentNullException.ThrowIfNull(text);

        var fields = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;
        var hasContent = false;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    hasContent = true;
                    break;

                case '﻿':
                    break;                                  // BOM la începutul fișierului

                case '\r':
                    break;

                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    if (hasContent || fields.Count > 1 || fields[0].Length > 0)
                    {
                        yield return fields.ToArray();
                    }

                    fields.Clear();
                    hasContent = false;
                    break;

                default:
                    if (c == delimiter)
                    {
                        fields.Add(field.ToString());
                        field.Clear();
                    }
                    else
                    {
                        field.Append(c);
                        hasContent = true;
                    }

                    break;
            }
        }

        if (hasContent || field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            yield return fields.ToArray();
        }
    }

    private static string Quote(string value, char delimiter)
    {
        value ??= string.Empty;

        var needsQuotes = value.Contains(delimiter)
            || value.Contains('"')
            || value.Contains('\n')
            || value.Contains('\r')
            || value.StartsWith(' ')
            || value.EndsWith(' ');

        return needsQuotes
            ? string.Concat("\"", value.Replace("\"", "\"\"", StringComparison.Ordinal), "\"")
            : value;
    }
}
