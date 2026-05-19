using Xunit;

namespace Haworks.Audit.Integration;

[CollectionDefinition("AuditIntegration")]
public class AuditIntegrationTestDefinition : ICollectionFixture<AuditWebAppFactory>;
