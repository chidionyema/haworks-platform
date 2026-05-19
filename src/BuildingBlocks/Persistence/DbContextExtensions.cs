using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Haworks.BuildingBlocks.Persistence;

public static class DbContextExtensions
{
    /// <summary>
    /// Adds all platform-registered interceptors (SagaPersistenceInterceptor, etc.)
    /// to this DbContext. Call from every AddDbContext((sp, options) => ...) block.
    /// </summary>
    public static DbContextOptionsBuilder AddPlatformInterceptors(
        this DbContextOptionsBuilder options, IServiceProvider sp)
    {
        var interceptors = sp.GetServices<ISaveChangesInterceptor>();
        if (interceptors.Any())
            options.AddInterceptors(interceptors.ToArray());
        return options;
    }
}
