# Proposal

## Why

The site is still a scaffold: one page showing a greeting from the database. Its purpose is to tell people which completed games are worth watching "as live" without spoiling them, and college football is the first sport, because CollegeFootballData.com (CFBD) provides free data with everything needed to judge how watchable a game was.

## What Changes

- Add site-wide navigation with a single "College Football" item, and client-side routing so each section has its own URL.
- Add a College Football page where the user picks a week of the current season (regular-season weeks plus postseason) and sees every FBS game that week, ordered by kickoff, each labelled **Must watch**, **Worth it** or **Skip**, with no reasons shown. Games that haven't finished show "Not finished yet". The page opens on the most recent week with completed games, and the selected week is in the URL so it can be bookmarked and shared.
- Rate games on the server from final-state signals: final margin, upset against the spread (pregame Elo as a fallback when no line exists), rankings, a curated rivalry list, and CFBD's excitement index. Scores, margins and other result data never leave the server.
- Fetch CFBD data on demand and cache it in SQL. Completed games are never fetched again. A week is only fetched when it has a game that kicked off at least 3 hours ago and isn't final in the cache. One shared 15-minute throttle per week applies however many people are visiting.
- Show a friendly "waking up" state while the scaled-to-zero API and auto-paused database start, and a neutral message (no spoilers) when data can't be refreshed.
- The home page stops showing the database greeting and points visitors to College Football.

## Capabilities

### New Capabilities

- `site-navigation`: the site-wide navigation menu and routing between sport sections.
- `college-football-watchability`: viewing spoiler-free watchability tiers for a week's FBS games, including week selection, how tiers are derived, spoiler protection, and how and when game data is refreshed from the third-party provider.

### Modified Capabilities

None. No specs exist yet.

## Impact

- **Web (`src/web`)**: adds a routing dependency (`react-router`), a navigation layout, a College Football page and its tests. `App.tsx` no longer fetches `/api/greeting`.
- **API (`src/ShouldIWatch.Api`)**: new endpoints under `/api/college-football/...`, a CFBD HTTP client, a sync service, the scoring logic, and repositories. HTTP caching headers on watchability responses.
- **Database (`src/ShouldIWatch.Database`)**: new DbUp migration scripts for teams/games, week sync state and a seeded rivalry table.
- **AppHost**: a new secret parameter for the CFBD API key, passed to the API.
- **CI/CD (`.github/workflows/azure-dev.yml`)**: the deploy workflow passes the `AZURE_CFBD_API_KEY` repository secret to `azd`, so the new secret parameter is available to `azd provision --no-prompt`.
- **Tests (`tests/ShouldIWatch.Tests`)**: unit tests for scoring and fetch rules; the AppHost integration test must still start without contacting CFBD.
- **External dependency**: the CollegeFootballData.com API (free tier, bearer-token API key, monthly call limit).
