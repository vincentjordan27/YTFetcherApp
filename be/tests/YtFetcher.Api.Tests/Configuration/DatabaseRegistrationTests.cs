using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using YtFetcher.Infrastructure.Persistence;

namespace YtFetcher.Api.Tests.Configuration;

[Collection(ApiStartupCollection.Name)]
public class DatabaseRegistrationTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    [Fact]
    public void CreateScope_resolves_the_postgres_db_context()
    {
        using var databaseConfiguration = new HostConfigurationScope();
        using var application = factory.WithWebHostBuilder(_ => { });
        using var scope = application.Services.CreateScope();

        var context = scope.ServiceProvider.GetRequiredService<YtFetcherDbContext>();

        Assert.NotNull(context);
    }
}
