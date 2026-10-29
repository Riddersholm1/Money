namespace Riddersholm.Money.DataSync;

/// <summary>
/// Minimal RFC 4180 reader. The ISO list contains quoted fields with embedded commas
/// ("BOLIVIA, PLURINATIONAL STATE OF"), so naive splitting silently corrupts the data.
/// </summary>
internal static class Csv
{
    public static List<Dictionary<string, string>> Parse(string content)
    {
        List<string[]> rows = [];
        List<string> fields = [];
        System.Text.StringBuilder field = new();
        bool inQuotes = false;

        for (int i = 0; i < content.Length; i++)
        {
            char c = content[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // A doubled quote inside a quoted field is a literal quote.
                    if (i + 1 < content.Length && content[i + 1] == '"')
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
                    break;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    break;
                case '\r':
                    break;
                case '\n':
                    fields.Add(field.ToString());
                    field.Clear();
                    rows.Add([.. fields]);
                    fields.Clear();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || fields.Count > 0)
        {
            fields.Add(field.ToString());
            rows.Add([.. fields]);
        }

        if (rows.Count == 0)
        {
            return [];
        }

        string[] header = rows[0];
        List<Dictionary<string, string>> result = new(rows.Count - 1);

        foreach (string[] row in rows.Skip(1))
        {
            Dictionary<string, string> record = new(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < header.Length; i++)
            {
                record[header[i].Trim()] = i < row.Length ? row[i].Trim() : string.Empty;
            }

            result.Add(record);
        }

        return result;
    }
}
