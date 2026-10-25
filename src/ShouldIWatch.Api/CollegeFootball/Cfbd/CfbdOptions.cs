namespace ShouldIWatch.Api.CollegeFootball.Cfbd;

/// <summary>CollegeFootballData.com settings, bound from the <c>Cfbd</c> configuration section.</summary>
internal sealed class CfbdOptions
{
    public const string SectionName = "Cfbd";

    public const string MissingApiKeyMessage =
        "Cfbd:ApiKey is not configured. Set the AppHost secret with " +
        "`dotnet user-secrets set Parameters:cfbd-api-key <key> --project src/ShouldIWatch.AppHost`.";

    public Uri BaseUrl { get; set; } = new("https://api.collegefootballdata.com");

    public string ApiKey { get; set; } = "";
}
