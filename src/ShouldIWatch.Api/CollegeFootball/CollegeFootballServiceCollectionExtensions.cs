using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using ShouldIWatch.Api.CollegeFootball.Cfbd;
using ShouldIWatch.Api.CollegeFootball.Data;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball;

internal static class CollegeFootballServiceCollectionExtensions
{
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(30);

    public static IServiceCollection AddCollegeFootball(this IServiceCollection services)
    {
        services.AddOptions<CfbdOptions>()
            .BindConfiguration(CfbdOptions.SectionName)
            .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey), CfbdOptions.MissingApiKeyMessage)
            .ValidateOnStart();

        // Swapping data providers is a change to this one registration.
        var cfbdClient = services.AddHttpClient<ICollegeFootballDataSource, CfbdDataSource>((serviceProvider, client) =>
            {
                var options = serviceProvider.GetRequiredService<IOptions<CfbdOptions>>().Value;
                client.BaseAddress = options.BaseUrl;
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", options.ApiKey);
                client.Timeout = ProviderTimeout;
            });

        // One claimed fetch must stay one set of calls, so the default resilience handler (with retries) is removed.
#pragma warning disable EXTEXP0001 // Experimental, but the supported way to opt one client out of AddServiceDefaults resilience.
        cfbdClient.RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001

        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<CfbGameRepository>();
        services.AddScoped<CfbSyncStateRepository>();
        services.AddScoped<CfbRivalryRepository>();
        services.AddScoped<CollegeFootballSyncService>();

        return services;
    }
}
