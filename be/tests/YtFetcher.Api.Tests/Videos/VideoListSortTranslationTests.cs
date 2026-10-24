using Microsoft.EntityFrameworkCore;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Videos;

/// <summary>
/// The video list read is one joined, ordered, paged statement. The InMemory suite proves the ordering
/// result; this guard proves the same LINQ shape also translates to PostgreSQL SQL, because the Npgsql
/// provider refuses <c>ToLowerInvariant</c> and cannot order by an untranslatable projection. It needs no
/// database: the SQL is produced from the query plan only.
/// </summary>
public class VideoListSortTranslationTests
{
    [Fact]
    public void Case_insensitive_title_order_translates_to_postgresql()
    {
        using var context = CreateContext();

        var sql = VisibleVideos(context)
            .OrderBy(video => video.Title.ToLower())
            .ThenBy(video => video.Id)
            .Join(
                VisibleChannels(context),
                video => video.ChannelId,
                channel => channel.Id,
                (video, channel) => new { Video = video, ChannelName = channel.Name, channel.YoutubeChannelId })
            .Skip(0)
            .Take(10)
            .ToQueryString();

        Assert.Contains("lower(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("channels", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("youtube_channel_id", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Null_last_update_ordering_translates_to_a_null_test_in_both_directions()
    {
        using var context = CreateContext();

        var ascending = VisibleVideos(context)
            .OrderBy(video => video.LastStatsFetchedAt == null)
            .ThenBy(video => video.LastStatsFetchedAt)
            .ThenBy(video => video.Id)
            .Skip(0)
            .Take(10)
            .ToQueryString();

        var descending = VisibleVideos(context)
            .OrderBy(video => video.LastStatsFetchedAt == null)
            .ThenByDescending(video => video.LastStatsFetchedAt)
            .ThenBy(video => video.Id)
            .Skip(0)
            .Take(10)
            .ToQueryString();

        Assert.Contains("IS NULL", ascending, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("IS NULL", descending, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void The_list_read_is_one_statement_that_orders_before_it_pages()
    {
        using var context = CreateContext();

        var sql = VisibleVideos(context)
            .OrderByDescending(video => video.CurrentViews)
            .ThenBy(video => video.Id)
            .Join(
                VisibleChannels(context),
                video => video.ChannelId,
                channel => channel.Id,
                (video, channel) => new { Video = video, ChannelName = channel.Name, channel.YoutubeChannelId })
            .Skip(10)
            .Take(10)
            .ToQueryString();

        // One FROM means no second lookup, and the ORDER BY belongs to the same paged statement.
        // The videos table is read once: one outer SELECT with the join and the paging clause.
        Assert.Equal(1, CountOccurrences(sql, "FROM videos"));
        Assert.Contains("INNER JOIN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ORDER BY", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LIMIT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("OFFSET", sql, StringComparison.OrdinalIgnoreCase);
    }

    private static IQueryable<YtFetcher.Core.Videos.Video> VisibleVideos(YtFetcherDbContext context) =>
        context.Videos
            .AsNoTracking()
            .Where(video => !video.IsDeleted && !video.IsReference);

    private static IQueryable<YtFetcher.Core.Channels.Channel> VisibleChannels(YtFetcherDbContext context) =>
        context.Channels.AsNoTracking().Where(channel => !channel.IsDeleted);

    private static YtFetcherDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<YtFetcherDbContext>()
            .UseNpgsql("Host=localhost;Database=translation_only;Username=translation_only;Password=translation_only")
            .Options);

    private static int CountOccurrences(string value, string token)
    {
        var count = 0;
        var index = value.IndexOf(token, StringComparison.OrdinalIgnoreCase);
        while (index >= 0)
        {
            count++;
            index = value.IndexOf(token, index + token.Length, StringComparison.OrdinalIgnoreCase);
        }

        return count;
    }
}
