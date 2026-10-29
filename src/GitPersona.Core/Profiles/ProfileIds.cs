using System.Text;

namespace GitPersona.Core.Profiles;

public static class ProfileIds
{
    private const int MaxLength = 32;

    /// <summary>Turns a display name such as "İş — Şirkət" into a valid profile id ("is-sirket").</summary>
    public static string Slugify(string displayName)
    {
        ArgumentNullException.ThrowIfNull(displayName);
        var builder = new StringBuilder();
        foreach (var c in displayName.Trim().ToLowerInvariant())
        {
            var mapped = c switch
            {
                'ə' => 'e',
                'ı' or 'i' or 'İ' => 'i',
                'ö' => 'o',
                'ü' => 'u',
                'ş' => 's',
                'ç' => 'c',
                'ğ' => 'g',
                _ => c,
            };

            if (mapped is >= 'a' and <= 'z' or >= '0' and <= '9')
            {
                builder.Append(mapped);
            }
            else if (builder.Length > 0 && builder[^1] != '-')
            {
                builder.Append('-');
            }
        }

        var slug = builder.ToString().Trim('-');
        return slug.Length <= MaxLength ? slug : slug[..MaxLength].TrimEnd('-');
    }
}
