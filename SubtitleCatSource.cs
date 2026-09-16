using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace JavSubtitleScraper;

public sealed class SubtitleCatSource : ISubtitleSource
{
    private const string Site = "https://subtitlecat.com";
    private const int MaxDetailPages = 5;
    private static readonly HttpClient Client = new();
    private static readonly Regex LinkRegex = new("<a[^>]+href=[\"'](?<href>[^\"']+)[\"'][^>]*>(?<text>.*?)</a>", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Singleline);
    private static readonly Regex RowRegex = new(@"<tr[\s\S]*?<a\s+href=[""'](?<href>[^""']+)[""'][^>]*>(?<text>[\s\S]*?)</a>[\s\S]*?</tr>", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public string Name => "SubtitleCat";

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(string videoNumber, string language, long videoDurationMs, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(Site + "/index.php?search=" + Uri.EscapeDataString(videoNumber) + "&show=1000", cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var matches = new List<SearchMatch>();
        var japaneseTranslationUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in RowRegex.Matches(html))
        {
            var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
            var rowText = CleanText(match.Value);
            if (!MatchesCode(href + " " + CleanText(match.Groups["text"].Value), videoNumber)) continue;
            var translatedFromChinese = Regex.IsMatch(rowText, @"translated\s+from\s+Chinese", RegexOptions.IgnoreCase);
            var translatedFromJapanese = Regex.IsMatch(rowText, @"translated\s+from\s+Japanese", RegexOptions.IgnoreCase);
            if (translatedFromJapanese) japaneseTranslationUrls.Add(ToAbsolute(href));
            matches.Add(new SearchMatch(ToAbsolute(href), translatedFromChinese, translatedFromChinese && Regex.IsMatch(rowText, "精翻", RegexOptions.IgnoreCase), ParseCount(rowText, "languages?"), ParseCount(rowText, "downloads?"), matches.Count));
        }

        foreach (var match in matches)
            match.TranslatedFromJapanese = japaneseTranslationUrls.Contains(match.Url);

        var pages = matches
            .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(item => item.TranslationTier)
            .ThenByDescending(item => item.FineTranslation)
            .ThenByDescending(item => item.Languages)
            .ThenByDescending(item => item.Downloads)
            .ThenBy(item => item.Index)
            .Take(MaxDetailPages)
            .ToList();
        if (pages.Count == 0) return Array.Empty<SubtitleCandidate>();

        var detailTasks = pages.Select((page, index) => { page.Priority = index; return ReadDetailPageAsync(page, language, cancellationToken); }).ToArray();
        var firstResult = await detailTasks[0].ConfigureAwait(false);
        if (firstResult.Any(item => item.LanguageRank == 5)) return firstResult;
        var pageResults = await Task.WhenAll(detailTasks).ConfigureAwait(false);
        return pageResults
            .SelectMany(result => result)
            .OrderByDescending(item => item.LanguageRank)
            .ThenByDescending(item => item.TranslationTier)
            .ThenByDescending(item => item.FineTranslation)
            .ThenByDescending(item => item.Languages)
            .ThenByDescending(item => item.Downloads)
            .ThenBy(item => item.PageIndex)
            .ToList();
    }

    public async Task<Stream> DownloadAsync(SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        using var response = await Client.GetAsync(candidate.DownloadUrl, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        return new MemoryStream(await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false), writable: false);
    }

    private static async Task<IReadOnlyList<SubtitleCandidate>> ReadDetailPageAsync(SearchMatch page, string language, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await Client.GetAsync(page.Url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var html = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var result = new List<SubtitleCandidate>();
            foreach (Match match in LinkRegex.Matches(html))
            {
                var href = WebUtility.HtmlDecode(match.Groups["href"].Value);
                if (!Regex.IsMatch(href, @"-zh-CN\.srt(?:\?|$)", RegexOptions.IgnoreCase) &&
                    !Regex.IsMatch(href, @"-zh-TW\.srt(?:\?|$)", RegexOptions.IgnoreCase)) continue;
                var rank = Regex.IsMatch(href, @"-zh-CN\.srt(?:\?|$)", RegexOptions.IgnoreCase) ? 5 : 1;
                var url = ToAbsolute(href);
                if (result.Any(item => item.DownloadUrl.Equals(url, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new SubtitleCandidate
                {
                    Source = "SubtitleCat",
                    Id = url,
                    Language = language,
                    Format = "srt",
                    DownloadUrl = url,
                    Title = CleanText(match.Groups["text"].Value),
                    LanguageRank = rank,
                    QualityRank = MaxDetailPages - page.Priority,
                    TranslationTier = page.TranslationTier,
                    FineTranslation = page.FineTranslation,
                    Languages = page.Languages,
                    Downloads = page.Downloads,
                    PageIndex = page.Index
                });
            }
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { return Array.Empty<SubtitleCandidate>(); }
    }

    private static string ToAbsolute(string href) => href.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? href : Site + (href.StartsWith("/") ? href : "/" + href);
    private static string CleanText(string value) => Regex.Replace(WebUtility.HtmlDecode(value ?? string.Empty), "<[^>]+>", " ").Replace("\r", " ").Replace("\n", " ").Trim();
    private static int ParseCount(string value, string suffix) => int.TryParse((Regex.Match(value, @"(\d[\d,]*)\s+" + suffix, RegexOptions.IgnoreCase).Groups[1].Value ?? string.Empty).Replace(",", string.Empty), out var count) ? count : 0;
    private static bool MatchesCode(string value, string code)
    {
        var normalizedValue = Regex.Replace(value, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        var normalizedCode = Regex.Replace(code, "[^A-Za-z0-9]", string.Empty).ToUpperInvariant();
        return normalizedCode.Length > 0 && normalizedValue.Contains(normalizedCode, StringComparison.Ordinal);
    }

    private sealed class SearchMatch
    {
        public SearchMatch(string url, bool translatedFromChinese, bool fineTranslation, int languages, int downloads, int index)
        {
            Url = url;
            TranslatedFromChinese = translatedFromChinese;
            FineTranslation = fineTranslation;
            Languages = languages;
            Downloads = downloads;
            Index = index;
        }

        public string Url { get; }
        public bool TranslatedFromChinese { get; }
        public bool TranslatedFromJapanese { get; set; }
        public bool FineTranslation { get; }
        public int TranslationTier => TranslatedFromChinese ? 3 : TranslatedFromJapanese ? 2 : 1;
        public int Languages { get; }
        public int Downloads { get; }
        public int Index { get; }
        public int Priority { get; set; }
    }
}
