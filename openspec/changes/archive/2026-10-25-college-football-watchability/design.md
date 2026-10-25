# Design

## Context

See proposal.md for motivation and the two spec deltas for required behavior.

Current state that shapes the approach:

- **API**: a .NET 10 minimal API (`src/ShouldIWatch.Api/Program.cs`). It uses Dapper repositories over an Aspire-registered `SqlConnection` (pattern: `Data/GreetingRepository.cs`), runs DbUp migrations from `src/ShouldIWatch.Database/Scripts/NNNN_*.sql` at startup, and already serves the React build with `MapFallbackToFile("index.html")`, so deep links work once deployed.
- **Hosting**: the API container scales to zero and Azure SQL (free offer) auto-pauses (`src/ShouldIWatch.AppHost/AppHost.cs`). Nothing runs in the background, and the first request after idle is slow.
- **Web**: React 19 + Vite, with a single `App.tsx`, no router, and Vitest + Testing Library tests that stub `fetch`.
- **Tests**: `tests/ShouldIWatch.Tests` uses xUnit v3. `ApiIntegrationTests` starts the whole AppHost (Docker SQL Server) and calls `/api/greeting`.
- **Packages**: central package management (`Directory.Packages.props`). ServiceDefaults adds the standard HTTP resilience handler to all `HttpClient`s.
- **CFBD**: `GET /games` returns `completed`, `startDate`, `startTimeTBD`, points, `homeLineScores`/`awayLineScores`, pregame Elo and a nullable `excitementIndex`, but no rankings or lines. Those come from `GET /rankings` and `GET /lines`. Authentication is a bearer token.

## Goals / Non-Goals

**Goals:**
- One fetch-and-store service that the API calls on demand now and a scheduled job could call later without changes.
- The data provider can be replaced by writing one new implementation of a single interface, with no changes to sync, scoring, storage or the API.
- A pure, unit-tested scoring function that is easy to tune in later changes.
- Spoiler safety enforced by the API's response type, not by the UI hiding data.

**Non-Goals:**
- A scheduled or background pre-fetch job.
- Play-by-play or drive analysis.
- Removing `/api/greeting` and its integration test. The web app stops calling it; removal can be a cleanup change.
- Styling beyond a basic usable layout. That's a separate visual-design change.
- Past seasons, other sports, accounts, and revealing reasons.

## Decisions

### 1. On-demand sync behind `CollegeFootballSyncService`
On each watchability request the endpoint calls `CollegeFootballSyncService.EnsureSeasonScheduleAsync(season)` and then `EnsureWeekAsync(season, weekKey)`, and afterwards reads only from SQL. The service talks to the provider only through `ICollegeFootballDataSource` (decision 7). Each `Ensure*` decides from cached data and the current time (`TimeProvider`) whether a provider call is due (spec: *No provider calls when no game can have finished*, *Shared refresh throttle*, *Season schedule and weekly reference data*).
- *Alternative*: a scheduled Container Apps Job. Rejected for now. It makes provider calls and wakes the database even when nobody visits, and adds a resource to deploy. Because the sync logic lives in a service, a job can be added later as a second caller.

### 2. Throttles as atomic claims in SQL
Sync state tables hold `last_*_fetch_at` timestamps. Before fetching, a request *claims* the slot with one conditional statement, for example:

```sql
UPDATE dbo.cfb_week_sync
SET results_fetched_at = @now
WHERE season = @season AND season_type = @seasonType AND week = @week
  AND (results_fetched_at IS NULL OR results_fetched_at < DATEADD(minute, -15, @now));
```

Only the request that updates a row fetches; every other request reads the cache. This works across multiple API replicas without an extra lock service. The claim is taken before the call, so a failed fetch still waits 15 minutes (spec: *Failure does not trigger rapid retries*). Season rows use the same pattern with a 1-day window. Missing rows are created with `MERGE ... WITH (HOLDLOCK)`, so two first requests can't both insert.
- *Alternative*: an in-process `SemaphoreSlim` per week. Rejected because it isn't shared across replicas or restarts.

### 3. "Due" rule
A week needs a results fetch when it has a cached game where `completed = 0 AND start_date <= now - 3h`. Games with `start_time_tbd` use their date at 00:00 UTC, so they count as due conservatively once the date has passed. Rankings and lines are fetched once per week, the first time the week is due, and recorded in `reference_fetched_at`. The data-source calls for a first fetch (results plus reference data) run in parallel to keep the cold-start delay down.

