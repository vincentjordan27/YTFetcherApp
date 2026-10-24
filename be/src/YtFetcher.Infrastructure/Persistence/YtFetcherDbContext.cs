using Microsoft.EntityFrameworkCore;
using YtFetcher.Core.BackfillRuns;
using YtFetcher.Core.Channels;
using YtFetcher.Core.Discovery;
using YtFetcher.Core.Quotas;
using YtFetcher.Core.Users;
using YtFetcher.Core.Videos;

namespace YtFetcher.Infrastructure.Persistence;

public sealed class YtFetcherDbContext(DbContextOptions<YtFetcherDbContext> options) : DbContext(options)
{
    public DbSet<Channel> Channels => Set<Channel>();

    public DbSet<DiscoveryKeyword> DiscoveryKeywords => Set<DiscoveryKeyword>();

    public DbSet<DiscoveryState> DiscoveryStates => Set<DiscoveryState>();

    public DbSet<ChannelDiscovery> ChannelDiscoveries => Set<ChannelDiscovery>();

    public DbSet<Video> Videos => Set<Video>();

    public DbSet<VideoSnapshot> VideoSnapshots => Set<VideoSnapshot>();

    public DbSet<YouTubeDailyQuotaUsage> YouTubeDailyQuotaUsages => Set<YouTubeDailyQuotaUsage>();

    public DbSet<ChannelBackfillRun> ChannelBackfillRuns => Set<ChannelBackfillRun>();

    public DbSet<User> Users => Set<User>();
}
