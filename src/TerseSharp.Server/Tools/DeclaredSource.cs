namespace TerseSharp.Server.Tools;

internal static class DeclaredSource
{
    public static Task<string> FirstAsync(ToolContext context, string? workspace, SyntaxRead? read, Func<Task<string>> resolved, CancellationToken cancellationToken) => read is { } syntax
        ? ChosenAsync(
            record => context.WithWorkspaceAsync(
                workspace,
                syntax.Path,
                async loaded => record(await SyntaxSource.SourceAsync(loaded, syntax, cancellationToken).ConfigureAwait(false)),
                cancellationToken: cancellationToken),
            resolved)
        : resolved();

    internal static async Task<string> ChosenAsync(Func<Func<string?, string>, Task<string>> attempt, Func<Task<string>> resolved)
    {
        string? hit = null;
        var answer = await attempt(found => (hit = found) ?? string.Empty).ConfigureAwait(false);

        return hit is { Length: > 0 } ? answer : await resolved().ConfigureAwait(false);
    }
}