### 4. Data model (new DbUp scripts)
- `cfb_season_sync(season PK, schedule_fetched_at)`
- `cfb_week_sync(season, season_type, week PK, results_fetched_at, reference_fetched_at)`
- `cfb_game(id int IDENTITY PK, provider, provider_game_id, season, season_type, week, start_date datetime2 UTC, start_time_tbd, home_team, away_team, completed, home_points NULL, away_points NULL, went_to_overtime, home_pregame_elo NULL, away_pregame_elo NULL, excitement_index NULL, home_spread NULL, home_rank NULL, away_rank NULL, updated_at)` with `UNIQUE (provider, provider_game_id)`. The game key is **provider-neutral**: `provider` holds the data source's name (for example `cfbd`) and `provider_game_id` holds its game id as a string. Upserts match on that pair. The API exposes only the internal `id`, so client URLs and responses never depend on the provider.
- `cfb_rivalry(team_a, team_b, name)`, stored with team names in alphabetical order and seeded with about 25 well-known rivalries

Games are upserted, so overlapping fetches are harmless. `went_to_overtime` is supplied by the data source (the CFBD implementation derives it from the length of the line-score arrays, more than 4 periods), and line scores are never stored. Tiers are **computed when read, not stored**, so tuning the scoring re-rates every cached game without calling CFBD again.

**Week keys**: a provider-neutral `WeekKey` is either a regular-season week number or "postseason" (all postseason games). The CFBD implementation maps these to `seasonType=regular&week=N` and `seasonType=postseason`. The **current season** is the calendar year from August onward and the previous year from January to July.

### 5. Scoring: pure function with points then buckets
`WatchabilityScorer.Score(GameFacts) -> Tier` has no I/O. Initial weights:

| Signal | Points |
|---|---|
| Overtime, or margin ≤ 3 | +3 |
| Margin ≤ 8 | +2 |
| Margin ≤ 16 | +1 |
| Margin > 24 | −2 |
| Underdog won (spread; else pregame Elo gap ≥ 100) | +2 |
| Both teams ranked / one ranked | +2 / +1 |
| Rivalry | +1 |
| Excitement index ≥ 6 / ≥ 4 / ≥ 2.5 / missing | +3 / +2 / +1 / 0 |

Tiers: **≥ 5 → Must watch**, **2–4 → Worth it**, **≤ 1 → Skip**. These values satisfy the spec scenarios: a close game between ranked teams scores ≥ 5, an unremarkable blowout scores ≤ −2, and an upset only adds points. The weights are constants in one class, and the tests cover each band edge so later tuning is deliberate.

**Rankings**: the data source supplies one rank per team per game. The CFBD implementation uses the College Football Playoff committee poll for that week when it exists, otherwise the AP Top 25.

### 6. The API response type is the spoiler boundary
Endpoints:
- `GET /api/college-football/weeks` returns `{ season, defaultWeek, weeks: [{ key, label }] }`
- `GET /api/college-football/weeks/{key}` returns `{ season, week, updateFailed, games: [{ id, homeTeam, awayTeam, kickoff, startTimeTbd, status: "final" | "notFinished", tier? }] }`

