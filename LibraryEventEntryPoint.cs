using System;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using MediaBrowser.Model.Logging;

namespace JavSubtitleScraper;

public sealed class LibraryEventEntryPoint : IServerEntryPoint
{
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;

    public LibraryEventEntryPoint(ILibraryManager libraryManager, ILogManager logManager)
    {
        _libraryManager = libraryManager;
        _logger = logManager.GetLogger(nameof(LibraryEventEntryPoint));
        _libraryManager.ItemAdded += OnItemChanged;
        _libraryManager.ItemUpdated += OnItemChanged;
    }

    private void OnItemChanged(object? sender, ItemChangeEventArgs args)
    {
        if (!Plugin.Instance.Configuration.EnableLibraryEvents || string.IsNullOrWhiteSpace(args.Item.Path)) return;
        var path = args.Item.Path;
        var durationMs = args.Item.RunTimeTicks.GetValueOrDefault() > 0 ? args.Item.RunTimeTicks.GetValueOrDefault() / TimeSpan.TicksPerMillisecond : 0;
        _ = System.Threading.Tasks.Task.Run(async () =>
        {
            try
            {
                if (SubtitleScanTask.Current != null)
                    await SubtitleScanTask.Current.ProcessSingleAsync(path, durationMs, default).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger.Error($"Library event subtitle processing failed: {ex.Message}"); }
        });
    }

    public void Run() { }

    public void Dispose()
    {
        _libraryManager.ItemAdded -= OnItemChanged;
        _libraryManager.ItemUpdated -= OnItemChanged;
    }
}
