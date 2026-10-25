using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json;
using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Api.CollegeFootball.Cfbd;

/// <summary>
/// <see cref="ICollegeFootballDataSource"/> over the CollegeFootballData.com API. A typed <see cref="HttpClient"/>
/// (base address, bearer key and timeout configured at registration) with retries turned off, so one claimed fetch
/// stays one set of calls.
/// </summary>
internal sealed partial class CfbdDataSource(HttpClient httpClient, ILogger<CfbdDataSource> logger) : ICollegeFootballDataSource
{
    public const string Name = "cfbd";

    private const string CfpPoll = "Playoff Committee Rankings";
    private const string ApPoll = "AP Top 25";

    // Preferred sportsbooks for the spread, in order; otherwise the first line that has one.
    private static readonly string[] PreferredLineProviders = ["consensus", "DraftKings", "ESPN Bet", "Bovada"];

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ProviderName => Name;

    public async Task<IReadOnlyList<ScheduledGame>> GetSeasonScheduleAsync(int season, CancellationToken cancellationToken)
    {
        var games = await GetAsync<CfbdGame>(Invariant($"games?year={season}&seasonType=both&classification=fbs"), cancellationToken);

        return games
            .Select(game => (game, week: ToWeekKey(game)))
            .Where(x => x.week is not null)
            .Select(x => new ScheduledGame(
                ProviderGameId(x.game.Id),
                x.game.Season,
                x.week!.Value,
                x.game.StartDate.UtcDateTime,
                x.game.StartTimeTbd,
                x.game.HomeTeam,
                x.game.AwayTeam))
            .ToList();
    }

    public async Task<IReadOnlyList<GameResult>> GetWeekResultsAsync(int season, WeekKey week, CancellationToken cancellationToken)
    {
        var games = await GetWeekGamesAsync(season, week, cancellationToken);

        return games
            .Select(game => new GameResult(
                ProviderGameId(game.Id),
                game.Completed,
                game.HomePoints,
                game.AwayPoints,
                WentToOvertime(game),
                game.HomePregameElo,
                game.AwayPregameElo,
                game.ExcitementIndex))
            .ToList();
    }

    public async Task<IReadOnlyList<GameReference>> GetWeekReferenceDataAsync(int season, WeekKey week, CancellationToken cancellationToken)
    {
        // Rankings are per team, so the week's games are needed to attach them to games without lines.
        var gamesTask = GetWeekGamesAsync(season, week, cancellationToken);
        var linesTask = GetAsync<CfbdBettingGame>(Invariant($"lines?year={season}&{WeekQuery(week)}"), cancellationToken);
        var rankingsTask = GetAsync<CfbdPollWeek>(RankingsPath(season, week), cancellationToken);
        await Task.WhenAll(gamesTask, linesTask, rankingsTask);

        var spreads = (await linesTask)
            .GroupBy(game => game.Id)
            .ToDictionary(group => group.Key, group => HomeSpread(group.SelectMany(game => game.Lines)));
        var ranks = PollRanks(await rankingsTask, week);

        return (await gamesTask)
            .Select(game => new GameReference(
                ProviderGameId(game.Id),
                spreads.GetValueOrDefault(game.Id),
                RankOf(ranks, game.HomeId, game.HomeTeam),
                RankOf(ranks, game.AwayId, game.AwayTeam)))
            .ToList();
    }

    private Task<IReadOnlyList<CfbdGame>> GetWeekGamesAsync(int season, WeekKey week, CancellationToken cancellationToken) =>
        GetAsync<CfbdGame>(Invariant($"games?year={season}&{WeekQuery(week)}&classification=fbs"), cancellationToken);

    private static string WeekQuery(WeekKey week) =>
        week.IsPostseason ? "seasonType=postseason" : Invariant($"seasonType=regular&week={week.RegularWeek}");

    // Postseason games are rated against the last regular-season poll, not the final poll published after the bowls.
    private static string RankingsPath(int season, WeekKey week) =>
        week.IsPostseason
            ? Invariant($"rankings?year={season}&seasonType=regular")
            : Invariant($"rankings?year={season}&seasonType=regular&week={week.RegularWeek}");

    private static WeekKey? ToWeekKey(CfbdGame game) => game.SeasonType switch
    {
        "regular" when game.Week >= 0 => WeekKey.Regular(game.Week),
        "postseason" => WeekKey.Postseason,
        _ => null,
    };

    private static bool WentToOvertime(CfbdGame game) =>
        Math.Max(game.HomeLineScores?.Length ?? 0, game.AwayLineScores?.Length ?? 0) > 4;

    private static double? HomeSpread(IEnumerable<CfbdGameLine> lines)
    {
        var withSpread = lines.Where(line => line.Spread is not null).ToList();
        return PreferredLineProviders
            .Select(provider => withSpread.FirstOrDefault(line => string.Equals(line.Provider, provider, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(line => line is not null)?.Spread
            ?? withSpread.FirstOrDefault()?.Spread;
    }

    /// <summary>Ranks from the CFP committee poll for the week when it exists, otherwise the AP Top 25.</summary>
    private static CfbdPollRank[] PollRanks(IReadOnlyList<CfbdPollWeek> pollWeeks, WeekKey week)
    {
        var pollWeek = week.IsPostseason
            ? pollWeeks.MaxBy(p => p.Week)
            : pollWeeks.FirstOrDefault(p => p.Week == week.RegularWeek);
        var polls = pollWeek?.Polls ?? [];

        var poll = polls.FirstOrDefault(p => string.Equals(p.Poll, CfpPoll, StringComparison.OrdinalIgnoreCase))
            ?? polls.FirstOrDefault(p => string.Equals(p.Poll, ApPoll, StringComparison.OrdinalIgnoreCase));
        return poll?.Ranks ?? [];
    }

    private static int? RankOf(CfbdPollRank[] ranks, int teamId, string school) =>
        (ranks.FirstOrDefault(rank => rank.TeamId == teamId)
            ?? ranks.FirstOrDefault(rank => string.Equals(rank.School, school, StringComparison.Ordinal)))?.Rank;

    private static string ProviderGameId(int id) => id.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(FormattableString value) => FormattableString.Invariant(value);

    private async Task<IReadOnlyList<T>> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        LogCalling(path);
        try
        {
            using var response = await httpClient.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                throw new DataSourceUnavailableException(
                    Invariant($"CFBD returned {(int)response.StatusCode} for {path}."));
            }

            return await response.Content.ReadFromJsonAsync<T[]>(JsonOptions, cancellationToken)
                ?? throw new DataSourceUnavailableException(Invariant($"CFBD returned an empty body for {path}."));
        }
        catch (HttpRequestException ex)
        {
            throw new DataSourceUnavailableException(Invariant($"CFBD request failed for {path}."), ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new DataSourceUnavailableException(Invariant($"CFBD request timed out for {path}."), ex);
        }
        catch (JsonException ex)
        {
            throw new DataSourceUnavailableException(Invariant($"CFBD returned unreadable JSON for {path}."), ex);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Calling CFBD {Path}")]
    private partial void LogCalling(string path);
}
