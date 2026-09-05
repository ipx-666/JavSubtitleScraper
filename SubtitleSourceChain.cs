using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JavSubtitleScraper;

public sealed class SubtitleSourceChain : ISubtitleSource
{
    private readonly IReadOnlyList<ISubtitleSource> _sources;

    public SubtitleSourceChain(params ISubtitleSource[] sources)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
    }

    public string Name => "优先级字幕源";

    public async Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(string videoNumber, string language, CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
        {
            try
            {
                var results = await source.SearchAsync(videoNumber, language, cancellationToken).ConfigureAwait(false);
                if (results.Count > 0) return results;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
                // A failed source should not prevent the next source from being tried.
            }
        }

        return Array.Empty<SubtitleCandidate>();
    }

    public Task<System.IO.Stream> DownloadAsync(SubtitleCandidate candidate, CancellationToken cancellationToken)
    {
        foreach (var source in _sources)
            if (string.Equals(source.Name, candidate.Source, StringComparison.OrdinalIgnoreCase))
                return source.DownloadAsync(candidate, cancellationToken);
        throw new InvalidOperationException("Subtitle source is not registered: " + candidate.Source);
    }
}
