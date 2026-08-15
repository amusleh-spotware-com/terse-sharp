namespace TerseSharp.Core;

public static class MarkdownTable
{
    public static Result<string> Projected(string label, string text, IReadOnlyList<string> columns)
    {
        var scan = new Scan(columns);

        foreach (var line in text.AsSpan().EnumerateLines())
            scan.Read(line.Trim());

        if (scan.Rows.Count is 0)
            return Result.Fail<string>(Missing(label, columns, scan.Headers));

        var response = new ResponseBuilder("read_text", label + " columns");

        response.Summary(scan.Rows.Count, scan.Rows.Count, "rows");

        foreach (var row in scan.Rows)
            response.Line(row);

        return Result.Ok(response.ToString());
    }

    private static TerseError Missing(string label, IReadOnlyList<string> columns, List<string> headers) => Errors.Invalid(
        headers.Count is 0
            ? label + " holds no markdown table, so columns= has nothing to project"
            : "columns=" + string.Join(",", columns) + " names no column of " + label,
        headers.Count is 0
            ? "drop columns=, or pass headings=true for the section map"
            : "its columns are: " + string.Join(", ", headers));

    private sealed class Scan(IReadOnlyList<string> columns)
    {
        public List<string> Rows { get; } = [];

        public List<string> Headers { get; } = [];

        private int[] wanted = [];
        private bool header;

        public void Read(ReadOnlySpan<char> row)
        {
            if (!IsRow(row))
            {
                wanted = [];
                header = false;

                return;
            }

            var cells = Cells(row);

            if (!header)
            {
                header = true;
                wanted = Wanted(cells, columns);
                Headers.AddRange(cells);

                return;
            }

            if (wanted.Length > 0 && !IsDelimiter(cells))
                Rows.Add(Joined(cells, wanted));
        }
    }

    private static bool IsRow(ReadOnlySpan<char> row) => row.Length > 1 && row[0] is '|';

    private static bool IsDelimiter(string[] cells) =>
        Array.TrueForAll(cells, cell => cell.Length > 0 && cell.AsSpan().IndexOfAnyExcept('-', ':') < 0);

    private static int[] Wanted(string[] header, IReadOnlyList<string> columns)
    {
        var indexes = new List<int>(columns.Count);

        foreach (var column in columns)
        {
            var found = Array.FindIndex(header, cell => string.Equals(cell, column, StringComparison.OrdinalIgnoreCase));

            if (found >= 0)
                indexes.Add(found);
        }

        return [.. indexes];
    }

    private static string Joined(string[] cells, int[] wanted)
    {
        var kept = new List<string>(wanted.Length);

        foreach (var index in wanted)
            kept.Add(index < cells.Length ? cells[index] : string.Empty);

        return string.Join(" | ", kept);
    }

    private static string[] Cells(ReadOnlySpan<char> row)
    {
        var inner = row[1..(row.Length - (row[^1] is '|' ? 1 : 0))];
        var cells = new List<string>();
        var start = 0;

        for (var index = 0; index < inner.Length; index++)
        {
            if (inner[index] is '|' && (index is 0 || inner[index - 1] is not '\\'))
            {
                cells.Add(inner[start..index].Trim().ToString());
                start = index + 1;
            }
        }

        cells.Add(inner[start..].Trim().ToString());

        return [.. cells];
    }
}
