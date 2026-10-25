using ShouldIWatch.Api.CollegeFootball.Sources;
using ShouldIWatch.Tests.Infrastructure;

namespace ShouldIWatch.Tests.CollegeFootball;

public sealed class FakeCollegeFootballDataSourceTests
{
    [Fact]
    public async Task ReturnsScriptedDataAndRecordsEveryCall()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var week = WeekKey.Regular(3);
        var fake = new FakeCollegeFootballDataSource("fake");
        fake.Schedule.Add(new ScheduledGame("g1", 2030, week, new DateTime(2030, 9, 14, 16, 0, 0, DateTimeKind.Utc), false, "Home", "Away"));
        fake.Schedule.Add(new ScheduledGame("g2", 2031, week, new DateTime(2031, 9, 14, 16, 0, 0, DateTimeKind.Utc), false, "Home", "Away"));
        fake.Results[week] = [new GameResult("g1", true, 21, 20, false)];

        var schedule = await fake.GetSeasonScheduleAsync(2030, cancellationToken);
        var results = await fake.GetWeekResultsAsync(2030, week, cancellationToken);
        var references = await fake.GetWeekReferenceDataAsync(2030, WeekKey.Postseason, cancellationToken);

        Assert.Equal("g1", Assert.Single(schedule).ProviderGameId);
        Assert.Equal(21, Assert.Single(results).HomePoints);
        Assert.Empty(references);
        Assert.Equal(
            [
                new DataSourceCall(FakeCollegeFootballDataSource.ScheduleCall, 2030),
                new DataSourceCall(FakeCollegeFootballDataSource.ResultsCall, 2030, week),
                new DataSourceCall(FakeCollegeFootballDataSource.ReferenceCall, 2030, WeekKey.Postseason),
            ],
            fake.Calls);
    }

    [Fact]
    public async Task ThrowsDataSourceUnavailableWhenUnavailableAndStillRecordsTheCall()
    {
        var fake = new FakeCollegeFootballDataSource("fake") { Unavailable = true };

        await Assert.ThrowsAsync<DataSourceUnavailableException>(
            () => fake.GetWeekResultsAsync(2030, WeekKey.Regular(1), TestContext.Current.CancellationToken));

        Assert.Single(fake.Calls);
    }
}
