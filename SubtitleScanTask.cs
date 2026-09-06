using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Tasks;

namespace JavSubtitleScraper;

public sealed class SubtitleScanTask : IScheduledTask, IConfigurableScheduledTask
{
    internal static SubtitleScanTask? Current { get; private set; }
    private readonly SemaphoreSlim _concurrencyGate;
    private readonly ILibraryManager _libraryManager;
    private readonly ILogger _logger;
    private readonly ISubtitleSource _subtitleSource;

    public SubtitleScanTask(ILogManager logManager, ILibraryManager libraryManager)
    {
        _logger = logManager.GetLogger(nameof(SubtitleScanTask));
        _libraryManager = libraryManager;
        _subtitleSource = new SubtitleSourceChain(new XunleiSubtitleSource(), new SubtitleCatSource());
        var concurrency = Math.Clamp(Plugin.Instance?.Configuration.MaxConcurrency ?? 4, 1, 8);
        _concurrencyGate = new SemaphoreSlim(concurrency, concurrency);
        Current = this;
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
        if (!Plugin.Instance.Configuration.EnableManualScan)
        {
            _logger.Info("Manual subtitle scan is disabled.");
            return;
        }

        await ScanAsync(cancellationToken, progress, Plugin.Instance.Configuration.ForceFullScan).ConfigureAwait(false);
    }

    internal async Task ScanAsync(CancellationToken cancellationToken, IProgress<double> progress, bool forceFullScan)
    {
        var items = _libraryManager.GetItemList(new InternalItemsQuery
        {
            Recursive = true,
            IncludeItemTypes = new[] { "Movie", "Video" },
            IsFolder = false
        });
        var files = items
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .Select(item => item.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        _logger.Info($"Found {files.Count} video files.");

        var completed = 0;
        var jobs = files.Select(async file =>
        {
            await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await ProcessFileAsync(file, forceFullScan, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                var index = Interlocked.Increment(ref completed);
                progress.Report(index / (double)Math.Max(files.Count, 1) * 100d);
                _concurrencyGate.Release();
            }
        });
        await Task.WhenAll(jobs).ConfigureAwait(false);
        progress.Report(100);
    }

    private async Task ProcessFileAsync(string file, bool forceFullScan, CancellationToken cancellationToken)
    {
            cancellationToken.ThrowIfCancellationRequested();
            var number = NumberExtractor.FromPath(file);
            if (number == null)
                _logger.Debug($"Skipped file without JAV number: {file}");
            else
            {
                _logger.Debug($"Matched {number}: {file}");
                if (!forceFullScan && HasSubtitle(file, Plugin.Instance.Configuration.TargetLanguage))
                {
                    _logger.Debug($"Skipped existing subtitle for {number}: {file}");
                    return;
                }
                try
                {
                    var candidates = await _subtitleSource.SearchAsync(number, Plugin.Instance.Configuration.TargetLanguage, cancellationToken).ConfigureAwait(false);
                    if (candidates.Count == 0)
                        _logger.Warn($"No subtitle found for {number}.");
                    else
                    {
                        Exception? lastError = null;
                        foreach (var candidate in candidates)
                        {
                            try
                            {
                                using var content = await _subtitleSource.DownloadAsync(candidate, cancellationToken).ConfigureAwait(false);
                                var saved = await SubtitleFileWriter.SaveAsync(file, candidate, content, Plugin.Instance.Configuration.OverwriteExistingSubtitles, cancellationToken).ConfigureAwait(false);
                                _logger.Info($"Downloaded subtitle for {number}: {saved}");
                                lastError = null;
                                break;
                            }
                            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                            {
                                throw;
                            }
                            catch (Exception candidateError)
                            {
                                lastError = candidateError;
                            }
                        }

                        if (lastError != null)
                            _logger.Error($"All subtitle candidates failed for {number}: {lastError.Message}");
                    }
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

    }

    private static bool HasSubtitle(string videoPath, string language)
    {
        var directory = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrWhiteSpace(directory)) return false;
        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var suffix = "." + (string.IsNullOrWhiteSpace(language) ? "und" : language.Trim()) + ".";
        return Directory.Exists(directory) && Directory.EnumerateFiles(directory, baseName + suffix + "*", SearchOption.TopDirectoryOnly).Any();
    }

    internal async Task ProcessSingleAsync(string videoPath, CancellationToken cancellationToken)
    {
        await _concurrencyGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
        var number = NumberExtractor.FromPath(videoPath);
        if (number == null || HasSubtitle(videoPath, Plugin.Instance.Configuration.TargetLanguage)) return;
        var candidates = await _subtitleSource.SearchAsync(number, Plugin.Instance.Configuration.TargetLanguage, cancellationToken).ConfigureAwait(false);
        foreach (var candidate in candidates)
        {
            try
            {
                using var content = await _subtitleSource.DownloadAsync(candidate, cancellationToken).ConfigureAwait(false);
                await SubtitleFileWriter.SaveAsync(videoPath, candidate, content, Plugin.Instance.Configuration.OverwriteExistingSubtitles, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { }
        }
        }
        finally
        {
            _concurrencyGate.Release();
        }
    }

}
