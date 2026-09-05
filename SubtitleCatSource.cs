using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JavSubtitleScraper;

public sealed class SubtitleCatSource : ISubtitleSource
{
    private const string Site = "https://subtitlecat.com";
    private static readonly HttpClient Client = new();
    private static readonly Regex LinkRegex = new("<a[^>]+href=[\\\"'](?<href>[^\\\"']+)[\\\"'][^>]*>(?<text>.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

    public string Name => "SubtitleCat";

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(string videoNumber, string language, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(Site + "/index.php?search=" + Uri.EscapeDataString(videoNumber), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var results = new List<SubtitleCandidate>();
        foreach (Match match in LinkRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var text = WebUtility.HtmlDecode(Regex.Replace(match.Groups["text"].Value, "<[^>]+>", " "));
            if (!href.Contains("/subtitle/", StringComparison.OrdinalIgnoreCase) || !MatchesCode(href + " " + text, videoNumber)) continue;
            results.Add(new SubtitleCandidate { Source = Name, Id = href, Language = language, Format = "srt", DownloadUrl = ToAbsolute(href), Title = text.Trim() });
        }
        return results;
    }

    public async Task<Stream> DownloadAsync(SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(candidate.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return new MemoryStream(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), writable: false);
    }

    private static string ToAbsolute(string href) => href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Site + (href.StartsWith("/") ? href : "/" + href);

    private static bool MatchesCode(string value, string code)
    {
        var normalizedValue = Regex.Replace(value, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        var normalizedCode = Regex.Replace(code, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        return normalizedCode.Length > 0 && normalizedValue.Contains(normalizedCode, StringComparison.Ordinal);
    }
}
