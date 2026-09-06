using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace JavSubtitleScraper;

public sealed class ScheduledSubtitleScanTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly SubtitleScanTask _scanTask;

    public ScheduledSubtitleScanTask(SubtitleScanTask scanTask) => _scanTask = scanTask;

    public string Name => Plugin.PluginName + ": 定时增量扫描";
    public string Key => Plugin.PluginName + ".ScheduledScan";
    public string Description => "按计划增量扫描媒体库并下载 JAV 字幕";
    public string Category => Plugin.PluginName;
    public bool IsHidden => false;
    public bool IsEnabled => true;
    public bool IsLogged => true;
    public bool CategoryIsHidden => false;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        var config = Plugin.Instance.Configuration;
        if (!config.EnableScheduledScan) return Array.Empty<TaskTriggerInfo>();
        var rawTime = config.ScheduleMode == "Weekly" ? config.WeeklyTime : config.DailyTime;
        if (!TimeSpan.TryParse(rawTime, out var time)) time = new TimeSpan(3, 0, 0);
        var trigger = new TaskTriggerInfo
        {
            Type = config.ScheduleMode == "Weekly" ? TaskTriggerInfo.TriggerWeekly : TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = time.Ticks
        };
        if (config.ScheduleMode == "Weekly") trigger.DayOfWeek = config.WeeklyDay;
        return new[] { trigger };
    }

    public Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        if (!Plugin.Instance.Configuration.EnableScheduledScan) return Task.CompletedTask;
        return _scanTask.ScanAsync(cancellationToken, progress, false);
    }
}
