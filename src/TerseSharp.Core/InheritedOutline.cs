using System.Text;
using Microsoft.CodeAnalysis;

namespace TerseSharp.Core;

public static class InheritedOutline
{
    public static async Task<Result<string>> WithBasesAsync(LoadedWorkspace workspace, ISymbol symbol, OutlineService.TypeOutlineFormat format, CancellationToken cancellationToken)
    {
        var own = await OwnAsync(workspace, symbol, format, cancellationToken).ConfigureAwait(false);

        if (!own.IsOk || !format.Inherited || symbol is not INamedTypeSymbol { BaseType: { } first })
            return own;

        var outline = new StringBuilder(own.Value!.TrimEnd('\n'));
        var current = first;

        while (current is { SpecialType: not (SpecialType.System_Object or SpecialType.System_ValueType or SpecialType.System_Enum) } type)
        {
            outline.Append(await BaseAsync(workspace, type, format, cancellationToken).ConfigureAwait(false));
            current = type.BaseType;
        }

        return Result.Ok(outline.ToString());
    }

    private static Task<Result<string>> OwnAsync(LoadedWorkspace workspace, ISymbol symbol, OutlineService.TypeOutlineFormat format, CancellationToken cancellationToken) =>
        OutlineService.TypeAsync(workspace, symbol, format.Signatures, format.Ids, cancellationToken, format.ParameterNames, format.Contains, format.All);

    private static async Task<string> BaseAsync(LoadedWorkspace workspace, INamedTypeSymbol type, OutlineService.TypeOutlineFormat format, CancellationToken cancellationToken)
    {
        var outline = await OwnAsync(workspace, type.OriginalDefinition, format, cancellationToken).ConfigureAwait(false);

        return string.Create(CultureInfo.InvariantCulture, $"\ninherited from {SymbolId.From(type.OriginalDefinition).Value}\n{(outline.IsOk ? outline.Value!.TrimEnd('\n') : outline.Error!.Render())}");
    }
}
