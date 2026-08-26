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
            return shown.Error!.Render();

        var label = relative + "@" + reference;

        return whole && SourceFile.IsCSharp(relative)
            ? NavigationTools.Unwrap(OutlineService.FromText(label, shown.Value!, signatures: true, "short", usings: false)) + "\n" + Historical(reference)
            : NavigationTools.Unwrap(FileService.Rendered(relative, label, shown.Value!, request));
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
            return shown.Error!.Render();

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

    public static TerseError Batched(string parameter) => Errors.Invalid(
        "ref= reads one file at a git ref and cannot be combined with " + parameter,
        "pass a single path= with ref=, or drop ref= to read the working tree");

    private static string Relative(LoadedWorkspace workspace, string path) => PositionFormat.Relative(
        workspace.Root,
        Path.IsPathRooted(path) ? path : Path.Combine(workspace.Root, path));
}

internal readonly record struct OutlineOptions(bool Signatures, string Ids, bool Usings, bool ParameterNames, string? Contains, bool All = false);
