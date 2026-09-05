using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace JavSubtitleScraper;

public interface ISubtitleSource
{
    string Name { get; }
    Task<IReadOnlyList<SubtitleCandidate>> SearchAsync(string videoNumber, string language, CancellationToken cancellationToken);
    Task<System.IO.Stream> DownloadAsync(SubtitleCandidate candidate, CancellationToken cancellationToken);
}
