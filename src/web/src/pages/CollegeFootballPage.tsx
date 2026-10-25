import { useEffect } from 'react';
import { useSearchParams } from 'react-router';
import {
  tierLabels,
  weekUrl,
  weeksUrl,
  type GameSummary,
  type WeekGamesResponse,
  type WeeksResponse,
} from '../collegeFootball/api';
import { useJson, type RequestState } from '../collegeFootball/useJson';

const kickoffFormat = new Intl.DateTimeFormat(undefined, {
  weekday: 'short',
  month: 'short',
  day: 'numeric',
  hour: 'numeric',
  minute: '2-digit',
});
const dateOnlyFormat = new Intl.DateTimeFormat(undefined, { weekday: 'short', month: 'short', day: 'numeric' });

export default function CollegeFootballPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const requestedWeek = searchParams.get('week');

  const [weeksState, retryWeeks] = useJson<WeeksResponse>(weeksUrl);
  const weeks = weeksState.status === 'loaded' ? weeksState.data : null;
  const isKnownWeek = weeks?.weeks.some((week) => week.key === requestedWeek) ?? false;
  const selectedWeek = weeks ? (isKnownWeek ? requestedWeek : weeks.defaultWeek) : null;

  const [weekState, retryWeek] = useJson<WeekGamesResponse>(selectedWeek ? weekUrl(selectedWeek) : null);

  // A week that doesn't exist (or that the API no longer knows) falls back to the default week.
  const weekNotFound = weekState.status === 'notFound' && selectedWeek !== weeks?.defaultWeek;
  useEffect(() => {
    if (weeks && requestedWeek !== null && (!isKnownWeek || weekNotFound)) {
      setSearchParams(
        (params) => {
          params.delete('week');
          return params;
        },
        { replace: true },
      );
    }
  }, [weeks, requestedWeek, isKnownWeek, weekNotFound, setSearchParams]);

  // The title names only the sport and week, never teams or tiers.
  const weekLabel = weeks?.weeks.find((week) => week.key === selectedWeek)?.label;
  useEffect(() => {
    document.title = weekLabel ? `College Football – ${weekLabel}` : 'College Football';
  }, [weekLabel]);

  const selectWeek = (key: string) => setSearchParams({ week: key });

  return (
    <section className="college-football">
      <h1>College Football</h1>
      {weeksState.status !== 'loaded' ? (
        <RequestStatus state={weeksState} onRetry={retryWeeks} />
      ) : !weeks || weeks.weeks.length === 0 || selectedWeek === null ? (
        <Unavailable />
      ) : (
        <>
          <label className="week-select">
            Week{' '}
            <select value={selectedWeek} onChange={(event) => selectWeek(event.target.value)}>
              {weeks.weeks.map((week) => (
                <option key={week.key} value={week.key}>
                  {week.label}
                </option>
              ))}
            </select>
          </label>
          {weekState.status === 'loaded' ? (
            <WeekGames week={weekState.data} />
          ) : weekState.status === 'notFound' && !weekNotFound ? (
            <Unavailable />
          ) : (
            <RequestStatus state={weekState} onRetry={retryWeek} />
          )}
        </>
      )}
    </section>
  );
}

function RequestStatus<T>({ state, onRetry }: { state: RequestState<T>; onRetry: () => void }) {
  if (state.status === 'loading' || state.status === 'notFound' || state.status === 'loaded') {
    return (
      <p role="status">
        {state.status === 'loading' && state.slow
          ? 'Waking up the site… this can take a little while after it has been idle.'
          : 'Loading…'}
      </p>
    );
  }

  return (
    <div role="alert">
      <p>
        {state.timedOut
          ? "The site is taking too long to respond. Please try again."
          : 'Games are unavailable right now.'}
      </p>
      <button type="button" onClick={onRetry}>
        Try again
      </button>
    </div>
  );
}

function Unavailable() {
  return <p role="alert">Games are unavailable right now. Please try again later.</p>;
}

function WeekGames({ week }: { week: WeekGamesResponse }) {
  return (
    <>
      {week.updateFailed && (
        <p role="status" className="notice">
          Results couldn&apos;t be updated just now. Please check back later.
        </p>
      )}
      {week.games.length === 0 ? (
        <p>There are no games this week.</p>
      ) : (
        <ul className="games" aria-label={`${week.week.label} games`}>
          {week.games.map((game) => (
            <GameRow key={game.id} game={game} />
          ))}
        </ul>
      )}
    </>
  );
}

function GameRow({ game }: { game: GameSummary }) {
  const kickoff = new Date(game.kickoff);
  const rating = game.status === 'final' && game.tier ? tierLabels[game.tier] : 'Not finished yet';

  return (
    <li className="game">
      <span className="teams">
        {game.awayTeam} at {game.homeTeam}
      </span>
      <time className="kickoff" dateTime={game.kickoff}>
        {game.startTimeTbd ? `${dateOnlyFormat.format(kickoff)}, time TBD` : kickoffFormat.format(kickoff)}
      </time>
      <span className={`rating rating-${game.status === 'final' && game.tier ? game.tier : 'not-finished'}`}>
        {rating}
      </span>
    </li>
  );
}
