namespace TerseSharp.Core;

public sealed record UpdateRequest(ReleaseVersion Running, string StatePath, string Endpoint, TimeSpan Window);
