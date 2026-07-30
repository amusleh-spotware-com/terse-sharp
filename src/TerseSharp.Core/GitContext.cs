namespace TerseSharp.Core;

public sealed record GitContext(string Branch, string WorktreeName)
{
    public static GitContext Unknown { get; } = new("-", "-");

    public static GitContext Detect(string solutionPath)
    {
        var directory = new DirectoryInfo(Path.GetDirectoryName(Path.GetFullPath(solutionPath))!);

        while (directory is not null)
        {
            var gitPath = Path.Combine(directory.FullName, ".git");

            if (Directory.Exists(gitPath) || File.Exists(gitPath))
                return new GitContext(ReadBranch(gitPath), directory.Name);

            directory = directory.Parent;
        }

        return Unknown;
    }

    private static string ReadBranch(string gitPath)
    {
        var headFile = Directory.Exists(gitPath) ? Path.Combine(gitPath, "HEAD") : ResolveLinkedHead(gitPath);

        if (headFile is null || !File.Exists(headFile))
            return "-";

        var head = File.ReadAllText(headFile).Trim();

        return head.StartsWith("ref: refs/heads/", StringComparison.Ordinal) ? head[16..] : head;
    }

    private static string? ResolveLinkedHead(string gitFile)
    {
        var content = File.ReadAllText(gitFile).Trim();

        return content.StartsWith("gitdir: ", StringComparison.Ordinal)
            ? Path.Combine(content[8..].Trim(), "HEAD")
            : null;
    }
}