`defaultWeek` is the most recent week with a game that is final in the cache or kicked off at least 3 hours ago (decision 3's due rule), so past weeks count before anyone has opened them.

These DTOs are the only types serialized. They have no fields for points, spread, rank, Elo, overtime or excitement. A test serializes a response for a week of completed games and checks the exact set of JSON property names. `Cache-Control: public, max-age=86400` is set when every game is final, otherwise `max-age=60`. An invalid key returns 404, and the page then falls back to the default week.

### 7. Provider boundary: `ICollegeFootballDataSource`, implemented by `CfbdDataSource`
The sync service depends on one interface that returns the app's own types, never provider JSON:

```csharp
interface ICollegeFootballDataSource
{
    string ProviderName { get; } // stamped into cfb_game.provider, e.g. "cfbd"
    Task<IReadOnlyList<ScheduledGame>> GetSeasonScheduleAsync(int season, CancellationToken ct);
    Task<IReadOnlyList<GameResult>> GetWeekResultsAsync(int season, WeekKey week, CancellationToken ct);
    Task<IReadOnlyList<GameReference>> GetWeekReferenceDataAsync(int season, WeekKey week, CancellationToken ct);
}
```

- `ScheduledGame`: provider game id, season, `WeekKey`, kickoff (UTC), start-time-TBD flag, home team, away team.
- `GameResult`: provider game id, completed, home/away points (null until known), went-to-overtime, and the **optional** signals pregame Elo (home/away) and excitement index.
- `GameReference`: provider game id, home spread (optional), home rank and away rank (optional).

Optional signals are nullable because they belong to one provider (excitement index and Elo are CFBD-specific). The scorer already rates games without them (decision 5). Every provider-specific decision, including how overtime is detected, which poll gives the rank, and how lines map to a home spread, lives inside the implementation.

**`CfbdDataSource`** is the only implementation in this change. It's a typed `HttpClient` with its base address and API key from configuration (`Cfbd:BaseUrl`, `Cfbd:ApiKey`), and it sends the key as `Authorization: Bearer`. It deserializes only the fields it uses into private CFBD DTOs and maps them to the types above, joining lines on CFBD game id and ranks on team. Retries from the standard resilience handler are **turned off for this client**, so one claimed fetch stays one set of calls. Failures throw a single `DataSourceUnavailableException`, which the sync service logs and reports as `updateFailed: true`. It's registered as `services.AddHttpClient<ICollegeFootballDataSource, CfbdDataSource>()`, so swapping providers is a one-line registration change.

- *Alternative*: the sync service calls a concrete CFBD client and maps CFBD DTOs itself. Rejected because provider details would spread into sync logic and its tests, and swapping providers would mean rewriting the sync service.
- *Alternative*: interfaces on the repositories and scorer as well. Rejected because the scorer is a pure function tested directly, and the repositories are best tested against real SQL. Only the external boundary needs a seam.

### 8. Secret wiring
The AppHost declares `builder.AddParameter("cfbd-api-key", secret: true)` and passes it to the API as `Cfbd__ApiKey`. Locally the value comes from AppHost user-secrets (`Parameters:cfbd-api-key`). In Azure the value is a Container Apps secret. Run interactively, `azd provision` prompts for the parameter and saves it as a secret in the azd environment. The GitHub deploy workflow runs `azd provision --no-prompt` and cannot prompt, so `azd pipeline config --provider github` publishes each saved secret parameter as its own repository secret, named `AZURE_` plus the parameter name in upper snake case (`AZURE_CFBD_API_KEY`), and `azure-dev.yml` passes it to `azd` as an environment variable of the same name, which azd maps back to the `cfbd_api_key` parameter. Without it, the first push after this change fails at provisioning. The integration test supplies a dummy value and an unreachable `Cfbd__BaseUrl`, so it never contacts CFBD.

### 9. Web structure
- `react-router` (library mode, `BrowserRouter`) with a `Layout` that renders the `<nav>` and an `<Outlet>`. `NavLink` gives `aria-current="page"` (spec: *Current section is indicated*).
- Routes: `/` → `HomePage`, `/college-football` → `CollegeFootballPage`, `*` → `NotFoundPage`.
- `CollegeFootballPage` reads `week` from `useSearchParams`, loads `/weeks` and then `/weeks/{key}`, and formats kickoff with `Intl.DateTimeFormat` in local time. It shows the "waking up" message after 3s and gives up with a retry button after 90s (`AbortController` plus timers), and sets `document.title` to the sport and week only.
- Vite's dev server already falls back to `index.html`, so deep links work locally as well as in production.

## Risks / Trade-offs

- **[Risk] CFBD's free-tier monthly limit is unknown or changes.** → The worst case is about 50 calls per busy Saturday plus one schedule call per day. Check the limit on collegefootballdata.com/api-tiers before deploying, and log every outbound call so usage can be seen.
- **[Risk] The first visit after idle is slow** (container start, database resume, and possibly a fetch). → The waking-up state, parallel CFBD calls, and caching of completed weeks.
- **[Risk] CFBD marks games completed late, or games run past 3h** (weather delays). → The game stays due and is picked up by a later fetch after the throttle.
- **[Risk] Team names differ between `/games`, `/rankings`, `/lines` and the rivalry seed.** → Inside `CfbdDataSource`, join on CFBD game id for lines, and on team id for rankings where available. Rivalry seeds use CFBD's exact school names, checked by a test against a recorded `/games` sample.
- **[Risk] A future provider names teams differently**, so rivalry matching breaks after a swap. → Accepted for now. A swap would add a team-name mapping or re-seed rivalries as part of that change. Cached games are keyed by provider, so a new provider starts with a fresh cache rather than mixing ids.
- **[Trade-off] The tier itself hints at the result** (a "Skip" means a blowout). → Accepted, since that's the point of the product. Reasons are never shown and the list is in kickoff order.
- **[Trade-off] Heuristic weights are guesses.** → They're isolated in one pure function, with thresholds covered by tests, and tuning is a later change.
- **[Risk] The 15-minute claim is taken before the call, so a transient failure delays fresh data by 15 minutes.** → Accepted; protecting the call budget matters more.

## Migration Plan

1. DbUp applies the new scripts automatically on API startup. They only add tables, so no existing data changes.
2. Before running locally, set the AppHost secret `Parameters:cfbd-api-key`. Before the first push to `main` after this change, run `azd provision` once locally to save the key in the azd environment, then re-run `azd pipeline config --provider github` so the deploy workflow receives it.
3. Rollback: redeploy the previous image. The new tables are ignored by the old code and can stay.

## Open Questions

- Exact weights and band edges will need tuning against real weeks. This can be done in a follow-up change without altering the specs.
- The final rivalry seed list can be extended at any time with a new migration script.
