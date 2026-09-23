
namespace TerseSharp.Server.Tools;

internal static class RetryNote
{
    public static readonly string[] Tools = ["replace_symbol", "replace_symbol_body", "add_member", "write_text"];

    public static string For(string tool, TerseErrorCode code, int payloads) => tool switch
    {
        "replace_symbol" => ReplaceSymbol(code, payloads),
        "add_member" => AddMember(code, payloads),
        "write_text" => code is TerseErrorCode.CompileRegression
            ? "the rejected content and its usings= are held, so the retry names the token plus force=true instead of re-sending the file - a content= you pass replaces the held one"
            : "the content is held, so the retry is the token plus a corrected path= and nothing else",
        "replace_symbol_body" => code is TerseErrorCode.CompileRegression
            ? "the rejected body and its usings= are held, so the retry names the token instead of re-sending them"
            : "the body is held, so the retry is the token plus a corrected symbolId= and nothing else",
        _ => "the rejected text is held, so the retry names the token instead of re-sending it",
    };

    private static string ReplaceSymbol(TerseErrorCode code, int payloads) => (code, payloads) switch
    {
        (TerseErrorCode.CompileRegression, > 1) => "the rejected declarations, their add= and their usings= are held, so the retry names the token, and fix=[\"<index>=<corrected declaration>\"] replaces only the entries that were wrong",
        (TerseErrorCode.CompileRegression, _) => "the rejected declaration, its add= and its usings= are held, so the retry names the token instead of re-sending them",
        (_, > 1) => "the declarations are held, so the retry is the token plus a corrected symbolIds= - one entry per held declaration - or fix=[\"<index>=<corrected declaration>\"] to replace only the entries that were wrong",
        _ => "the declaration is held, so the retry is the token plus a corrected symbolId= and nothing else",
    };

    private const string ChangeBeside = " - and when an existing member must change with them, such as the constructor that assigns a new field, replace_symbol takes this same token with append set and lands them beside that member in ONE edit";

    private static string AddMember(TerseErrorCode code, int payloads) => (code, payloads) switch
    {
        (TerseErrorCode.CompileRegression, > 1) => "the rejected declarations and their usings= are held, so the retry names the token plus a corrected declarations= - one entry per held declaration" + ChangeBeside,
        (TerseErrorCode.CompileRegression, _) => "the rejected declaration and its usings= are held, so the retry names the token instead of re-sending them" + ChangeBeside,
        (_, > 1) => "the declarations are held, so the retry is the token plus a corrected typeSymbolIds= - one entry per held declaration",
        _ => "the declaration is held, so the retry is the token plus a corrected typeSymbolId= and nothing else",
    };
}
