namespace TerseSharp.Server;

public static class AssetBanner
{
    private static string? pending;

    public static void Publish(AssetState state) => Interlocked.Exchange(ref pending, Notice(state));

    public static string Appended(string response)
    {
        var notice = Volatile.Read(ref pending);

        return notice is { Length: > 0 } ? response + "\n" + notice : response;
    }

    public static string? Notice(AssetState state)
    {
        var missing = new List<string>(2);

        if (!state.GuardInstalled)
            missing.Add("WARNING guard=absent - nothing stops an agent answering with Read, Grep, cat or dotnet build; run: terse install --guard");

        if (!state.SkillInstalled)
            missing.Add("WARNING skill=absent - the agent has no tool guide for this server; run: terse install --skill");

        return missing.Count is 0 ? null : string.Join("\n", missing);
    }
}
