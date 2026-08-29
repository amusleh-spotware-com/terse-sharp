namespace TerseSharp.Core;

public static class MarkdownTable
{
    public static Result<string> Projected(
        string label,
        string text,
        IReadOnlyList<string> columns,
        int startLine = 0,
        int endLine = 0,
        int maxRows = 0,
        string? section = null,
        int maxChars = 0,
        int cellChars = 0)
    {
        var scan = new Scan(columns, cellChars);
        var number = 0;

        foreach (var line in text.AsSpan().EnumerateLines())
        {
            number++;

            if (number < startLine)
                continue;

            if (endLine > 0 && number > endLine)
                break;

            scan.Read(line.Trim());
        }

        var missed = scan.Missed();

        return scan.Headers.Count is 0 || missed.Count > 0
            ? Result.Fail<string>(Missing(label, missed, scan.Headers, section))
            : Result.Ok(Rendered(label, scan, maxRows, maxChars, cellChars));
    }

    private static string Rendered(string label, Scan scan, int maxRows, int maxChars, int cellChars)
    {
        var rows = scan.Rows;
        var limit = maxRows > 0 ? Math.Min(rows.Count, maxRows) : rows.Count;
        var shown = Fitted(rows, limit, maxChars);
        var response = new ResponseBuilder("read_text", label + " columns");

        response.Summary(shown, rows.Count, "rows", "maxLines=, columns= or section=");

        for (var index = 0; index < shown; index++)
            response.Line(rows[index]);

        if (scan.Clipped > 0)
        {
            response.Note(string.Create(
                CultureInfo.InvariantCulture,
                $"cellChars={cellChars} clipped {scan.Clipped} of {scan.Counted} projected cell(s) - raise it, or drop it for the whole cell"));
        }

        if (shown < limit)
        {
            response.Note(
                "next: search_regex matchesOnly=true unique=true - the projection ran out of its maxChars budget before its maxLines one, so a regex over the one column you actually want answers this in ONE bounded call; raise maxChars only if you really need every row");
        }

        return response.ToString();
    }

    private static TerseError Missing(string label, IReadOnlyList<string> missed, List<string> headers, string? section)
    {
        var scanned = section is { Length: > 0 } heading ? "section '" + heading + "' of " + label : label;

        return Errors.Invalid(
            headers.Count is 0
                ? scanned + " holds no markdown table, so columns= has nothing to project"
                : "columns=" + string.Join(",", missed) + " names no column of " + scanned,
            Scoped(section, headers.Count is 0
                ? "drop columns=, or pass headings=true for the section map"
                : "its columns are: " + string.Join(", ", headers)));
    }

    private sealed class Scan(IReadOnlyList<string> columns, int cellChars)
    {
        private const string Ellipsis = "...";

        private readonly HashSet<string> matched = new(StringComparer.OrdinalIgnoreCase);

        public List<string> Rows { get; } = [];

        public List<string> Headers { get; } = [];

        public int Counted { get; private set; }

        public int Clipped { get; private set; }

        private int[] wanted = [];
        private bool header;

        public List<string> Missed()
        {
            var missed = new List<string>(columns.Count);

            foreach (var column in columns)
            {
                if (!matched.Contains(column))
                    missed.Add(column);
            }

            return missed;
        }

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
                wanted = Wanted(cells);
                Remember(cells);

                return;
            }

            if (wanted.Length > 0 && !IsDelimiter(cells))
                Rows.Add(Row(cells));
        }

        private string Row(string[] cells)
        {
            var kept = new List<string>(wanted.Length);

            foreach (var index in wanted)
                kept.Add(Clamped(index < cells.Length ? cells[index] : string.Empty));

            return string.Join(" | ", kept);
        }

        private string Clamped(string cell)
        {
            Counted++;

            if (cellChars <= 0 || cell.Length <= cellChars)
                return cell;

            Clipped++;

            if (cellChars <= Ellipsis.Length)
                return new string(cell.AsSpan(0, cellChars));

            var kept = cellChars - Ellipsis.Length;

            if (char.IsHighSurrogate(cell[kept - 1]))
                kept--;

            return string.Concat(cell.AsSpan(0, kept), Ellipsis);
        }

        private int[] Wanted(string[] cells)
        {
            var indexes = new List<int>(columns.Count);

            foreach (var column in columns)
            {
                var found = Array.FindIndex(cells, cell => string.Equals(cell, column, StringComparison.OrdinalIgnoreCase));

                if (found >= 0)
                {
                    indexes.Add(found);
                    matched.Add(column);
                }
            }

            return [.. indexes];
        }

        private void Remember(string[] cells)
        {
            foreach (var cell in cells)
            {
                if (!Headers.Exists(existing => string.Equals(existing, cell, StringComparison.OrdinalIgnoreCase)))
                    Headers.Add(cell);
            }
        }
    }

    private static bool IsRow(ReadOnlySpan<char> row) => row.Length > 1 && row[0] is '|';

    private static bool IsDelimiter(string[] cells) =>
        Array.TrueForAll(cells, cell => cell.Length > 0 && cell.AsSpan().IndexOfAnyExcept('-', ':') < 0);

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

    private static string Scoped(string? section, string remedy) =>
        section is { Length: > 0 }
            ? remedy + "; drop section= to project every table of the file, which may declare it elsewhere"
            : remedy;

    private static int Fitted(List<string> rows, int limit, int maxChars)
    {
        if (maxChars <= 0)
            return limit;

        var used = 0;

        for (var index = 0; index < limit; index++)
        {
            used += rows[index].Length + 1;

            if (used > maxChars)
                return index;
        }

        return limit;
    }
}
