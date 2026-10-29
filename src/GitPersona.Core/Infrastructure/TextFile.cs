using System.Text;

namespace GitPersona.Core.Infrastructure;

/// <summary>Text of a file plus the encoding detail needed to write it back byte-for-byte.</summary>
public sealed record TextFileContent(string Text, bool HasUtf8Bom = false);

/// <summary>Strict UTF-8 reading/writing for config files we edit in place.</summary>
public static class TextFile
{
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    // Throwing on invalid bytes is deliberate: a file in some other encoding would be
    // silently mangled by a lossy round trip, so we refuse to edit it instead.
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>Reads the file, or returns <see langword="null"/> when it does not exist.</summary>
    /// <exception cref="DecoderFallbackException">The file is not valid UTF-8.</exception>
    public static TextFileContent? ReadIfExists(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var bytes = File.ReadAllBytes(path);
        var hasBom = bytes.AsSpan().StartsWith(Utf8Bom);
        var offset = hasBom ? Utf8Bom.Length : 0;
        return new TextFileContent(StrictUtf8.GetString(bytes, offset, bytes.Length - offset), hasBom);
    }

    public static byte[] Encode(TextFileContent content)
    {
        ArgumentNullException.ThrowIfNull(content);
        var body = StrictUtf8.GetBytes(content.Text);
        return content.HasUtf8Bom ? [.. Utf8Bom, .. body] : body;
    }
}
