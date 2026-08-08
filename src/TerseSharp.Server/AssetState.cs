namespace TerseSharp.Server;

public sealed record AssetState(bool SkillInstalled, bool SkillCurrent, bool GuardInstalled, bool GuardCurrent)
{
    public bool NeedsInstall =>
    !SkillInstalled || !SkillCurrent || !GuardInstalled || !GuardCurrent;
}
