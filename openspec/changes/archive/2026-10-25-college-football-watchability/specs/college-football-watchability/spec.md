# Spec Delta

## Purpose

Tells visitors which completed FBS college football games are worth watching "as live", as a spoiler-free three-tier rating per game, organized by season week, while keeping calls to the third-party data provider to a minimum.

## ADDED Requirements

### Requirement: Weekly game list
The College Football page SHALL list every FBS game in the selected week of the current season, ordered by scheduled kickoff time (earliest first). Each game SHALL show both team names and the kickoff date and time in the visitor's local time zone.

#### Scenario: Games listed in kickoff order
- **WHEN** a visitor views a week with games kicking off at 12:00, 15:30 and 19:00
- **THEN** the games are listed in that order, each showing its two teams and kickoff time

#### Scenario: Order does not depend on rating
- **WHEN** a later-kicking-off game has a higher tier than an earlier one
- **THEN** the earlier game is still listed first

#### Scenario: Week with no games
- **WHEN** the selected week has no FBS games
- **THEN** the page says there are no games that week

### Requirement: Three watchability tiers without reasons
Each completed game SHALL show exactly one of three tiers: "Must watch", "Worth it" or "Skip". The page SHALL NOT show why a game received its tier.

#### Scenario: Completed game shows a tier
- **WHEN** a game in the selected week is final
- **THEN** it shows exactly one of "Must watch", "Worth it" or "Skip"

#### Scenario: No reasons shown
- **WHEN** any game shows a tier
- **THEN** no text, icon, tooltip or label explains the tier (for example "upset", "overtime", "comeback" or "close game")

### Requirement: Unfinished games are not rated
A game that is not final SHALL show "Not finished yet" in place of a tier, whether it has not started or is in progress.

#### Scenario: Future game
- **WHEN** a game's kickoff is in the future
- **THEN** it shows "Not finished yet"

#### Scenario: Game in progress
- **WHEN** a game has kicked off but is not final
- **THEN** it shows "Not finished yet", with no indication of how long it has been running

### Requirement: Results never reach the browser
API responses for watchability SHALL contain only game identity, teams, kickoff time and tier (or not-finished status). They SHALL NOT contain scores, margins, winners, line or quarter scores, betting lines, rankings movement, excitement values, overtime indicators or any other result data.

#### Scenario: API response has no result fields
- **WHEN** a client requests a week containing completed games
- **THEN** each game in the response has only identity, team, kickoff and tier/status fields, and no field reveals points, margin, winner or game events

#### Scenario: Neutral page title and URL
- **WHEN** a visitor views any week
- **THEN** the page title and URL contain only the sport and week, never team results or tiers

### Requirement: Tier reflects how entertaining the game was
The tier SHALL be derived on the server from final-state signals: final margin, whether the underdog won (using the betting spread, or pregame Elo when no line exists), team rankings, rivalry status and the provider's excitement index when available. Closer, more surprising, higher-stakes and more exciting games SHALL rank higher.

#### Scenario: Close game between ranked teams
- **WHEN** two ranked teams play a game decided by 3 points or fewer, or in overtime
- **THEN** the game is "Must watch"

#### Scenario: Unremarkable blowout
- **WHEN** a game between unranked non-rivals is decided by more than 24 points, the favorite wins and the excitement index is low or missing
- **THEN** the game is "Skip"

#### Scenario: Upset raises the tier
- **WHEN** two otherwise identical games differ only in that the underdog won one of them
- **THEN** the game the underdog won has a tier at least as high as the other

#### Scenario: Missing excitement index
- **WHEN** the provider has no excitement index for a completed game
- **THEN** the game is still given a tier from the remaining signals

### Requirement: Week selection
The visitor SHALL be able to choose any regular-season week of the current season, or the postseason, and the selected week SHALL be reflected in the URL (for example `/college-football?week=8` or `?week=postseason`).

#### Scenario: Default week
- **WHEN** a visitor opens `/college-football` without a week
- **THEN** the most recent week that has at least one completed game is shown, or the first week of the season if no game has completed yet

#### Scenario: Choosing a week
- **WHEN** a visitor selects week 5
- **THEN** week 5's games are shown and the URL includes `week=5`

