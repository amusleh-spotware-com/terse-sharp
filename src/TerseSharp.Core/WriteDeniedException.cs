namespace TerseSharp.Core;

public sealed class WriteDeniedException(string filePath, bool readOnly, Exception? innerException)
    : UnauthorizedAccessException(string.Create(CultureInfo.InvariantCulture, $"Access to the path '{filePath}' is denied."), innerException)
{
    public string FilePath { get; } = filePath;

    public bool ReadOnly { get; } = readOnly;
}
