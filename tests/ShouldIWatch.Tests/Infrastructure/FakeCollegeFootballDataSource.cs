using ShouldIWatch.Api.CollegeFootball.Sources;

namespace ShouldIWatch.Tests.Infrastructure;

internal sealed record DataSourceCall(string Method, int Season, WeekKey? Week = null);

/// <summary>
/// Scripted <see cref="ICollegeFootballDataSource"/> that records every call. Set <see cref="Unavailable"/> to make
/// every call fail with <see cref="DataSourceUnavailableException"/>.
/// </summary>
internal sealed class FakeCollegeFootballDataSource(string providerName) : ICollegeFootballDataSource
{
    public const string ScheduleCall = nameof(GetSeasonScheduleAsync);
    public const string ResultsCall = nameof(GetWeekResultsAsync);
    public const string ReferenceCall = nameof(GetWeekReferenceDataAsync);

    private readonly Lock gate = new();
    private readonly List<DataSourceCall> calls = [];

    public string ProviderName => providerName;

    public List<ScheduledGame> Schedule { get; } = [];

    public Dictionary<WeekKey, List<GameResult>> Results { get; } = [];

    public Dictionary<WeekKey, List<GameReference>> References { get; } = [];

    public bool Unavailable { get; set; }

    public IReadOnlyList<DataSourceCall> Calls
    {
        get
        {
            lock (gate)
            {
                return [.. calls];
            }
        }
    }

    public void ClearCalls()
    {
        lock (gate)
        {
            calls.Clear();
        }
    }

    public Task<IReadOnlyList<ScheduledGame>> GetSeasonScheduleAsync(int season, CancellationToken cancellationToken) =>
        RespondAsync(new DataSourceCall(ScheduleCall, season), () => Schedule.Where(game => game.Season == season).ToList());

    public Task<IReadOnlyList<GameResult>> GetWeekResultsAsync(int season, WeekKey week, CancellationToken cancellationToken) =>
        RespondAsync(new DataSourceCall(ResultsCall, season, week), () => Results.GetValueOrDefault(week) ?? []);

    public Task<IReadOnlyList<GameReference>> GetWeekReferenceDataAsync(int season, WeekKey week, CancellationToken cancellationToken) =>
        RespondAsync(new DataSourceCall(ReferenceCall, season, week), () => References.GetValueOrDefault(week) ?? []);

    private async Task<IReadOnlyList<T>> RespondAsync<T>(DataSourceCall call, Func<List<T>> response)
    {
        lock (gate)
        {
            calls.Add(call);
        }

        await Task.Yield();
        return Unavailable
            ? throw new DataSourceUnavailableException("Fake data source is unavailable.")
            : response();
    }
}
