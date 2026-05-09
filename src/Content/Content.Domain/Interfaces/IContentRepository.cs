using Haworks.Content.Domain.Entities;

namespace Haworks.Content.Domain.Interfaces;

/// <summary>
/// Repository for Content operations.
/// </summary>
public interface IContentRepository
{
    Task<IEnumerable<ContentEntity>> GetContentsByEntityIdAsync(Guid entityId, string entityType, CancellationToken ct = default);
    Task<ContentEntity?> GetContentByIdAsync(Guid id, CancellationToken ct = default);
    Task<ContentEntity?> GetContentByIdTrackedAsync(Guid id, CancellationToken ct = default);
    Task AddContentAsync(ContentEntity content, CancellationToken ct = default);
    Task AddContentsAsync(IEnumerable<ContentEntity> contents, CancellationToken ct = default);
    Task RemoveContentAsync(ContentEntity content, CancellationToken ct = default);
    void RemoveContents(IEnumerable<ContentEntity> contents);

    /// <summary>
    /// Pending rows created before <paramref name="cutoffUtc"/> — used
    /// by the sweeper to abort orphaned multipart uploads and mark the
    /// rows Failed.
    /// </summary>
    Task<IReadOnlyList<ContentEntity>> ListExpiredPendingAsync(
        DateTime cutoffUtc, int batchSize, CancellationToken ct = default);

    Task SaveChangesAsync(CancellationToken ct = default);
}
