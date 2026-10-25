import { act, fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, Route, Routes, useLocation } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import type { GameSummary, WeekGamesResponse, WeeksResponse } from '../collegeFootball/api';
import CollegeFootballPage from './CollegeFootballPage';

const weeks: WeeksResponse = {
  season: 2026,
  defaultWeek: '7',
  weeks: [
    { key: '5', label: 'Week 5' },
    { key: '6', label: 'Week 6' },
    { key: '7', label: 'Week 7' },
    { key: 'postseason', label: 'Postseason' },
  ],
};

const game = (overrides: Partial<GameSummary> & Pick<GameSummary, 'id' | 'homeTeam' | 'awayTeam'>): GameSummary => ({
  kickoff: '2026-10-10T16:00:00Z',
  startTimeTbd: false,
  status: 'final',
  tier: 'worthIt',
  ...overrides,
});

const week7: WeekGamesResponse = {
  season: 2026,
  week: { key: '7', label: 'Week 7' },
  updateFailed: false,
  games: [
    game({ id: 11, homeTeam: 'Georgia', awayTeam: 'Auburn', kickoff: '2026-10-10T16:00:00Z', tier: 'skip' }),
    game({ id: 12, homeTeam: 'Texas', awayTeam: 'Oklahoma', kickoff: '2026-10-10T19:30:00Z', tier: 'worthIt' }),
    game({ id: 13, homeTeam: 'USC', awayTeam: 'Notre Dame', kickoff: '2026-10-10T23:00:00Z', tier: 'mustWatch' }),
    game({ id: 14, homeTeam: 'Hawaii', awayTeam: 'Fresno State', kickoff: '2026-10-11T05:00:00Z', status: 'notFinished', tier: null }),
  ],
};

const week5: WeekGamesResponse = {
  season: 2026,
  week: { key: '5', label: 'Week 5' },
  updateFailed: false,
  games: [game({ id: 21, homeTeam: 'Michigan', awayTeam: 'Ohio State', tier: 'mustWatch' })],
};

const postseason: WeekGamesResponse = {
  season: 2026,
  week: { key: 'postseason', label: 'Postseason' },
  updateFailed: false,
  games: [game({ id: 31, homeTeam: 'Rose Bowl Home', awayTeam: 'Rose Bowl Away', kickoff: '2027-01-01T22:00:00Z' })],
};

type Route = () => Response | Promise<Response>;

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } });

const fetchMock = vi.fn<typeof fetch>();

/** Answers fetches by URL. Unknown URLs are 404s. */
function stubApi(routes: Record<string, Route>) {
  fetchMock.mockImplementation(async (input) => {
    const route = routes[String(input)];
    return route ? route() : new Response(null, { status: 404 });
  });
}

/** A fetch that never answers. */
function hangingFetch(): Route {
  return () => new Promise<Response>(() => {});
}

function LocationDisplay() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname + location.search}</div>;
}

function renderPage(path = '/college-football') {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <Routes>
        <Route path="/college-football" element={<CollegeFootballPage />} />
      </Routes>
      <LocationDisplay />
    </MemoryRouter>,
  );
}

const gameRows = () => within(screen.getByRole('list')).getAllByRole('listitem');
const location = () => screen.getByTestId('location').textContent;
const requestedUrls = () => fetchMock.mock.calls.map(([input]) => String(input));

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal('fetch', fetchMock);
  document.title = '';
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

describe('weekly game list', () => {
  beforeEach(() =>
    stubApi({
      '/api/college-football/weeks': () => json(weeks),
      '/api/college-football/weeks/7': () => json(week7),
    }),
  );

  it('lists games in kickoff order with both teams and the local kickoff time', async () => {
    renderPage();

    await screen.findByRole('list', { name: 'Week 7 games' });
    const rows = gameRows();
    expect(rows.map((row) => row.querySelector('.teams')?.textContent)).toEqual([
      'Auburn at Georgia',
      'Oklahoma at Texas',
      'Notre Dame at USC',
      'Fresno State at Hawaii',
    ]);

    const expectedKickoff = new Intl.DateTimeFormat(undefined, {
      weekday: 'short',
      month: 'short',
      day: 'numeric',
      hour: 'numeric',
      minute: '2-digit',
    }).format(new Date('2026-10-10T19:30:00Z'));
    expect(within(rows[1]).getByText(expectedKickoff)).toHaveAttribute('datetime', '2026-10-10T19:30:00Z');
  });

  it('keeps kickoff order even when a later game has a higher tier', async () => {
    renderPage();

    await screen.findByRole('list');
    const tiers = gameRows().map((row) => row.querySelector('.rating')?.textContent);
    expect(tiers).toEqual(['Skip', 'Worth it', 'Must watch', 'Not finished yet']);
  });

  it('says when a week has no games', async () => {
    stubApi({
      '/api/college-football/weeks': () => json(weeks),
      '/api/college-football/weeks/7': () => json({ ...week7, games: [] }),
    });

    renderPage();

    expect(await screen.findByText('There are no games this week.')).toBeInTheDocument();
  });

  it('shows the date only when the kickoff time is to be decided', async () => {
    stubApi({
      '/api/college-football/weeks': () => json(weeks),
      '/api/college-football/weeks/7': () =>
        json({ ...week7, games: [game({ id: 1, homeTeam: 'A', awayTeam: 'B', startTimeTbd: true, status: 'notFinished', tier: null })] }),
    });

    renderPage();

    expect(await screen.findByText(/time TBD$/)).toBeInTheDocument();
  });
});

