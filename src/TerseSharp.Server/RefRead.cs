using Microsoft.CodeAnalysis;
using TerseSharp.Server.Tools;

namespace TerseSharp.Server;

internal static class RefRead
{
    public static async Task<string> TextAsync(
            LoadedWorkspace workspace,
            string path,
            string reference,
            FileService.ReadRequest request,
            bool whole,
            CancellationToken cancellationToken)
    {
        var relative = Relative(workspace, path);
        var shown = await GitRunner.ShowAsync(workspace.Root, reference, relative, cancellationToken).ConfigureAwait(false);

        if (!shown.IsOk)
            return Failed(shown.Error!, workspace.Root, relative, reference);

        var label = relative + "@" + reference;
        var text = shown.Value!;

        var answer = whole && SourceFile.IsCSharp(relative)
            ? Historic(OutlineService.FromText(label, text, signatures: true, "short", usings: false), reference)
            : FileService.Rendered(relative, label, text, request);

        var size = request.Bytes
            ? await SizeAsync(workspace.Root, reference, relative, cancellationToken).ConfigureAwait(false)
            : null;

        return NavigationTools.Unwrap(Stamped(answer, request, text.Length, size));
    }

    public static async Task<string> OutlineAsync(
        LoadedWorkspace workspace,
        string path,
        string reference,
        OutlineOptions options,
        CancellationToken cancellationToken)
    {
        var relative = Relative(workspace, path);
        var shown = await GitRunner.ShowAsync(workspace.Root, reference, relative, cancellationToken).ConfigureAwait(false);

        if (!shown.IsOk)
            return Failed(shown.Error!, workspace.Root, relative, reference);

        var outline = NavigationTools.Unwrap(OutlineService.FromText(
            relative + "@" + reference,
            shown.Value!,
            options.Signatures,
            options.Ids,
            options.Usings,
            options.ParameterNames,
            options.Contains,
            options.All));

        return outline + "\n" + Historical(reference);
    }

    private static string Historical(string reference) => string.Create(
        CultureInfo.InvariantCulture,
        $"at {reference} - the ids above address that revision's text, not the loaded solution; read a body with read_text ref={reference}");

    internal static bool IsAbsent(TerseError error) =>
            error.Code == TerseErrorCode.InvalidArgument
            && error.Message.Contains("exists on disk, but not in", StringComparison.Ordinal);

    private static string Failed(TerseError error, string root, string relative, string reference) => IsAbsent(error)
        ? string.Create(CultureInfo.InvariantCulture, $"ABSENT  {relative} at {reference} - the working tree holds it and that revision does not, so it was added after {reference}")
        : Unread(error, root, relative, reference);

    public static TerseError Batched(string parameter) => Errors.Invalid(
        "ref= reads one file at a git ref and cannot be combined with " + parameter,
        "pass a single path= with ref=, or drop ref= to read the working tree");

    private static string Relative(LoadedWorkspace workspace, string path) => PositionFormat.Relative(
        workspace.Root,
        Path.IsPathRooted(path) ? path : Path.Combine(workspace.Root, path));

    private static Result<string> Historic(Result<string> outline, string reference) =>
            outline.IsOk ? Result.Ok(outline.Value! + "\n" + Historical(reference)) : outline;

    private static Result<string> Stamped(Result<string> answer, FileService.ReadRequest request, int characters, long? bytes)
    {
        if (!answer.IsOk || (!request.Tokens && !request.Bytes))
            return answer;

        var stamps = FileService.Stamps(request with { Bytes = bytes is not null, Length = bytes ?? 0, Characters = characters });

        return Result.Ok(answer.Value! + stamps + (request.Bytes && bytes is null ? "\nbytes=UNRESOLVED  git could not size the blob at this ref" : string.Empty));
    }

    private static async Task<long?> SizeAsync(string root, string reference, string relative, CancellationToken cancellationToken)
    {
        var read = await GitRunner.ReadAsync(
            root,
            ["cat-file", "-s", "--end-of-options", reference + ":./" + relative.Replace('\\', '/')],
            cancellationToken).ConfigureAwait(false);

        return read.IsOk && long.TryParse(read.Value!.AsSpan().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
            ? size
            : null;
    }

    public static async Task<string> SourceAsync(
        LoadedWorkspace workspace,
        string path,
        string reference,
        string symbolId,
        SourceFormat format,
        CancellationToken cancellationToken)
    {
        var relative = Relative(workspace, path);
        var shown = await GitRunner.ShowAsync(workspace.Root, reference, relative, cancellationToken).ConfigureAwait(false);

        if (!shown.IsOk)
            return Failed(shown.Error!, workspace.Root, relative, reference);

        var source = await SourceService.FromTextAsync(
            workspace.Root,
            relative + "@" + reference,
            shown.Value!,
            symbolId,
            References(workspace, path),
            format,
            cancellationToken).ConfigureAwait(false);

        return source.IsOk ? source.Value! + "\n" + Historical(reference) : source.Error!.Render();
    }

    public static TerseError Unaddressed() => Errors.Invalid(
        "ref= needs path=, because a revision has no workspace index to resolve a name against",
        "pass path=<the file the symbol lives in> beside ref=, or drop ref= to read the working tree");

    private static IEnumerable<MetadataReference> References(LoadedWorkspace workspace, string path) =>
        DocumentLookup.Find(workspace, path)?.Project.MetadataReferences
        ?? workspace.Solution.Projects.FirstOrDefault()?.MetadataReferences
        ?? [];

    private const string RefRemedy = "check that ref= names a commit, branch or tag this repository holds";

    internal static bool IsMissing(TerseError error) =>
        error.Code == TerseErrorCode.InvalidArgument
        && error.Message.Contains("does not exist in", StringComparison.Ordinal);

    private static string Unread(TerseError error, string root, string relative, string reference) => error switch
    {
        _ when IsMissing(error) => Missing(root, relative, reference),
        { Code: TerseErrorCode.InvalidArgument } => new TerseError(error.Code, error.Message, RefRemedy).Render(),
        _ => error.Render(),
    };

    private static string Missing(string root, string relative, string reference) => NearestPaths.Around(root, relative) switch
    {
        [] => string.Create(CultureInfo.InvariantCulture, $"ABSENT  {relative} at {reference} and not in the working tree - no file beside it shares its extension; find_files glob=\"**/{Path.GetFileName(relative)}\" searches the whole tree"),
        var nearest => string.Create(CultureInfo.InvariantCulture, $"ABSENT  {relative} at {reference} and not in the working tree - nearest: {string.Join("  ", nearest)}"),
    };
}

internal readonly record struct OutlineOptions(bool Signatures, string Ids, bool Usings, bool ParameterNames, string? Contains, bool All = false);
