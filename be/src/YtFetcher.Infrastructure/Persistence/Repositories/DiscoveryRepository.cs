using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using YtFetcher.Application.Abstractions.Repositories;
using YtFetcher.Application.Discovery.DTO.Result;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Users;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Infrastructure.Persistence.Repositories;

public sealed class DiscoveryRepository(YtFetcherDbContext dbContext) : IDiscoveryRepository
{
    public async Task<IReadOnlyList<DiscoveryKeyword>> GetActiveKeywordsAsync(
        CancellationToken cancellationToken) =>
        await dbContext.DiscoveryKeywords
            .AsNoTracking()
            .Where(keyword => keyword.IsActive && !keyword.IsDeleted)
            .OrderBy(keyword => keyword.CreatedAt)
            .ThenBy(keyword => keyword.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DiscoveryState>> GetStatesForKeywordsAsync(
        IReadOnlyCollection<Guid> keywordIds,
        CancellationToken cancellationToken)
    {
        if (keywordIds.Count == 0)
        {
            return [];
        }

        return await dbContext.DiscoveryStates
            .AsNoTracking()
            .Where(state => keywordIds.Contains(state.KeywordId) && !state.IsDeleted)
            .ToListAsync(cancellationToken);
    }

    public async Task<DiscoveryPersistResult> PersistKeywordDiscoveryAsync(
        DiscoveryKeyword keyword,
        IReadOnlyList<YouTubeChannelCandidate> candidates,
        string? nextPageToken,
        DateTimeOffset utcNow,
        CancellationToken cancellationToken,
        DateOnly quotaDate)
    {
        IDbContextTransaction? transaction = null;

        try
        {
            if (dbContext.Database.IsRelational())
            {
                transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);
            }

            var uniqueChannelIds = candidates
                .Select(candidate => candidate.YouTubeChannelId)
                .Distinct()
                .ToList();

            var existingChannels = await dbContext.Channels
                .Where(channel => uniqueChannelIds.Contains(channel.YoutubeChannelId))
                .ToDictionaryAsync(channel => channel.YoutubeChannelId, cancellationToken);

            var resolvedChannels = new Dictionary<string, Channel>();
            var channelsCreated = 0;
            var channelsRefreshed = 0;
            var softDeletedChannelsIgnored = 0;

            foreach (var channelId in uniqueChannelIds)
            {
                var metadata = candidates.First(candidate => candidate.YouTubeChannelId == channelId);

                if (existingChannels.TryGetValue(channelId, out var existingChannel))
                {
                    if (existingChannel.IsDeleted)
                    {
                        softDeletedChannelsIgnored++;
                        continue;
                    }

                    existingChannel.Name = metadata.ChannelName;
                    existingChannel.ThumbnailUrl = metadata.ChannelThumbnailUrl;
                    existingChannel.UploadsPlaylistId = metadata.UploadsPlaylistId;
                    existingChannel.ModifiedAt = utcNow;
                    existingChannel.ModifiedByUserId = User.SystemUserId;

                    resolvedChannels[channelId] = existingChannel;
                    channelsRefreshed++;
                    continue;
                }

                var newChannel = new Channel
                {
                    Id = Guid.NewGuid(),
                    YoutubeChannelId = channelId,
                    Name = metadata.ChannelName,
                    ThumbnailUrl = metadata.ChannelThumbnailUrl,
                    UploadsPlaylistId = metadata.UploadsPlaylistId,
                    Status = ChannelStatus.PENDING,
                    BackfillStatus = BackfillStatus.PENDING,
                    IsDeleted = false,
                    DiscoveredAt = utcNow,
                    CreatedAt = utcNow,
                    CreatedByUserId = User.SystemUserId,
                    ModifiedAt = utcNow,
                    ModifiedByUserId = User.SystemUserId
                };

                dbContext.Channels.Add(newChannel);
                existingChannels[channelId] = newChannel;
                resolvedChannels[channelId] = newChannel;
                channelsCreated++;
            }

            // PostgreSQL enforces the parent FK. Persist new channels before adding their evidence,
            // while the surrounding transaction keeps this keyword page atomic.
            if (channelsCreated > 0)
            {
                await dbContext.SaveChangesAsync(cancellationToken);
            }

            var candidateVideoIds = candidates
                .Select(candidate => candidate.YouTubeVideoId)
                .Distinct()
                .ToList();

            var existingVideoIds = await dbContext.ChannelDiscoveries
                .Where(discovery => candidateVideoIds.Contains(discovery.YoutubeVideoId))
                .Select(discovery => discovery.YoutubeVideoId)
                .ToListAsync(cancellationToken);

            var knownVideoIds = existingVideoIds.ToHashSet();

            var provenanceRowsCreated = 0;
            var duplicateProvenanceRows = 0;

            foreach (var candidate in candidates)
            {
                if (!resolvedChannels.TryGetValue(candidate.YouTubeChannelId, out var channel))
                {
                    continue;
                }

                if (!knownVideoIds.Add(candidate.YouTubeVideoId))
                {
                    duplicateProvenanceRows++;
                    continue;
                }

                dbContext.ChannelDiscoveries.Add(new ChannelDiscovery
                {
                    Id = Guid.NewGuid(),
                    ChannelId = channel.Id,
                    KeywordId = keyword.Id,
                    YoutubeVideoId = candidate.YouTubeVideoId,
                    VideoTitle = candidate.VideoTitle,
                    VideoThumbnailUrl = candidate.VideoThumbnailUrl,
                    VideoPublishedAt = candidate.VideoPublishedAt,
                    DiscoveredAt = utcNow,
                    IsDeleted = false,
                    CreatedAt = utcNow,
                    CreatedByUserId = User.SystemUserId,
                    ModifiedAt = utcNow,
                    ModifiedByUserId = User.SystemUserId
                });

                provenanceRowsCreated++;
            }

            var state = await dbContext.DiscoveryStates
                .SingleOrDefaultAsync(existing => existing.KeywordId == keyword.Id, cancellationToken);

            if (state is null)
            {
                state = new DiscoveryState
                {
                    Id = Guid.NewGuid(),
                    KeywordId = keyword.Id,
                    IsDeleted = false,
                    CreatedAt = utcNow,
                    CreatedByUserId = User.SystemUserId
                };

                dbContext.DiscoveryStates.Add(state);
            }

            state.RecentPageToken = nextPageToken;
            if (nextPageToken is null)
            {
                state.RecentCompletedQuotaDate = quotaDate;
            }

            state.LastRecentCompletedAt = utcNow;
            state.ModifiedAt = utcNow;
            state.ModifiedByUserId = User.SystemUserId;

            await dbContext.SaveChangesAsync(cancellationToken);

            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }

            return new DiscoveryPersistResult(
                channelsCreated,
                channelsRefreshed,
                softDeletedChannelsIgnored,
                provenanceRowsCreated,
                duplicateProvenanceRows);
        }
        catch
        {
            if (transaction is not null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }

            dbContext.ChangeTracker.Clear();
            throw;
        }
        finally
        {
            if (transaction is not null)
            {
                await transaction.DisposeAsync();
            }
        }
    }
}