describe('watchability tiers', () => {
  beforeEach(() =>
    stubApi({
      '/api/college-football/weeks': () => json(weeks),
      '/api/college-football/weeks/7': () => json(week7),
    }),
  );

  it('shows exactly one tier for each completed game', async () => {
    renderPage();

    await screen.findByRole('list');
    for (const row of gameRows().slice(0, 3)) {
      const labels = ['Must watch', 'Worth it', 'Skip'].filter((label) => within(row).queryByText(label));
      expect(labels).toHaveLength(1);
    }
  });

  it('never explains a tier', async () => {
    renderPage();

    await screen.findByRole('list');
    const text = document.body.textContent ?? '';
    for (const reason of ['upset', 'overtime', 'comeback', 'close game', 'blowout', 'rival', 'ranked', 'score']) {
      expect(text.toLowerCase()).not.toContain(reason);
    }
    expect(screen.getByRole('list').querySelectorAll('[title], img, svg')).toHaveLength(0);
  });

  it('shows "Not finished yet" for an unfinished game with no indication of how long it has run', async () => {
    renderPage();

    await screen.findByRole('list');
    const unfinished = gameRows()[3];
    expect(within(unfinished).getByText('Not finished yet')).toBeInTheDocument();
    expect(unfinished.textContent).not.toMatch(/Must watch|Worth it|Skip|quarter|live|minute|half/i);
  });
});

describe('week selection', () => {
  beforeEach(() =>
    stubApi({
      '/api/college-football/weeks': () => json(weeks),
      '/api/college-football/weeks/5': () => json(week5),
      '/api/college-football/weeks/7': () => json(week7),
      '/api/college-football/weeks/postseason': () => json(postseason),
    }),
  );

  it('opens the default week when no week is in the URL', async () => {
    renderPage();

    await screen.findByRole('list', { name: 'Week 7 games' });
    expect(screen.getByRole('combobox', { name: 'Week' })).toHaveValue('7');
    expect(location()).toBe('/college-football');
  });

  it('shows the chosen week and puts it in the URL', async () => {
    renderPage();
    await screen.findByRole('list', { name: 'Week 7 games' });

    fireEvent.change(screen.getByRole('combobox', { name: 'Week' }), { target: { value: '5' } });

    expect(await screen.findByText('Ohio State at Michigan')).toBeInTheDocument();
    expect(location()).toBe('/college-football?week=5');
    expect(requestedUrls()).toContain('/api/college-football/weeks/5');
  });

  it('opens a bookmarked postseason URL', async () => {
    renderPage('/college-football?week=postseason');

    expect(await screen.findByText('Rose Bowl Away at Rose Bowl Home')).toBeInTheDocument();
    expect(screen.getByRole('combobox', { name: 'Week' })).toHaveValue('postseason');
  });

  it('falls back to the default week for a week that does not exist', async () => {
    renderPage('/college-football?week=99');

    await screen.findByRole('list', { name: 'Week 7 games' });
    expect(location()).toBe('/college-football');
    expect(requestedUrls()).not.toContain('/api/college-football/weeks/99');
  });

  it('falls back to the default week when the API no longer knows the week', async () => {
    renderPage('/college-football?week=6'); // listed, but /weeks/6 is a 404

    await screen.findByRole('list', { name: 'Week 7 games' });
    expect(location()).toBe('/college-football');
  });
});

describe('page title', () => {
  it('names only the sport and week', async () => {
    stubApi({
      '/api/college-football/weeks': () => json(weeks),
      '/api/college-football/weeks/7': () => json(week7),
      '/api/college-football/weeks/5': () => json(week5),
    });

    renderPage();
    await screen.findByRole('list', { name: 'Week 7 games' });
    expect(document.title).toBe('College Football – Week 7');

    fireEvent.change(screen.getByRole('combobox', { name: 'Week' }), { target: { value: '5' } });
    await screen.findByText('Ohio State at Michigan');
    expect(document.title).toBe('College Football – Week 5');

    const teamsAndTiers = [...week7.games, ...week5.games]
      .flatMap((g) => [g.homeTeam, g.awayTeam])
      .concat(['Must watch', 'Worth it', 'Skip', 'Not finished']);
    for (const text of teamsAndTiers) {
      expect(document.title).not.toContain(text);
    }
  });

  it('is just the sport while loading', () => {
    stubApi({ '/api/college-football/weeks': hangingFetch() });

    renderPage();

    expect(document.title).toBe('College Football');
  });
});

