namespace TerseSharp.Server;

public sealed record AssetState(bool SkillInstalled, bool SkillCurrent, bool GuardInstalled, bool GuardCurrent)
{
    public bool Stale => (SkillInstalled && !SkillCurrent) || (GuardInstalled && !GuardCurrent);
}
