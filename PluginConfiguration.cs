using System;
using MediaBrowser.Model.Plugins;

namespace JavSubtitleScraper;

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool EnableScheduledScan { get; set; }
    public bool EnableManualScan { get; set; } = true;
    public bool EnableLibraryEvents { get; set; } = true;
    public bool ForceFullScan { get; set; }
    public bool OverwriteExistingSubtitles { get; set; }
    public string TargetLanguage { get; set; } = "zh-CN";
    public int MaxConcurrency { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public string ScheduleMode { get; set; } = "Daily";
    public string DailyTime { get; set; } = "03:00";
    public DayOfWeek WeeklyDay { get; set; } = DayOfWeek.Sunday;
    public string WeeklyTime { get; set; } = "03:00";
}