describe('slow and failed loading', () => {
  it('shows a waking-up message after 3 seconds and then the games', async () => {
    vi.useFakeTimers();
    let respond: (response: Response) => void = () => {};
    stubApi({
      '/api/college-football/weeks': () => new Promise<Response>((resolve) => (respond = resolve)),
      '/api/college-football/weeks/7': () => json(week7),
    });

    renderPage();
    await act(() => vi.advanceTimersByTimeAsync(2_900));
    expect(screen.getByRole('status')).toHaveTextContent('Loading…');

    await act(() => vi.advanceTimersByTimeAsync(100));
    expect(screen.getByRole('status')).toHaveTextContent(/waking up/i);

    await act(() => vi.advanceTimersByTimeAsync(17_000));
    await act(async () => respond(json(weeks)));
    await act(() => vi.advanceTimersByTimeAsync(0));

    expect(screen.getByRole('list', { name: 'Week 7 games' })).toBeInTheDocument();
    expect(screen.queryByText(/waking up/i)).not.toBeInTheDocument();
  });

  it('gives up after 90 seconds with a neutral error and a retry button', async () => {
    vi.useFakeTimers();
    let attempts = 0;
    fetchMock.mockImplementation((input, init) => {
      if (String(input) === '/api/college-football/weeks' && ++attempts === 1) {
        return new Promise<Response>((_, reject) =>
          init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))),
        );
      }
      return Promise.resolve(String(input) === '/api/college-football/weeks' ? json(weeks) : json(week7));
    });

    renderPage();
    await act(() => vi.advanceTimersByTimeAsync(89_000));
    expect(screen.queryByRole('alert')).not.toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(1_000));
    const alert = screen.getByRole('alert');
    expect(alert).toHaveTextContent(/taking too long/i);

    fireEvent.click(within(alert).getByRole('button', { name: 'Try again' }));
    await act(() => vi.advanceTimersByTimeAsync(0));

    expect(screen.getByRole('list', { name: 'Week 7 games' })).toBeInTheDocument();
    expect(attempts).toBe(2);
  });

  it('applies the waking-up message and timeout to the week request too', async () => {
    vi.useFakeTimers();
    fetchMock.mockImplementation((input, init) =>
      String(input) === '/api/college-football/weeks'
        ? Promise.resolve(json(weeks))
        : new Promise<Response>((_, reject) =>
            init?.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError'))),
          ),
    );

    renderPage();
    await act(() => vi.advanceTimersByTimeAsync(100));
    expect(screen.getByRole('combobox', { name: 'Week' })).toBeInTheDocument();
    expect(screen.getByRole('status')).toHaveTextContent('Loading…');

    await act(() => vi.advanceTimersByTimeAsync(3_000));
    expect(screen.getByRole('status')).toHaveTextContent(/waking up/i);

    await act(() => vi.advanceTimersByTimeAsync(90_000));
    expect(screen.getByRole('alert')).toHaveTextContent(/taking too long/i);
  });

  it('shows a neutral notice when results could not be updated', async () => {
    stubApi({
      '/api/college-football/weeks': () => json(weeks),
      '/api/college-football/weeks/7': () => json({ ...week7, updateFailed: true }),
    });

    renderPage();

    const notice = await screen.findByText(/couldn.t be updated just now/i);
    expect(notice).toHaveAttribute('role', 'status');
    expect(gameRows()).toHaveLength(4);
  });

  it('says games are unavailable when nothing is known about the season', async () => {
    stubApi({ '/api/college-football/weeks': () => json({ season: 2026, defaultWeek: null, weeks: [] }) });

    renderPage();

    expect(await screen.findByRole('alert')).toHaveTextContent('Games are unavailable right now.');
    expect(screen.queryByRole('combobox')).not.toBeInTheDocument();
  });

  it('says games are unavailable, with a retry, when the API fails', async () => {
    let calls = 0;
    stubApi({
      '/api/college-football/weeks': () => (++calls === 1 ? new Response(null, { status: 500 }) : json(weeks)),
      '/api/college-football/weeks/7': () => json(week7),
    });

    renderPage();
    const alert = await screen.findByRole('alert');
    expect(alert).toHaveTextContent('Games are unavailable right now.');

    fireEvent.click(within(alert).getByRole('button', { name: 'Try again' }));

    expect(await screen.findByRole('list', { name: 'Week 7 games' })).toBeInTheDocument();
  });
});
