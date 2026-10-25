# Tasks

## 1. Configuration and secrets

- [x] 1.1 In `AppHost.cs`, add the `cfbd-api-key` secret parameter and pass it to the API as `Cfbd__ApiKey`; verify `dotnet build ShouldIWatch.slnx` succeeds and the Aspire dashboard shows the parameter
- [x] 1.2 Bind a `CfbdOptions` (`BaseUrl` defaulting to `https://api.collegefootballdata.com`, `ApiKey`) in the API with validation on start; verify the API fails fast with a clear message when the key is missing
- [x] 1.3 Update `ApiIntegrationTests` to supply a dummy `cfbd-api-key` and an unreachable `Cfbd__BaseUrl`; verify `dotnet test --solution ShouldIWatch.slnx` still passes the greeting test
- [x] 1.4 Add a README note on setting `Parameters:cfbd-api-key` with `dotnet user-secrets` for the AppHost; verify the documented command works
- [x] 1.5 Pass `AZURE_CFBD_API_KEY: ${{ secrets.AZURE_CFBD_API_KEY }}` to azd in `.github/workflows/azure-dev.yml` (replacing the wrong `AZD_INITIAL_ENVIRONMENT_CONFIG` line), and correct the README's deploy section to describe the per-parameter secret; verify the workflow YAML parses
- [x] 1.6 (manual) Run `azd provision` locally to save `cfbd-api-key` in the `siwal-prod` azd environment, then `azd pipeline config --provider github`; verify the `AZURE_CFBD_API_KEY` repository secret exists before pushing

## 2. Database schema

- [x] 2.1 Add DbUp scripts for `cfb_season_sync`, `cfb_week_sync` and `cfb_game` with its internal identity key and `UNIQUE (provider, provider_game_id)` (design decision 4); verify `DatabaseMigratorTests` and a local AppHost run apply them cleanly
- [x] 2.2 Add a script creating `cfb_rivalry` and seeding about 25 rivalries using CFBD school names in alphabetical pair order; verify with a test that every seeded name appears in a recorded CFBD `/games` sample fixture

## 3. Scoring

- [x] 3.1 Implement `GameFacts`, `Tier` and the pure `WatchabilityScorer` with the weights and bands from design decision 5; verify with xUnit tests for each band edge (scores 1/2 and 4/5)
- [x] 3.2 Add scorer tests mirroring the spec scenarios (close game between ranked teams is Must watch, unremarkable blowout is Skip, upset never lowers the tier, missing excitement index still rates, Elo fallback when there is no spread); verify all pass

## 4. Data source boundary

- [x] 4.1 Define `ICollegeFootballDataSource`, `WeekKey`, `ScheduledGame`, `GameResult` (with nullable Elo and excitement index), `GameReference` and `DataSourceUnavailableException` (design decision 7), with no provider-specific types; verify the API project builds and nothing in these types references CFBD
- [x] 4.2 Add a reusable `FakeCollegeFootballDataSource` to the test project that returns scripted data, records every call and can be set to throw `DataSourceUnavailableException`; verify with a small self-test that the calls are recorded

## 5. CFBD data source

- [x] 5.1 Implement `CfbdDataSource : ICollegeFootballDataSource` as a typed `HttpClient` for `/games` (season or week), `/rankings` and `/lines`, sending the bearer key, with standard-resilience retries turned off, and register it with `AddHttpClient<ICollegeFootballDataSource, CfbdDataSource>()`; verify with tests using a stub `HttpMessageHandler` and recorded JSON fixtures that the request URLs, `WeekKey` mapping (regular week and postseason) and auth header are correct
- [x] 5.2 Map CFBD responses to the app's types inside `CfbdDataSource`: derive went-to-overtime from line-score length, home spread from lines (joined on game id), and ranks from the CFP poll falling back to AP; turn HTTP errors and timeouts into `DataSourceUnavailableException`; verify with fixture-based tests, including missing `excitementIndex`, no lines, a TBD kickoff and a 429 response

## 6. Sync service and repositories

