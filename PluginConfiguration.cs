using MediaBrowser.Model.Plugins;

namespace JavSubtitleScraper;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableScheduledScan { get; set; }
    public bool EnableManualScan { get; set; } = true;
    public bool OverwriteExistingSubtitles { get; set; }
    public string TargetLanguage { get; set; } = "zh-CN";
    public int MaxConcurrency { get; set; } = 1;
}
