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

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(string videoNumber, string language, long videoDurationMs, CancellationToken cancellationToken)
    {
        var queryCodes = new List<string> { videoNumber };
        for (var queryIndex = 0; queryIndex < queryCodes.Count; queryIndex++)
        {
            var queryCode = queryCodes[queryIndex];
            var rows = await RequestRowsAsync(queryCode, cancellationToken).ConfigureAwait(false);
            if (rows.Count == 0 || !rows.Any(row => CodeMatches(GetString(row, "name", "title"), queryCode)))
            {
                if (queryIndex == 0 && TryGetAlias(videoNumber, out var alias))
                {
                    queryCodes.Add(alias);
                    continue;
                }
                return Array.Empty<SubtitleCandidate>();
            }

            var results = new List<SubtitleCandidate>();
            foreach (var row in rows)
            {
                var url = GetString(row, "url", "subtitle_url", "download_url");
                var title = GetString(row, "name", "title", "extra_name");
                if (!IsSrtUrl(url) || ExplicitOtherCode(title, queryCode)) continue;
                var rank = LanguageRank(row, title, queryCode);
                if (rank <= 0) continue;
                var durationMs = GetInt64(row, "duration");
                if (Plugin.Instance.Configuration.EnableDurationFilter && videoDurationMs > 0 && durationMs > 0)
                {
                    var ratio = (double)durationMs / videoDurationMs;
                    var lower = Math.Clamp(Plugin.Instance.Configuration.DurationFilterLowerPercent, 1, 85) / 100d;
                    var upper = Math.Clamp(Plugin.Instance.Configuration.DurationFilterUpperPercent, 115, 300) / 100d;
                    if (lower <= upper && (ratio < lower || ratio > upper)) continue;
                }
                results.Add(new SubtitleCandidate { Source = Name, Id = url, Language = language, Format = "srt", DownloadUrl = url, Title = title, DurationMs = durationMs, LanguageRank = rank, QualityRank = QualityRank(title) });
            }

            return results
                .Select((candidate, index) => new { Candidate = candidate, Index = index })
                .OrderByDescending(item => item.Candidate.LanguageRank)
                .ThenByDescending(item => item.Candidate.QualityRank)
                // .ThenByDescending(item => item.Candidate.DurationMs)
                .ThenBy(item => item.Index)
                .Select(item => item.Candidate)
                .ToList();
        }
        return Array.Empty<SubtitleCandidate>();
    }

    public async Task<Stream> DownloadAsync(SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(candidate.DownloadUrl)) throw new ArgumentException("Subtitle download URL is empty.", nameof(candidate));
        using var response = await Client.GetAsync(candidate.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return new MemoryStream(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), writable: false);
    }

    private static async Task<List<JsonElement>> RequestRowsAsync(string videoNumber, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(Endpoint + "?name=" + Uri.EscapeDataString(videoNumber), cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
        var rows = new List<JsonElement>();
        CollectRows(document.RootElement, rows);
        return rows;
    }

    private static void CollectRows(JsonElement element, List<JsonElement> rows)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            if (!string.IsNullOrWhiteSpace(GetString(element, "url", "subtitle_url", "download_url"))) rows.Add(element.Clone());
            foreach (var property in element.EnumerateObject()) CollectRows(property.Value, rows);
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var item in element.EnumerateArray()) CollectRows(item, rows);
    }

    private static int LanguageRank(JsonElement row, string title, string videoNumber)
    {
        var languages = GetString(row, "languages");
        if (Regex.IsMatch(languages, "(?:简体|zh-cn|zh-hans|chs|simplified)", RegexOptions.IgnoreCase)) return 5;
        return LanguageRank(title, videoNumber);
    }

    private static int LanguageRank(string title, string videoNumber)
    {
        var stem = Path.GetFileNameWithoutExtension(title ?? string.Empty);
        stem = Regex.Replace(stem, @"\s*\(\d+\)\s*$", string.Empty);
        var code = (videoNumber ?? string.Empty).Trim();
        if (Regex.IsMatch(stem, "^[a-f0-9]{32,64}$", RegexOptions.IgnoreCase)) return 2;
        if (stem.Equals(code, StringComparison.OrdinalIgnoreCase)) return 3;
        if (!stem.StartsWith(code, StringComparison.OrdinalIgnoreCase)) return 0;
        var remainder = stem[code.Length..].ToLowerInvariant().Replace('_', '-');
        if (Regex.IsMatch(remainder, @"(?:zh-cn|zh-sg|zh-hans|chs|sc|simplified)", RegexOptions.IgnoreCase)) return 5;
        if (Regex.IsMatch(remainder, @"(?:zh-tw|zh-hk|zh-mo|cht|tc|zh-hant|traditional)", RegexOptions.IgnoreCase)) return 1;
        if (Regex.IsMatch(remainder, @"zh", RegexOptions.IgnoreCase)) return 4;
        if (Regex.IsMatch(remainder, @"(?:en|eng|ja|jpn|jp|ko|kor|fr|fra|de|ger|es|spa|it|ita|pt|rus)", RegexOptions.IgnoreCase)) return 0;
        return 3;
    }

    private static int QualityRank(string title)
    {
        var value = title ?? string.Empty;
        if (Regex.IsMatch(value, "精翻|精制翻译|人工翻译", RegexOptions.IgnoreCase)) return 2;
        if (Regex.IsMatch(value, "ai|whisper|机器翻译|机翻", RegexOptions.IgnoreCase)) return 0;
        return 1;
    }

    private static bool CodeMatches(string value, string code)
    {
        var escaped = Regex.Escape((code ?? string.Empty).Trim());
        return escaped.Length > 0 && Regex.IsMatch(value ?? string.Empty, "(?:^|[^A-Za-z0-9])" + escaped + "(?=$|[^A-Za-z0-9])", RegexOptions.IgnoreCase);
    }

    private static bool ExplicitOtherCode(string name, string code)
    {
        var match = Regex.Match(name ?? string.Empty, @"^([A-Za-z]{2,}-\d{2,}[A-Za-z]?)(?:[^A-Za-z0-9]|$)");
        return match.Success && !match.Groups[1].Value.Equals(code, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetAlias(string videoNumber, out string alias)
    {
        alias = (videoNumber ?? string.Empty).ToLowerInvariant();
        alias = Regex.Replace(alias, @"^smbd-(\d+)$", "smd-$1", RegexOptions.IgnoreCase);
        alias = Regex.Replace(alias, @"^([a-z]+)bd-(\d+)$", "$1-$2", RegexOptions.IgnoreCase);
        alias = Regex.Replace(alias, @"^(.*-)(\d+)$", m => m.Groups[1].Value + m.Groups[2].Value.TrimStart('0').PadLeft(1, '0'));
        return !alias.Equals((videoNumber ?? string.Empty).ToLowerInvariant(), StringComparison.Ordinal);
    }

    private static bool IsSrtUrl(string url) => Regex.IsMatch(url ?? string.Empty, @"\.srt(?:\?|$)", RegexOptions.IgnoreCase);

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (!element.TryGetProperty(name, out var value)) continue;
            if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? string.Empty;
            if (value.ValueKind == JsonValueKind.Array) return string.Join(" ", value.EnumerateArray().Select(item => item.ToString()));
        }
        return string.Empty;
    }

    private static long GetInt64(JsonElement element, string name)
    {
        return element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var result) ? result : 0;
    }
}