- [x] 6.1 Implement Dapper repositories for games, rivalries and sync state, including the atomic claim `UPDATE`s, `MERGE ... HOLDLOCK` row creation and game upserts keyed on `(provider, provider_game_id)`; verify with integration tests against the AppHost SQL Server that only one of two concurrent claims succeeds and that re-upserting the same provider game updates rather than duplicates it
- [x] 6.2 Implement `CollegeFootballSyncService.EnsureSeasonScheduleAsync` (1-day claim, only while the season has unfinished games) and `EnsureWeekAsync` (due rule, 15-minute claim, reference data once per week, parallel calls, `DataSourceUnavailableException` reported rather than thrown), depending only on `ICollegeFootballDataSource` and `TimeProvider`; verify with unit tests using `FakeTimeProvider` and `FakeCollegeFootballDataSource` (asserting on its recorded calls) covering every scenario in the spec requirements *No provider calls when no game can have finished*, *Shared refresh throttle*, *Season schedule and weekly reference data* and *Graceful provider failure*
- [x] 6.3 Implement current-season and week-key resolution (August cutover, `postseason` key, default week = most recent week with a completed game, else the first week); verify with unit tests for dates in January, July, August and mid-season
- [x] 6.4 Count a week as having a completed game when a cached game is final or kicked off 3+ hours ago, so the default week is right before that week's results are fetched; verify with repository and endpoint tests

## 7. Watchability API

- [x] 7.1 Add `GET /api/college-football/weeks` and `GET /api/college-football/weeks/{key}` returning the spoiler-safe DTOs from design decision 6, using the internal game `id` only (404 for an unknown key); verify with endpoint tests
- [x] 7.2 Add a test that serializes a week of completed games and checks the exact set of JSON property names (no points, margin, spread, rank, Elo, overtime, excitement or provider ids); verify it fails if a result field is added
- [x] 7.3 Set `Cache-Control` (1 day when every game is final, otherwise 60s) and `updateFailed`; verify with endpoint tests for both cases and a failed data source (using `FakeCollegeFootballDataSource`)
- [x] 7.4 Add requests for the new endpoints to `ShouldIWatch.Api.http`; verify they return data when run against a local AppHost with a real key

## 8. Web: routing and navigation

- [x] 8.1 Add `react-router` and restructure `App.tsx` into `BrowserRouter` + `Layout` (`<nav>` with a `NavLink` to College Football) + routes for `/`, `/college-football` and `*`; remove the greeting fetch; verify `npm run build` and `npm run lint` pass
- [x] 8.2 Implement `HomePage` (site description and link) and `NotFoundPage` (message with nav); verify with Vitest tests that the menu renders on every route, `aria-current="page"` is on College Football only on that route, the home page makes no fetch, and unknown paths show not found
- [x] 8.3 Replace the existing `App.test.tsx` greeting tests with the new routing tests; verify `npm test` passes

## 9. Web: College Football page

- [x] 9.1 Implement `CollegeFootballPage`: load weeks, a week selector synced to `?week=`, the default and invalid-week fallback, the game list in API order with local kickoff times, the tier or "Not finished yet", and no reasons; verify with Vitest tests using stubbed `fetch` for each *Weekly game list*, *Three watchability tiers without reasons*, *Unfinished games are not rated* and *Week selection* scenario
- [x] 9.2 Add the waking-up message after 3s, a 90s timeout with a retry button, the neutral "couldn't update" notice when `updateFailed`, and the "games unavailable" message; verify with Vitest fake-timer tests
- [x] 9.3 Set `document.title` to the sport and week only; verify with a test that the title never contains team names or tiers

## 10. End-to-end check

- [x] 10.1 Run the AppHost locally with a real CFBD key, open `/college-football` for a completed week, and confirm through the browser network panel that the API response has no scores and that a reload within 15 minutes makes no new CFBD call (from the API logs)
- [x] 10.2 Run `dotnet test --solution ShouldIWatch.slnx` and `npm test`, `npm run lint` and `npm run build` in `src/web`; verify everything passes, as CI will

## Workflow follow-up

- Check the current CFBD free-tier limits at collegefootballdata.com/api-tiers before deploying.
- Ask before committing, then archive the change with `/opsx:archive` once it's reviewed.
