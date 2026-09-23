namespace TerseSharp.UnitTests;

internal static class GeneratorSolutionLock
{
    private static readonly string LockPath = Path.Combine(Path.GetTempPath(), "terse-generator-solution.lock");

    public static async Task<FileStream> AcquireAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            try
            {
                return new FileStream(LockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.Asynchronous);
            }
            catch (IOException)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(250), cancellationToken);
            }
        }
    }
}
