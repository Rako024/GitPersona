namespace GitPersona.Core.Tests;

/// <summary>A unique directory under %TEMP% that is deleted when disposed.</summary>
internal sealed class TempDirectory : IDisposable
{
    public TempDirectory()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "GitPersona.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public void Dispose()
    {
        try
        {
            // git marks object and pack files read-only.
            foreach (var file in Directory.EnumerateFiles(Path, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(file, FileAttributes.Normal);
            }

            Directory.Delete(Path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort; a leftover temp folder must not fail the test run.
        }
    }
}

internal static partial class ShortPath
{
    /// <summary>The 8.3 form of an existing path, or <see langword="null"/> when the volume has no short names.</summary>
    public static string? Of(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathNameW(path, buffer, (uint)buffer.Length);
        var shortPath = length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : null;
        return shortPath is not null && !shortPath.Equals(path, StringComparison.OrdinalIgnoreCase) ? shortPath : null;
    }

    [System.Runtime.InteropServices.LibraryImport("kernel32.dll", StringMarshalling = System.Runtime.InteropServices.StringMarshalling.Utf16)]
    private static partial uint GetShortPathNameW(string longPath, [System.Runtime.InteropServices.Out] char[] shortPath, uint bufferLength);
}

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
}
