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
    private static readonly Regex LinkRegex = new("<a[^>]+href=[\\"'](?<href>[^\\"']+)[\\"'][^>]*>(?<text>.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);

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
            if (!MatchesCode(href + " " + text, videoNumber)) continue;
            var title = text.Trim();
            if (LanguageScore(title) < 2) continue;
            results.Add(new SubtitleCandidate { Source = Name, Id = href, Language = language, Format = "srt", DownloadUrl = ToAbsolute(href), Title = title });
        }
        results.Sort((left, right) => LanguageScore(right.Title).CompareTo(LanguageScore(left.Title)));
        return results;
    }

    public async Task<Stream> DownloadAsync(SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(candidate.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        var contentType = response.Content.Headers.ContentType?.MediaType ?? string.Empty;
        if (contentType.Contains("subtitle", StringComparison.OrdinalIgnoreCase) || contentType.Contains("text/plain", StringComparison.OrdinalIgnoreCase) || candidate.DownloadUrl.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            return new MemoryStream(bytes, writable: false);
        var html = System.Text.Encoding.UTF8.GetString(bytes);
        foreach (Match match in LinkRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            if (!href.Contains(".srt", StringComparison.OrdinalIgnoreCase) && !href.Contains("download.php", StringComparison.OrdinalIgnoreCase)) continue;
            using var subtitleResponse = await Client.GetAsync(ToAbsolute(href), cancellationToken).ConfigureAwait(false);
            subtitleResponse.EnsureSuccessStatusCode();
            return new MemoryStream(await subtitleResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false), writable: false);
        }
        throw new InvalidDataException("SubtitleCat page did not contain a subtitle download link.");
    }

    private static string ToAbsolute(string href) => href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Site + (href.StartsWith("/") ? href : "/" + href);
    private static bool MatchesCode(string value, string code)
    {
        var normalizedValue = Regex.Replace(value, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        var normalizedCode = Regex.Replace(code, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        return normalizedCode.Length > 0 && normalizedValue.Contains(normalizedCode, StringComparison.Ordinal);
    }
    private static int LanguageScore(string value)
    {
        var text = value.ToLowerInvariant();
        if (text.Contains("zh-cn") || text.Contains("zh_cn") || text.Contains("simplified") || text.Contains("chs") || text.Contains("\u7b80\u4f53")) return 3;
        if (text.Contains("zh") || text.Contains("cn") || text.Contains("chinese") || text.Contains("\u4e2d\u6587")) return 2;
        return 1;
    }
}
