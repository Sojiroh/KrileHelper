namespace Sharlayan.Core;

public class SharlayanException : Exception
{
    public SharlayanException(string message) : base(message) { }
    public SharlayanException(string message, Exception inner) : base(message, inner) { }
}

public sealed class FFXIVNotRunningException : SharlayanException
{
    public FFXIVNotRunningException() : base("ffxiv_dx11.exe was not found in any running process.") { }
}

public sealed class SignatureScanFailedException : SharlayanException
{
    public string SignatureKey { get; }
    public SignatureScanFailedException(string key)
        : base($"Required signature '{key}' was not found in the FFXIV binary. Resource files may be outdated for this game version.")
    { SignatureKey = key; }
}

public sealed class ProcessDetachedException : SharlayanException
{
    public ProcessDetachedException() : base("The attached FFXIV process is no longer running.") { }
}
