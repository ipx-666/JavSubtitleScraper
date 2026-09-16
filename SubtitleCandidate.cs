namespace JavSubtitleScraper;

/// <summary>
/// A subtitle result returned by a source adapter.
/// </summary>
public sealed class SubtitleCandidate
{
    public string Source { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;
    public string Format { get; set; } = "srt";
    public string DownloadUrl { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long DurationMs { get; set; }
    public int LanguageRank { get; set; }
    public int QualityRank { get; set; }
    public int TranslationTier { get; set; }
    public bool FineTranslation { get; set; }
    public int Languages { get; set; }
    public int Downloads { get; set; }
    public int PageIndex { get; set; }
}
