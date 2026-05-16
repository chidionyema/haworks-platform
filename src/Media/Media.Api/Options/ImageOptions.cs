namespace Haworks.Media.Api.Options;

public sealed class ImageOptions
{
    public const string SectionName = "Image";

    public bool Enabled { get; set; } = true;
    public List<int> ThumbnailSizes { get; set; } = [150, 300, 600];
    public int WebPQuality { get; set; } = 80;
    public int MaxDimensionPixels { get; set; } = 16384;
    public bool StripExifGps { get; set; } = true;
}
