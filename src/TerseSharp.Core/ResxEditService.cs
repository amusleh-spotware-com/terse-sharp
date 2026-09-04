using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace TerseSharp.Core;

public readonly record struct ResxPair(string Key, string Value);

public static class ResxEditService
{
    public static async Task<Result<string>> Set(
        LoadedWorkspace workspace,
        string path,
        string? key,
        string? value,
        string? entries,
        string? culture,
        string? comment,
        bool dryRun,
        bool verbose)
    {
        var located = ResxTarget.Locate(workspace, path);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var pairs = Pairs(key, value, entries);

        return pairs.IsOk
            ? Settled(workspace, await Upsert(workspace, located.Value!, pairs.Value!, culture, comment, dryRun, verbose).ConfigureAwait(false), dryRun)
            : Result.Fail<string>(pairs.Error!);
    }
    public static async Task<Result<string>> RemoveAsync(
        LoadedWorkspace workspace,
        string path,
        string key,
        string? culture,
        bool force,
        bool dryRun,
        bool verbose,
        CancellationToken cancellationToken)
    {
        var located = ResxTarget.Locate(workspace, path);

        if (!located.IsOk)
        {
            return Result.Fail<string>(located.Error!);
        }

        if (key is not { Length: > 0 })
        {
            return Result.Fail<string>(Errors.Blank("key"));
        }

        var blocking = force
            ? []
            : await ResxUsageService.AllAsync(workspace, key, cancellationToken).ConfigureAwait(false);

        return blocking.Count > 0
            ? Result.Fail<string>(StillUsed(key, blocking))
            : Settled(workspace, await Delete(located.Value!, key, culture, dryRun, verbose).ConfigureAwait(false), dryRun);
    }
    public static async Task<Result<string>> Rename(
        LoadedWorkspace workspace,
        string path,
        string key,
        string newKey,
        bool updateReferences,
        bool dryRun,
        bool verbose)
    {
        var located = ResxTarget.Locate(workspace, path);

        if (!located.IsOk)
            return Result.Fail<string>(located.Error!);

        var checkedNames = Names(located.Value!, key, newKey);

        return checkedNames is null
            ? Settled(workspace, await Renamed(workspace, located.Value!, key, newKey, updateReferences, dryRun, verbose).ConfigureAwait(false), dryRun)
            : Result.Fail<string>(checkedNames);
    }
    private static TerseError? Names(ResxTarget target, string key, string newKey)
    {
        if (key is not { Length: > 0 } || newKey is not { Length: > 0 })
        {
            return Errors.Blank("key and newKey");
        }

        if (string.Equals(key, newKey, StringComparison.Ordinal))
        {
            return Errors.Invalid("newKey is the same as key", "pass a different newKey");
        }

        if (!KeyShape.IsMatch(newKey))
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{newKey}' is not a resource key"),
                "use letters, digits, '_', '.' and '-' only, starting with a letter or '_'");
        }

        return target.Family.Files.Any(file => target.Index.Entries(file).Any(entry => string.Equals(entry.Name, newKey, StringComparison.Ordinal)))
            ? Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{newKey}' already exists in this family"),
                "pick a free key, or remove the existing one first")
            : null;
    }

    private static async Task<Result<string>> Renamed(
        LoadedWorkspace workspace,
        ResxTarget target,
        string key,
        string newKey,
        bool updateReferences,
        bool dryRun,
        bool verbose)
    {
        var writes = target.Family.Files.Select(file => RenameIn(target.Index, file, key, newKey)).OfType<Pending>().ToList();

        if (writes.Count is 0)
        {
            return Result.Fail<string>(Missing(key, target.Family));
        }

        var references = updateReferences
            ? Rewrite(workspace.Root, target.Family.Name, key, newKey)
            : NoReferences;

        return await Apply(target.Index, "resx_rename", [.. writes, .. references], Notes(target.Family, references.Count), dryRun, verbose).ConfigureAwait(false);
    }

    private static Pending? RenameIn(ResxIndex index, ResxFile file, string key, string newKey)
    {
        var document = index.Read(file.Path);

        if (document is not { IsOk: true, Value: { } loaded } || loaded.Find(key) is not { } entry)
        {
            return null;
        }

        var declaration = loaded.Text[entry.Start..entry.End];
        var attribute = string.Create(CultureInfo.InvariantCulture, $"name=\"{key}\"");
        var at = declaration.IndexOf(attribute, StringComparison.Ordinal);

        if (at < 0)
        {
            return null;
        }

        var replaced = declaration
            .Remove(at, attribute.Length)
            .Insert(at, string.Create(CultureInfo.InvariantCulture, $"name=\"{newKey}\""));

        return new Pending(
            file.Path,
            file.Relative,
            loaded.Text,
            loaded.Text.Remove(entry.Start, entry.Length).Insert(entry.Start, replaced));
    }

    private static IReadOnlyList<Pending> Rewrite(string root, string family, string key, string newKey)
    {
        var renaming = new Renaming(key, newKey, family, Member(family, key, newKey));

        return
        [
            .. WorkspaceFiles
                .Enumerate(root, ResxUsageService.IsScannable)
                .Select(file => Rewrite(root, file, renaming))
                .OfType<Pending>(),
        ];
    }

    private static Pending? Rewrite(string root, string file, Renaming renaming)
    {
        var before = Read(file);

        if (before is null)
        {
            return null;
        }

        var after = string.Join('\n', before.Split('\n').Select(renaming.Apply));

        return string.Equals(before, after, StringComparison.Ordinal)
            ? null
            : new Pending(file, PositionFormat.Relative(root, file), before, after);
    }

    private static async Task<Result<string>> Delete(ResxTarget target, string key, string? culture, bool dryRun, bool verbose)
    {
        var writes = Targets(target, culture)
            .Select(file => DeleteIn(target.Index, file, key))
            .OfType<Pending>()
            .ToArray();

        return writes.Length is 0
            ? Result.Fail<string>(Missing(key, target.Family))
            : await Apply(target.Index, "resx_remove", writes, Notes(target.Family, 0), dryRun, verbose).ConfigureAwait(false);
    }

    private static Pending? DeleteIn(ResxIndex index, ResxFile file, string key)
    {
        var document = index.Read(file.Path);

        if (document is not { IsOk: true, Value: { } loaded } || loaded.Find(key) is not { } entry)
            return null;

        var start = entry.LineStart(loaded.Text);
        var end = EndOfLine(loaded.Text, entry.End);

        return new Pending(file.Path, file.Relative, loaded.Text, loaded.Text.Remove(start, end - start));
    }

    private static int EndOfLine(string text, int offset)
    {
        var end = text.IndexOf('\n', Math.Min(offset, text.Length - 1));

        return end < 0 ? text.Length : end + 1;
    }

    private static IEnumerable<ResxFile> Targets(ResxTarget target, string? culture) => culture switch
    {
        null or "" => target.Family.Files,
        "neutral" => [target.Family.Neutral],
        _ => target.Family.Culture(culture) is { } file ? [file] : [],
    };

    private static async Task<Result<string>> Upsert(
        LoadedWorkspace workspace,
        ResxTarget target,
        IReadOnlyList<ResxPair> pairs,
        string? culture,
        string? comment,
        bool dryRun,
        bool verbose)
    {
        var destination = Destination(workspace, target, culture);

        if (!destination.IsOk)
        {
            return Result.Fail<string>(destination.Error!);
        }

        var path = destination.Value!;
        var seed = Seed(target.Index, target.Family, path);

        if (!seed.IsOk)
        {
            return Result.Fail<string>(seed.Error!);
        }

        return await Written(workspace, target, path, seed.Value!, pairs, comment, dryRun, verbose).ConfigureAwait(false);
    }

    private static async Task<Result<string>> Written(
        LoadedWorkspace workspace,
        ResxTarget target,
        string path,
        Seeded seed,
        IReadOnlyList<ResxPair> pairs,
        string? comment,
        bool dryRun,
        bool verbose)
    {
        var notes = new List<string>();
        var applied = Fold(path, seed.Text, pairs, comment, notes);

        if (!applied.IsOk)
        {
            return Result.Fail<string>(applied.Error!);
        }

        var pending = new Pending(
            path,
            PositionFormat.Relative(workspace.Root, path),
            seed.Existing ?? string.Empty,
            applied.Value!.Text);

        return await Apply(target.Index, "resx_set", [pending], Created(target, path, applied.Value!.Added, notes), dryRun, verbose).ConfigureAwait(false);
    }

    private static List<string> Created(ResxTarget target, string destination, bool added, List<string> notes)
    {
        if (added)
        {
            notes.Add(Designer(target.Family));
        }

        if (!File.Exists(destination))
        {
            notes.Add(ResxProject.Wiring(target.Family.Neutral.Path, destination));
        }

        return notes;
    }

    private static Result<Applied> Fold(
        string path,
        string seed,
        IReadOnlyList<ResxPair> pairs,
        string? comment,
        List<string> notes)
    {
        var text = seed;
        var added = false;

        foreach (var pair in pairs)
        {
            var applied = Single(path, text, pair, comment, notes);

            if (!applied.IsOk)
            {
                return Result.Fail<Applied>(applied.Error!);
            }

            text = applied.Value!.Text;
            added |= applied.Value!.Added;
        }

        return Result.Ok(new Applied(text, added));
    }

    private static Result<Applied> Single(string path, string text, ResxPair pair, string? comment, List<string> notes)
    {
        var parsed = ResxDocument.Parse(path, text);

        if (!parsed.IsOk)
        {
            return Result.Fail<Applied>(parsed.Error!);
        }

        var document = parsed.Value!;
        var matches = document.All(pair.Key);

        if (matches.Count > 1)
        {
            notes.Add(Duplicate(pair.Key, matches.Count));
        }

        if (matches.Count is 0)
        {
            return Result.Ok(new Applied(Insert(document, pair, comment), true));
        }

        var replaced = Replace(document, matches[0], pair, comment);

        return replaced.IsOk ? Result.Ok(new Applied(replaced.Value!, false)) : Result.Fail<Applied>(replaced.Error!);
    }

    private static string Insert(ResxDocument document, ResxPair pair, string? comment)
    {
        var indent = Indent(document, null);
        var at = document.InsertionPoint(pair.Key);
        var element = Element(pair, comment, indent, document.NewLine);

        return document.Text.Insert(at, indent + element + document.NewLine);
    }

    private static Result<string> Replace(ResxDocument document, ResxEntry entry, ResxPair pair, string? comment)
    {
        if (entry.Kind is not ResxEntryKind.Text)
        {
            return Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{entry.Name}' is a {entry.Kind.ToString().ToLowerInvariant()} resource, not a string"),
                "typed and binary entries are passed through untouched; edit them in a designer"));
        }

        var element = Element(pair, comment ?? entry.Comment, Indent(document, entry), document.NewLine);

        return Result.Ok(document.Text.Remove(entry.Start, entry.Length).Insert(entry.Start, element));
    }

    private static string Element(ResxPair pair, string? comment, string indent, string newLine)
    {
        var text = new StringBuilder(128);

        text.Append(CultureInfo.InvariantCulture, $"<data name=\"{Escaped(pair.Key)}\" xml:space=\"preserve\">{newLine}");
        text.Append(CultureInfo.InvariantCulture, $"{indent}  <value>{Escaped(pair.Value)}</value>{newLine}");

        if (comment is { Length: > 0 })
            text.Append(CultureInfo.InvariantCulture, $"{indent}  <comment>{Escaped(comment)}</comment>{newLine}");

        return text.Append(indent).Append("</data>").ToString();
    }

    private static string Escaped(string value) => System.Security.SecurityElement.Escape(value) ?? value;

    private static string Indent(ResxDocument document, ResxEntry? entry)
    {
        var anchor = entry ?? (document.Entries.Count is 0 ? null : document.Entries[0]);

        return anchor is null ? "  " : document.Text[anchor.LineStart(document.Text)..anchor.Start];
    }

    private static Result<string> Destination(LoadedWorkspace workspace, ResxTarget target, string? culture)
    {
        if (culture is null or "")
        {
            return Result.Ok(target.Path);
        }

        if (string.Equals(culture, "neutral", StringComparison.Ordinal))
        {
            return Result.Ok(target.Family.Neutral.Path);
        }

        return ResxCulture.IsCulture(culture)
            ? PathGuard.Resolve(workspace, target.Family.Culture(culture)?.Path ?? target.Family.CulturePath(culture))
            : Result.Fail<string>(Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"'{culture}' is not a culture name"),
                "pass a culture such as fr or pt-BR, or neutral"));
    }

    private static string? Template(ResxIndex index, ResxFamily family)
    {
        var document = index.Read(family.Neutral.Path);

        if (document is not { IsOk: true, Value: { } loaded })
            return null;

        var text = loaded.Text;

        foreach (var entry in loaded.Entries.OrderByDescending(entry => entry.Start))
            text = text.Remove(entry.LineStart(text), EndOfLine(text, entry.End) - entry.LineStart(text));

        return text;
    }

    private static Result<IReadOnlyList<ResxPair>> Pairs(string? key, string? value, string? entries)
    {
        if (key is { Length: > 0 } && entries is { Length: > 0 })
        {
            return Result.Fail<IReadOnlyList<ResxPair>>(Errors.Invalid(
                "key and entries are mutually exclusive",
                "pass key with value for one entry, or entries for several"));
        }

        if (key is { Length: > 0 })
        {
            return Result.Ok<IReadOnlyList<ResxPair>>([new ResxPair(key, value ?? string.Empty)]);
        }

        return entries is { Length: > 0 }
            ? Parsed(entries)
            : Result.Fail<IReadOnlyList<ResxPair>>(Errors.Invalid(
                "neither key nor entries was given",
                "pass key and value, or entries as Key=Value lines"));
    }

    private static Result<IReadOnlyList<ResxPair>> Parsed(string entries)
    {
        var pairs = new List<ResxPair>();
        var malformed = new List<int>();
        var number = 0;

        foreach (var line in entries.AsSpan().EnumerateLines())
        {
            number++;

            if (line.IsWhiteSpace())
                continue;

            if (Pair(new string(line)) is { } pair)
                pairs.Add(pair);
            else
                malformed.Add(number);
        }

        if (malformed.Count > 0)
        {
            return Result.Fail<IReadOnlyList<ResxPair>>(Errors.Invalid(
                "entries carried lines with no Key=Value separator: line " + string.Join(", line ", malformed),
                "write one Key=Value per line; nothing was written, so re-send the whole batch once those lines are fixed"));
        }

        return pairs.Count is 0
            ? Result.Fail<IReadOnlyList<ResxPair>>(Errors.Invalid(
                "entries contained no Key=Value line",
                "pass one Key=Value per line"))
            : Result.Ok<IReadOnlyList<ResxPair>>(pairs);
    }

    private static ResxPair? Pair(string line)
    {
        var separator = line.IndexOf('=', StringComparison.Ordinal);

        return separator <= 0 ? null : new ResxPair(line[..separator].Trim(), line[(separator + 1)..]);
    }

    private static async Task<Result<string>> Apply(
        ResxIndex index,
        string tool,
        IReadOnlyList<Pending> writes,
        IReadOnlyList<string> notes,
        bool dryRun,
        bool verbose)
    {
        var malformed = writes.Select(Validated).FirstOrDefault(error => error is not null);

        if (malformed is not null)
            return Result.Fail<string>(malformed);

        if (dryRun)
            return Result.Ok(Render(tool, writes, notes, dryRun, verbose));

        var written = await WriteAll(index, writes).ConfigureAwait(false);

        return written is null ? Result.Ok(Render(tool, writes, notes, dryRun, verbose)) : Result.Fail<string>(written);
    }

    private static TerseError? Validated(Pending write) => ResxIndex.IsResource(write.Path)
        ? ResxDocument.Parse(write.Relative, write.After).Error
        : null;

    private static async Task<TerseError?> WriteAll(ResxIndex index, IReadOnlyList<Pending> writes)
    {
        var done = new List<Pending>(writes.Count);

        try
        {
            foreach (var write in writes)
            {
                await AtomicWrite.TextAsync(write.Path, write.After).ConfigureAwait(false);
                index.Forget(write.Path);
                done.Add(write);
            }

            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Errors.EditConflict(Rolled(await Restore(index, done).ConfigureAwait(false), exception));
        }
    }

    private static async Task<List<string>> Restore(ResxIndex index, IEnumerable<Pending> done)
    {
        var stranded = new List<string>();

        foreach (var write in done)
        {
            try
            {
                await AtomicWrite.TextAsync(write.Path, write.Before).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                stranded.Add(write.Relative);
            }

            index.Forget(write.Path);
        }

        return stranded;
    }

    private static string Render(string tool, IReadOnlyList<Pending> writes, IReadOnlyList<string> notes, bool dryRun, bool verbose)
    {
        var response = new ResponseBuilder(tool, dryRun ? "dryRun" : "applied").Verbose(verbose);

        response.Summary(writes.Count, writes.Count, "files changed");

        if (dryRun && !verbose)
            response.Note("dryRun");

        if (!dryRun && !verbose)
        {
            foreach (var write in writes)
                response.Line(string.Create(CultureInfo.InvariantCulture, $"{write.Relative}  changedLines={UnifiedDiff.ChangedLines(write.Before, write.After)}"));
        }
        else
        {
            var reports = writes.Select(write => UnifiedDiff.Report(write.Relative, write.Before, write.After)).ToArray();

            foreach (var report in reports)
                response.Line(report.Text);

            response.Line(string.Create(
                CultureInfo.InvariantCulture,
                $"changedLines={reports.Sum(report => report.ChangedLines)}"));
        }

        foreach (var note in notes.Where(note => note.Length > 0))
            response.Note(note);

        return response.ToString();
    }
    private static IReadOnlyList<string> Notes(ResxFamily family, int references) => references is 0
        ? [Designer(family)]
        : [Designer(family), string.Create(CultureInfo.InvariantCulture, $"references={references}")];

    private static string Designer(ResxFamily family) => family.Designer is null
        ? string.Empty
        : string.Create(
            CultureInfo.InvariantCulture,
            $"designerStale=true - regenerate {family.Designer} (Visual Studio custom tool, or Generator=MSBuild:Compile) before referencing the key from C#");

    private static string Duplicate(string key, int count) => string.Create(
        CultureInfo.InvariantCulture,
        $"WARNING '{key}' is declared {count} times - updated the first declaration; run resx_validate for RESX004");

    private static TerseError Missing(string key, ResxFamily family) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{key}' is not declared in {family.Relative}"),
        "call resx_get or resx_find to see the keys this family declares");

    private static TerseError StillUsed(string key, IReadOnlyList<ResxUsage> usages) => Errors.Invalid(
        string.Create(
            CultureInfo.InvariantCulture,
            $"'{key}' is still referenced in {usages.Count} place(s): {string.Join(", ", usages.Take(5).Select(usage => usage.Relative + ":" + usage.Line.ToString(CultureInfo.InvariantCulture)))}"),
        "remove the references first, or pass force=true");

    [SuppressMessage("ApiDesign", "RS0030:Do not use banned APIs", Justification = "Synchronous read inside the resx rewrite path, which is a synchronous projection over the family set. Removing it means an async index, not a local change.")]
    private static string? Read(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private sealed record Pending(string Path, string Relative, string Before, string After);
    private static readonly IReadOnlyList<Pending> NoReferences = [];
    private static Result<Seeded> Seed(ResxIndex index, ResxFamily family, string path)
    {
        if (File.Exists(path))
        {
            var existing = Read(path);

            return existing is null
                ? Result.Fail<Seeded>(Unreadable(path))
                : Result.Ok(new Seeded(existing, existing));
        }

        var template = Template(index, family);

        return template is null
            ? Result.Fail<Seeded>(Unreadable(family.Neutral.Relative))
            : Result.Ok(new Seeded(null, template));
    }
    private static TerseError Unreadable(string path) => Errors.Invalid(
        string.Create(CultureInfo.InvariantCulture, $"'{path}' exists but could not be read, so it was not overwritten"),
        "close whatever holds the file open, or fix its permissions, and retry"); private sealed record Seeded(string? Existing, string Text); private sealed record Applied(string Text, bool Added);
    private static Regex? Member(string family, string key, string newKey) =>
            IsIdentifier(key) && IsIdentifier(newKey)
                ? new Regex(
                    @"\b" + Regex.Escape(family) + @"\s*\.\s*" + Regex.Escape(key) + @"\b",
                    RegexOptions.None,
                    TimeSpan.FromSeconds(2))
                : null; private static bool IsIdentifier(string name) => name.Length > 0
            && (char.IsLetter(name[0]) || name[0] is '_')
            && name.All(character => char.IsLetterOrDigit(character) || character is '_'); private sealed record Renaming(string Key, string NewKey, string Family, Regex? Member)
    {
        private static readonly string[] Markers =
            ["GetString", "ocalizer[", "x:Uid=", "Display(", "ResourceName", "ResourceType"];

        public string Apply(string line)
        {
            var rewritten = Member is null ? line : Member.Replace(line, match => match.Value.Replace(Key, NewKey, StringComparison.Ordinal));

            return IsLookup(line)
                ? rewritten.Replace(
                    string.Create(CultureInfo.InvariantCulture, $"\"{Key}\""),
                    string.Create(CultureInfo.InvariantCulture, $"\"{NewKey}\""),
                    StringComparison.Ordinal)
                : rewritten;
        }

        private bool IsLookup(string line) =>
            line.Contains(Family, StringComparison.Ordinal) || Markers.Any(marker => line.Contains(marker, StringComparison.Ordinal));
    }
    private static readonly Regex KeyShape = new(
        @"^[A-Za-z_][A-Za-z0-9_.\-]*$",
        RegexOptions.None,
        TimeSpan.FromSeconds(2)); private static string Rolled(List<string> stranded, Exception exception) => stranded.Count is 0
        ? string.Create(CultureInfo.InvariantCulture, $"the family was left unchanged: {exception.Message}")
        : string.Create(
            CultureInfo.InvariantCulture,
            $"PARTIAL - {stranded.Count} file(s) could not be restored and are still modified: {string.Join(", ", stranded)} ({exception.Message})");

    private static Result<string> Settled(LoadedWorkspace workspace, Result<string> written, bool dryRun)
    {
        if (!written.IsOk || dryRun)
            return written;

        workspace.Sync.Bumped(ChangeKind.Resx);
        workspace.Sync.Bumped(ChangeKind.Files);

        return written;
    }

    private const int MaxBatchedResxFiles = 10;

    public static async Task<Result<string>> SetManyAsync(
        LoadedWorkspace workspace,
        string path,
        IReadOnlyList<ResxWrite> files,
        string? culture,
        string? comment,
        bool dryRun,
        bool verbose)
    {
        if (Bounded(files) is { } refusal)
            return Result.Fail<string>(refusal);

        var applied = new List<string>(files.Count);
        var refused = new List<string>();

        for (var index = 0; index < files.Count; index++)
        {
            var target = files[index].Path is { Length: > 0 } named ? named : path;

            Sorted(await Set(workspace, target, null, null, files[index].Entries, culture, comment, dryRun, verbose).ConfigureAwait(false), index, target, applied, refused);
        }

        return Result.Ok(applied.Count is 0 ? string.Join('\n', refused) : string.Join('\n', applied.Concat(refused)));
    }

    private static TerseError? Bounded(IReadOnlyList<ResxWrite> files)
    {
        if (files.Count > MaxBatchedResxFiles)
        {
            return Errors.Invalid(
                string.Create(CultureInfo.InvariantCulture, $"files carried {files.Count} entries, at most {MaxBatchedResxFiles} are written in one call"),
                string.Create(CultureInfo.InvariantCulture, $"split it into calls of at most {MaxBatchedResxFiles} files"));
        }

        for (var index = 0; index < files.Count; index++)
        {
            if (files[index].Entries is not { Length: > 0 })
            {
                return Errors.Invalid(
                    string.Create(CultureInfo.InvariantCulture, $"files[{index}] carries no entries"),
                    "give every entry its own Key=Value lines, one per line");
            }
        }

        return null;
    }

    private static void Sorted(Result<string> answer, int index, string path, List<string> applied, List<string> refused)
    {
        if (answer.IsOk)
        {
            applied.Add(answer.Value!);

            return;
        }

        var error = answer.Error!;

        refused.Add(string.Create(CultureInfo.InvariantCulture, $"REFUSED files[{index}] {path}: {error.Code} - {error.Message}; remedy: {error.Remedy}"));
    }
}

public readonly record struct ResxWrite(string? Path = null, string? Entries = null);
