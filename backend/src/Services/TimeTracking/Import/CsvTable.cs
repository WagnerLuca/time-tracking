using System.Text;

namespace TimeTracking.Api.Services;

/// <summary>
/// A decoded CSV file: header fields plus the non-empty data lines (1-based row numbers counted
/// from the first data line, like the preview shows them). The delimiter is detected from the
/// header line (<c>;</c>, <c>,</c> or tab, whichever occurs most outside quotes; <c>,</c> on a tie).
/// </summary>
public sealed record CsvTable(char Delimiter, IReadOnlyList<string> Headers, IReadOnlyList<CsvRow> Rows)
{
    /// <summary>Header names trimmed and lower-cased, for column lookups.</summary>
    public IReadOnlyList<string> NormalizedHeaders => _normalized;
    private readonly List<string> _normalized = Headers.Select(h => h.Trim().ToLowerInvariant()).ToList();

    /// <summary>Index of the first header matching one of the names (case-insensitive), or -1.</summary>
    public int Column(params string[] names)
    {
        foreach (var name in names)
        {
            var idx = _normalized.IndexOf(name.ToLowerInvariant());
            if (idx >= 0) return idx;
        }
        return -1;
    }

    /// <summary>
    /// Decodes UTF-8 (a BOM is dropped) and splits into lines; blank lines are ignored.
    /// Returns null when there is no header line.
    /// </summary>
    public static CsvTable? Parse(byte[] data)
    {
        var text = Encoding.UTF8.GetString(data);
        if (text.Length > 0 && text[0] == '﻿') text = text[1..];

        var lines = text.Split(["\r\n", "\n"], StringSplitOptions.None)
            .Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
        if (lines.Count == 0) return null;

        var delimiter = DetectDelimiter(lines[0]);
        var rows = new List<CsvRow>(lines.Count - 1);
        for (var i = 1; i < lines.Count; i++)
            rows.Add(new CsvRow(i, ParseLine(lines[i], delimiter)));
        return new CsvTable(delimiter, ParseLine(lines[0], delimiter), rows);
    }

    /// <summary>The separator used most often outside quotes in the header line.</summary>
    public static char DetectDelimiter(string headerLine)
    {
        int semicolons = 0, commas = 0, tabs = 0;
        var inQuotes = false;
        foreach (var c in headerLine)
        {
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes)
            {
                if (c == ';') semicolons++;
                else if (c == ',') commas++;
                else if (c == '\t') tabs++;
            }
        }
        if (semicolons > commas && semicolons >= tabs) return ';';
        if (tabs > commas && tabs > semicolons) return '\t';
        return ',';
    }

    /// <summary>Splits one line; fields may be quoted with <c>"</c> (<c>""</c> inside quotes is a quote).</summary>
    public static List<string> ParseLine(string line, char delimiter)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < line.Length; i++)
        {
            var c = line[i];
            if (inQuotes)
            {
                if (c != '"') current.Append(c);
                else if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else inQuotes = false;
            }
            else if (c == '"') inQuotes = true;
            else if (c == delimiter) { fields.Add(current.ToString()); current.Clear(); }
            else current.Append(c);
        }

        fields.Add(current.ToString());
        return fields;
    }
}

/// <summary>One data line of a <see cref="CsvTable"/>.</summary>
public sealed record CsvRow(int RowNumber, IReadOnlyList<string> Fields)
{
    /// <summary>The trimmed field at <paramref name="index"/>, or "" when the column is missing.</summary>
    public string this[int index] => index >= 0 && index < Fields.Count ? Fields[index].Trim() : "";
}
