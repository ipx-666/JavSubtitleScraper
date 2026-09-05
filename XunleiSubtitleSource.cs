using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
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
                var searchable = title + " " + url;
                if (MatchesCode(searchable, videoNumber))
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

    private static string GetString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString() ?? string.Empty;
        return string.Empty;
    }

    private static bool MatchesCode(string value, string videoNumber)
    {
        static string Normalize(string input) => string.Concat(input.ToUpperInvariant().Split(default(char[]), StringSplitOptions.RemoveEmptyEntries)).Replace("-", string.Empty).Replace("_", string.Empty);
        return Normalize(value).Contains(Normalize(videoNumber), StringComparison.Ordinal);
    }
}
