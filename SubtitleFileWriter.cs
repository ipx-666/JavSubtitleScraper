using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace JavSubtitleScraper;

public static class SubtitleFileWriter
{
    public static async Task<string> SaveAsync(string videoPath, SubtitleCandidate candidate, Stream content, bool overwrite, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(videoPath)) throw new ArgumentException("Video path is required.", nameof(videoPath));
        if (candidate == null) throw new ArgumentNullException(nameof(candidate));
        if (content == null) throw new ArgumentNullException(nameof(content));

        var directory = Path.GetDirectoryName(videoPath);
        if (string.IsNullOrWhiteSpace(directory)) throw new InvalidOperationException("Video path has no parent directory.");
        Directory.CreateDirectory(directory);

        var baseName = Path.GetFileNameWithoutExtension(videoPath);
        var language = string.IsNullOrWhiteSpace(candidate.Language) ? "und" : candidate.Language.Trim();
        var extension = NormalizeExtension(candidate.Format);
        var targetPath = Path.Combine(directory, baseName + "." + language + "." + extension);
        if (!overwrite && File.Exists(targetPath)) return targetPath;

        var temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var output = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await content.CopyToAsync(output, 81920, cancellationToken).ConfigureAwait(false);
            if (overwrite && File.Exists(targetPath)) File.Delete(targetPath);
            File.Move(temporaryPath, targetPath);
            return targetPath;
        }
        finally
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    private static string NormalizeExtension(string value)
    {
        var extension = (value ?? string.Empty).Trim().TrimStart('.').ToLowerInvariant();
        return extension == "srt" || extension == "ass" || extension == "ssa" || extension == "vtt" ? extension : "srt";
    }
}
