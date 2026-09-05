using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace JavSubtitleScraper;

public sealed class SubtitleScanTask : IScheduledTask, IConfigurableScheduledTask
{
    private readonly ILibraryManager _libraryManager;
    private readonly IFileSystem _fileSystem;
    private readonly ILogger _logger;
    private readonly ISubtitleSource _subtitleSource;

    public SubtitleScanTask(ILogManager logManager, ILibraryManager libraryManager, IFileSystem fileSystem)
    {
        _logger = logManager.GetLogger(nameof(SubtitleScanTask));
        _libraryManager = libraryManager;
        _fileSystem = fileSystem;
        _subtitleSource = new XunleiSubtitleSource();
    }

    public string Name => Plugin.PluginName + ": 扫描并下载字幕";
    public string Key => Plugin.PluginName + ".Scan";
    public string Description => "扫描电影库并为 JAV 视频下载字幕";
    public string Category => Plugin.PluginName;
    public bool IsHidden => false;
    public bool IsEnabled => true;
    public bool IsLogged => true;
    public bool CategoryIsHidden => false;

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers() => Array.Empty<TaskTriggerInfo>();

    public async Task Execute(CancellationToken cancellationToken, IProgress<double> progress)
    {
        if (!Plugin.Instance.Configuration.EnableScheduledScan)
        {
            _logger.Info("Scheduled subtitle scan is disabled.");
            return;
        }

        await ScanAsync(cancellationToken, progress).ConfigureAwait(false);
    }

    public async Task RunManualAsync(CancellationToken cancellationToken, IProgress<double> progress)
    {
        if (!Plugin.Instance.Configuration.EnableManualScan)
        {
            _logger.Info("Manual subtitle scan is disabled.");
            return;
        }

        await ScanAsync(cancellationToken, progress).ConfigureAwait(false);
    }

    private async Task ScanAsync(CancellationToken cancellationToken, IProgress<double> progress)
    {
        var paths = _libraryManager.GetVirtualFolders()
            .Where(folder => string.Equals(folder.CollectionType, "movies", StringComparison.OrdinalIgnoreCase))
            .SelectMany(folder => folder.Locations ?? Array.Empty<string>())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var files = paths.SelectMany(path => GetVideoFiles(path, cancellationToken)).ToList();
        _logger.Info($"Found {files.Count} video files.");

        for (var index = 0; index < files.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var file = files[index];
            var number = NumberExtractor.FromPath(file.FullName);
            if (number == null)
                _logger.Debug($"Skipped file without JAV number: {file.FullName}");
            else
            {
                _logger.Debug($"Matched {number}: {file.FullName}");
                try
                {
                    var candidates = await _subtitleSource.SearchAsync(number, Plugin.Instance.Configuration.TargetLanguage, cancellationToken).ConfigureAwait(false);
                    if (candidates.Count > 0)
                    {
                        var candidate = candidates[0];
                        using var content = await _subtitleSource.DownloadAsync(candidate, cancellationToken).ConfigureAwait(false);
                        var saved = await SubtitleFileWriter.SaveAsync(file.FullName, candidate, content, Plugin.Instance.Configuration.OverwriteExistingSubtitles, cancellationToken).ConfigureAwait(false);
                        _logger.Info($"Downloaded subtitle for {number}: {saved}");
                    }
                    else
                        _logger.Warn($"No subtitle found for {number}.");
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Subtitle processing failed for {number}: {ex.Message}");
                }
            }

            progress.Report((index + 1d) / Math.Max(files.Count, 1) * 100d);
        }

        progress.Report(100);
    }

    private IEnumerable<FileSystemMetadata> GetVideoFiles(string path, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return _fileSystem.GetFiles(path, true)
                .Where(file => _libraryManager.IsVideoFile(file.FullName.AsSpan()))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger.Error($"Failed to scan {path}: {ex.Message}");
            return Array.Empty<FileSystemMetadata>();
        }
    }
}
