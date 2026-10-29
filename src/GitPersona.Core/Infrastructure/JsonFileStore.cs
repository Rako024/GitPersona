using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitPersona.Core.Infrastructure;

/// <summary>Shared JSON (de)serialization for GitPersona's own data files.</summary>
public static class JsonFileStore
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        // Keep non-ASCII names (ə, ş, ğ, ...) readable in the file; it is never embedded in HTML.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Reads <paramref name="path"/>. A file that cannot be parsed is moved aside
    /// (<c>name.corrupt-yyyyMMdd-HHmmss</c>) instead of being overwritten later.
    /// </summary>
    public static JsonReadResult<T> Read<T>(string path, TimeProvider? timeProvider = null)
        where T : class
    {
        if (!File.Exists(path))
        {
            return new JsonReadResult<T>(null, null);
        }

        try
        {
            using var stream = File.OpenRead(path);
            return new JsonReadResult<T>(JsonSerializer.Deserialize<T>(stream, Options), null);
        }
        catch (JsonException)
        {
            var stamp = (timeProvider ?? TimeProvider.System).GetLocalNow()
                .ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var quarantinePath = $"{path}.corrupt-{stamp}";
            File.Move(path, quarantinePath, overwrite: true);
            return new JsonReadResult<T>(null, quarantinePath);
        }
    }

    public static void Write<T>(string path, T value) =>
        AtomicFileWriter.Write(path, JsonSerializer.SerializeToUtf8Bytes(value, Options));
}

/// <param name="Value">The parsed value, or <see langword="null"/> when the file was missing or corrupt.</param>
/// <param name="QuarantinedPath">Where a corrupt file was moved to, if it was.</param>
public sealed record JsonReadResult<T>(T? Value, string? QuarantinedPath)
    where T : class;
