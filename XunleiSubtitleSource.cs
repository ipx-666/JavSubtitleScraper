using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JavSubtitleScraper;

public sealed class XunleiSubtitleSource : ISubtitleSource
{
    private const string Endpoint = "https://api-shoulei-ssl.xunlei.com/oracle/subtitle";
    private static readonly HttpClient Client = new();

    public string Name => "Xunlei";

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(string videoNumber, string language, CancellationToken cancellationToken)
    {
        var url = Endpoint + "?name=" + Uri.EscapeDataString(videoNumber);
        using var response = await Client.GetAsync(url, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);

        var results = new List<SubtitleCandidate>();
        CollectCandidates(document.RootElement, videoNumber, language, results);
        results = results
            .Select((candidate, index) => new { Candidate = candidate, Rank = LanguageRank(candidate.Title, videoNumber), Index = index })
            .OrderByDescending(item => item.Rank)
            .ThenBy(item => item.Index)
            .Select(item => item.Candidate)
            .ToList();
        return results;
    }

    public async Task<Stream> DownloadAsync(SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.DownloadUrl))
            throw new ArgumentException("Subtitle download URL is empty.", nameof(candidate));

        using var response = await Client.GetAsync(candidate.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
        return new MemoryStream(bytes, writable: false);
    }

    private void CollectCandidates(JsonElement element, string videoNumber, string language, List<SubtitleCandidate> results)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var url = GetString(element, "url", "subtitle_url", "download_url");
            if (!string.IsNullOrWhiteSpace(url) && url.StartsWith("http", StringComparison.OrdinalIgnoreCase) &&
                (url.EndsWith(".srt", StringComparison.OrdinalIgnoreCase) || url.Contains(".srt?", StringComparison.OrdinalIgnoreCase)))
            {
                var title = GetString(element, "name", "title", "extra_name");
                if (MatchesCode(title, videoNumber) && LanguageRank(title, videoNumber) > 0)
                {
                    results.Add(new SubtitleCandidate
                    {
                        Source = Name,
                        Id = url,
                        Language = language,
                        Format = "srt",
                        DownloadUrl = url,
                        Title = title
                    });
                }
            }

            foreach (var property in element.EnumerateObject())
                CollectCandidates(property.Value, videoNumber, language, results);
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                CollectCandidates(item, videoNumber, language, results);
        }
    }

    private static int LanguageRank(string name, string videoNumber)
    {
        var value = name ?? string.Empty;
        var stem = value.Split('?', '#')[0].TrimEnd('/');
        var slash = stem.LastIndexOf('/');
        if (slash >= 0) stem = stem[(slash + 1)..];
        if (stem.EndsWith(".srt", StringComparison.OrdinalIgnoreCase))
            stem = stem[..^4];
        stem = Regex.Replace(stem, @"\s*\(\d+\)\s*$", string.Empty);

        var code = videoNumber.Trim();
        if (stem.Equals(code, StringComparison.OrdinalIgnoreCase)) return 3;
        if (!stem.StartsWith(code, StringComparison.OrdinalIgnoreCase)) return 0;
        var remainder = stem[code.Length..].ToLowerInvariant().Replace('_', '-');
        if (Regex.IsMatch(remainder, @"(?:^|[._-])(?:zh-cn|zh-sg|zh-hans|chs|sc|simplified)(?:$|[._-])", RegexOptions.IgnoreCase)) return 5;
        if (Regex.IsMatch(remainder, @"(?:^|[._-])(?:zh-tw|zh-hk|zh-mo|cht|tc|zh-hant|traditional)(?:$|[._-])", RegexOptions.IgnoreCase)) return 1;
        if (Regex.IsMatch(remainder, @"(?:^|[._-])zh(?:$|[._-])", RegexOptions.IgnoreCase)) return 4;
        if (Regex.IsMatch(remainder, @"(?:^|[._-])(?:en|eng|ja|jpn|jp|ko|kor|fr|fra|de|ger|es|spa|it|ita|pt|rus)(?:$|[._-])", RegexOptions.IgnoreCase)) return 0;
        return 3;
    }

    private static bool MatchesCode(string value, string videoNumber)
    {
        var text = Path.GetFileNameWithoutExtension(value ?? string.Empty);
        var code = (videoNumber ?? string.Empty).Trim();
        if (code.Length == 0 || !text.StartsWith(code, StringComparison.OrdinalIgnoreCase)) return false;
        return text.Length == code.Length || !char.IsDigit(text[code.Length]);
    }

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? string.Empty;
        return string.Empty;
    }

}