#### Scenario: Bookmarked week
- **WHEN** a visitor opens `/college-football?week=postseason`
- **THEN** the postseason games are shown

#### Scenario: Invalid week
- **WHEN** a visitor opens a URL with a week that does not exist in the season
- **THEN** the default week is shown

### Requirement: No provider calls when no game can have finished
The system SHALL call the third-party provider for a week's results only when the cache has a game in that week that is not final and kicked off at least 3 hours ago. Games cached as final SHALL NOT be fetched again.

#### Scenario: Future week
- **WHEN** a visitor views a week where every game kicks off in the future
- **THEN** no third-party call is made for that week's results

#### Scenario: Games still in progress
- **WHEN** every non-final game in the week kicked off less than 3 hours ago
- **THEN** no third-party call is made for that week's results

#### Scenario: Fully completed week
- **WHEN** every game in the week is cached as final
- **THEN** no third-party call is made for that week, then or later

#### Scenario: Game probably finished
- **WHEN** a non-final cached game kicked off 3 or more hours ago and the week is not throttled
- **THEN** the week's results are fetched from the provider before responding

### Requirement: Shared refresh throttle
Results for a given week SHALL be fetched from the provider at most once every 15 minutes, across all visitors and all API instances.

#### Scenario: Many visitors at once
- **WHEN** many visitors request the same due week within the same 15 minutes
- **THEN** at most one results fetch for that week is made, and the other requests are answered from the cache

#### Scenario: Throttle window passes
- **WHEN** a week was fetched more than 15 minutes ago and still has a due game
- **THEN** the next request triggers a new fetch

### Requirement: Season schedule and weekly reference data
The system SHALL learn the season's games and kickoff times from the provider and refresh that schedule at most once per day while the season has unfinished games. Rankings and betting lines for a week SHALL be fetched at most once, when that week first has a due game.

#### Scenario: Schedule known without per-week calls
- **WHEN** the season schedule was fetched less than a day ago
- **THEN** viewing any week's list of games and kickoff times makes no schedule call

#### Scenario: Kickoff time changed
- **WHEN** the provider moves a game's kickoff
- **THEN** the new time is shown within a day

#### Scenario: Rankings and lines fetched once
- **WHEN** a week has already had its rankings and lines fetched
- **THEN** later refreshes of that week fetch only game results

### Requirement: Browser caching of responses
Watchability responses SHALL allow the browser to reuse them: a week where every game is final SHALL be cacheable for one day, and any other week for 60 seconds.

#### Scenario: Completed week
- **WHEN** the API returns a week where every game is final
- **THEN** the response allows caching for one day

#### Scenario: Week with unfinished games
- **WHEN** the API returns a week with any unfinished game
- **THEN** the response allows caching for no more than 60 seconds

### Requirement: Waking-up state
If watchability data takes longer than 3 seconds to arrive, the page SHALL show a friendly message that the site is waking up and keep waiting. If no data arrives within 90 seconds, it SHALL show a neutral error with a way to retry.

#### Scenario: Slow cold start
- **WHEN** the API takes 20 seconds to respond
- **THEN** a "waking up" message appears after 3 seconds and is replaced by the games when they arrive

#### Scenario: Never responds
- **WHEN** no response arrives within 90 seconds
- **THEN** the page shows a neutral error and a retry control

### Requirement: Graceful provider failure
When a provider call fails or is rate-limited, the system SHALL answer from the cache and the page SHALL show a neutral notice that results could not be updated just now. No error message SHALL reveal game results.

#### Scenario: Provider unavailable with cached data
- **WHEN** a due week cannot be fetched because the provider returns an error
- **THEN** cached games and tiers are shown, with a notice that results couldn't be updated

#### Scenario: Provider unavailable with nothing cached
- **WHEN** the provider fails and the cache has no schedule for the season
- **THEN** the page shows a neutral "games unavailable right now" message

#### Scenario: Failure does not trigger rapid retries
- **WHEN** a fetch for a week fails
- **THEN** the shared 15-minute throttle still applies before that week is fetched again

### Requirement: Provider credentials stay on the server
The provider API key SHALL be held only by the server and SHALL NOT appear in any response, page or client bundle.

#### Scenario: Key not exposed
- **WHEN** a visitor loads any page or API response
- **THEN** the provider API key does not appear in it
