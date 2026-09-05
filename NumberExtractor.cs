using System.IO;
using System.Text.RegularExpressions;

namespace JavSubtitleScraper;

public static class NumberExtractor
{
    private static readonly Regex Standard = new(
        @"(?<![A-Za-z0-9])(?<id>[A-Za-z]{2,10}[-_ ]?[A-Za-z0-9]{1,8}[-_ ]?\d{2,8})(?!\d)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Fc2 = new(
        @"FC2[-_ ]*(?:PPV[-_ ]*)?(?<id>\d{4,10})",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public static string? FromPath(string path)
    {
        var fileName = Path.GetFileNameWithoutExtension(path);
        var id = FromText(fileName);
        if (!string.IsNullOrWhiteSpace(id))
            return id;

        return FromText(Path.GetFileName(Path.GetDirectoryName(path) ?? string.Empty));
    }

    public static string? FromText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var fc2 = Fc2.Match(text);
        if (fc2.Success)
            return $"fc2-ppv-{fc2.Groups["id"].Value}";

        var match = Standard.Match(text);
        if (!match.Success)
            return null;

        return Regex.Replace(match.Groups["id"].Value, @"[_ ]+", "-").ToUpperInvariant();
    }
}
