using System.Runtime.InteropServices;

namespace GitPersona.Core.Infrastructure;

/// <summary>
/// Expands 8.3 short names (<c>C:\Users\MONAOC~1</c>) to the long form Git Bash reports for the same folder.
/// </summary>
public static partial class LongPath
{
    /// <summary>
    /// The long form of <paramref name="path"/>. Parts that do not exist yet are kept as written,
    /// after expanding the deepest folder that does exist.
    /// </summary>
    public static string Expand(string path)
    {
        var full = Path.GetFullPath(path);
        var existing = full;
        var missing = new Stack<string>();
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null)
            {
                return full;
            }

            missing.Push(Path.GetFileName(existing));
            existing = parent;
        }

        var expanded = GetLong(existing) ?? existing;
        return missing.Aggregate(expanded, Path.Combine);
    }

    private static string? GetLong(string path)
    {
        var buffer = new char[1024];
        var length = GetLongPathNameW(path, buffer, (uint)buffer.Length);
        return length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : null;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetLongPathNameW(string shortPath, [Out] char[] longPath, uint bufferLength);
}
