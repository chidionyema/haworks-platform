namespace Haworks.Media.Api.Options;

public sealed class TranscodeOptions
{
    public const string SectionName = "Transcode";

    public bool Enabled { get; set; }
    public string FfmpegPath { get; set; } = "/usr/bin/ffmpeg";
    public string FfprobePath { get; set; } = "/usr/bin/ffprobe";
    public string TempDirectory { get; set; } = Path.Combine(Path.GetTempPath(), "media-transcode");
    public int MaxConcurrentJobs { get; set; } = 2;
    public int TimeoutMinutes { get; set; } = 120;
    public int HlsSegmentSeconds { get; set; } = 6;
    public int MaxDurationMinutes { get; set; } = 480; // 8 hours

    public List<QualityTier> QualityTiers { get; set; } =
    [
        new() { Name = "1080p", Height = 1080, VideoBitrateKbps = 5000, MinSourceHeight = 1080 },
        new() { Name = "720p",  Height = 720,  VideoBitrateKbps = 2500, MinSourceHeight = 720 },
        new() { Name = "480p",  Height = 480,  VideoBitrateKbps = 1000, MinSourceHeight = 0 },
        new() { Name = "360p",  Height = 360,  VideoBitrateKbps = 500,  MinSourceHeight = 0 },
    ];
}

public sealed class QualityTier
{
    public string Name { get; set; } = string.Empty;
    public int Height { get; set; }
    public int VideoBitrateKbps { get; set; }
    public int MinSourceHeight { get; set; }
}
