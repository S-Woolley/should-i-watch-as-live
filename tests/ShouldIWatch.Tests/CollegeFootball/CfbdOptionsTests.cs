using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using ShouldIWatch.Api.CollegeFootball;
using ShouldIWatch.Api.CollegeFootball.Cfbd;

namespace ShouldIWatch.Tests.CollegeFootball;

public sealed class CfbdOptionsTests
{
    [Fact]
    public async Task HostFailsToStartWithClearMessageWhenApiKeyIsMissing()
    {
        using var host = BuildHost(new Dictionary<string, string?>());

        var exception = await Assert.ThrowsAsync<OptionsValidationException>(
            () => host.StartAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Cfbd:ApiKey is not configured", exception.Message, StringComparison.Ordinal);
        Assert.Contains("Parameters:cfbd-api-key", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void BaseUrlDefaultsToCollegeFootballData()
    {
        using var host = BuildHost(new Dictionary<string, string?> { ["Cfbd:ApiKey"] = "key" });

        var options = host.Services.GetRequiredService<IOptions<CfbdOptions>>().Value;

        Assert.Equal(new Uri("https://api.collegefootballdata.com"), options.BaseUrl);
        Assert.Equal("key", options.ApiKey);
    }

    [Fact]
    public void BaseUrlCanBeOverridden()
    {
        using var host = BuildHost(new Dictionary<string, string?>
        {
            ["Cfbd:ApiKey"] = "key",
            ["Cfbd:BaseUrl"] = "http://127.0.0.1:9/",
        });

        Assert.Equal(new Uri("http://127.0.0.1:9/"), host.Services.GetRequiredService<IOptions<CfbdOptions>>().Value.BaseUrl);
    }

    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddCollegeFootball();
        return builder.Build();
    }
}
