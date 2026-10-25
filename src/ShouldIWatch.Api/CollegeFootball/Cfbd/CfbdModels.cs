namespace ShouldIWatch.Api.CollegeFootball.Cfbd;

// Only the CFBD response fields CfbdDataSource uses. These never leave the Cfbd namespace.

internal sealed record CfbdGame(
    int Id,
    int Season,
    int Week,
    string SeasonType,
    DateTimeOffset StartDate,
    bool StartTimeTbd,
    bool Completed,
    int HomeId,
    string HomeTeam,
    int? HomePoints,
    double[]? HomeLineScores,
    int? HomePregameElo,
    int AwayId,
    string AwayTeam,
    int? AwayPoints,
    double[]? AwayLineScores,
    int? AwayPregameElo,
    double? ExcitementIndex);

internal sealed record CfbdPollWeek(int Season, string SeasonType, int Week, CfbdPoll[] Polls);

internal sealed record CfbdPoll(string Poll, CfbdPollRank[] Ranks);

internal sealed record CfbdPollRank(int? Rank, int TeamId, string School);

internal sealed record CfbdBettingGame(int Id, CfbdGameLine[] Lines);

internal sealed record CfbdGameLine(string Provider, double? Spread);
