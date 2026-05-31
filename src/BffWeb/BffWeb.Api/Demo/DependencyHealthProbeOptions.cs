namespace Haworks.BffWeb.Api.Demo;

public sealed class DependencyHealthProbeOptions
{
    public const string SectionName = "DependencyHealthProbe";

    public TimeSpan ProbeTimeout { get; set; } = TimeSpan.FromSeconds(5);
}