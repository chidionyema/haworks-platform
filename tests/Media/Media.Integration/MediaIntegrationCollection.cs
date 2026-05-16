using Xunit;

namespace Haworks.Media.Integration;

[CollectionDefinition("Media Integration")]
public sealed class MediaIntegrationCollection : ICollectionFixture<MediaWebAppFactory>
{
}
