using System.Diagnostics;

namespace TerseSharp.UnitTests;

public sealed class TemporarySolution : IDisposable
{
    private TemporarySolution(string root)
    {
        Root = root;
        SolutionPath = Path.Combine(root, "FixtureSolution.slnx");
    }

    public string Root { get; }

    public string SolutionPath { get; }

    public string ProjectDirectory => Path.Combine(Root, "src", "Fixture.Trading");

    public string ProjectPath => Path.Combine(ProjectDirectory, "Fixture.Trading.csproj");

    public string OrderServicePath => Path.Combine(ProjectDirectory, "OrderService.cs");

    public static TemporarySolution Create()
    {
        var root = Path.Combine(Path.GetTempPath(), "terse-fixture-" + Guid.NewGuid().ToString("N"));

        Copy(Path.Combine(Fixtures.RepositoryRoot, "fixtures", "FixtureSolution"), root);

        File.WriteAllText(Path.Combine(root, ".git"), "gitdir: none");

        return new TemporarySolution(root);
    }

    public static async Task<TemporarySolution> CreateRepositoryAsync(CancellationToken cancellationToken)
    {
        var solution = Create();

        try
        {
            await solution.CommitEverythingAsync(cancellationToken);

            return solution;
        }
        catch
        {
            solution.Dispose();
            throw;
        }
    }

    private async Task CommitEverythingAsync(CancellationToken cancellationToken)
    {
        File.Delete(Path.Combine(Root, ".git"));
        File.WriteAllText(Path.Combine(Root, ".gitignore"), "bin/\nobj/\n.vs/\n");

        await GitAsync(["init", "--quiet", "--initial-branch=main"], cancellationToken);
        await GitAsync(["config", "core.autocrlf", "false"], cancellationToken);
        await GitAsync(["add", "--all"], cancellationToken);
        await GitAsync(["-c", "user.name=terse", "-c", "user.email=terse@example.invalid", "-c", "commit.gpgsign=false", "commit", "--quiet", "-m", "fixture"], cancellationToken);
    }

    private async Task GitAsync(string[] arguments, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = Root, RedirectStandardOutput = true, RedirectStandardError = true };

        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);

        using var process = Process.Start(start) ?? throw new InvalidOperationException("git did not start");
        var error = process.StandardError.ReadToEndAsync(cancellationToken);

        await process.StandardOutput.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode is not 0)
            throw new InvalidOperationException("git " + string.Join(' ', arguments) + " failed: " + await error);
    }

    public void Dispose() => Delete(Root);

    private static void Copy(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (var file in Directory.EnumerateFiles(source).Where(Durable))
            CopyFile(file, Path.Combine(destination, Path.GetFileName(file)));

        foreach (var directory in Directory.EnumerateDirectories(source).Where(Copyable))
            Copy(directory, Path.Combine(destination, Path.GetFileName(directory)));
    }

    private static bool Durable(string file) => !TerseSharp.Core.WorkspaceFiles.IsTemporary(file);

    private static void CopyFile(string file, string destination)
    {
        try
        {
            File.Copy(file, destination);
        }
        catch (FileNotFoundException)
        {
        }
        catch (IOException) when (!File.Exists(file))
        {
        }
    }

    private static bool Copyable(string directory) => Path.GetFileName(directory) is not ("bin" or "obj");

    private static void Delete(string root)
    {
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
