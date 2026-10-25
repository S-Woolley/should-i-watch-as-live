import { fireEvent, render, screen, within } from '@testing-library/react';
import { MemoryRouter, useLocation } from 'react-router';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { AppRoutes } from './App';

function LocationDisplay() {
  const location = useLocation();
  return <div data-testid="location">{location.pathname + location.search}</div>;
}

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <AppRoutes />
      <LocationDisplay />
    </MemoryRouter>,
  );
}

const navigation = () => screen.getByRole('navigation', { name: 'Sports' });
const collegeFootballNavItem = () => within(navigation()).getByRole('link', { name: 'College Football' });

describe('site navigation', () => {
  const fetchMock = vi.fn<typeof fetch>();

  beforeEach(() => {
    // College Football data never arrives in these tests; they are about routing.
    fetchMock.mockReturnValue(new Promise<Response>(() => {}));
    vi.stubGlobal('fetch', fetchMock);
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    fetchMock.mockReset();
  });

  it.each(['/', '/college-football', '/college-football?week=5', '/no-such-page'])('shows the menu on %s', (path) => {
    renderAt(path);

    expect(within(navigation()).getAllByRole('link')).toHaveLength(1);
    expect(collegeFootballNavItem()).toHaveAttribute('href', '/college-football');
  });

  it('marks College Football as the current page on any College Football URL', () => {
    renderAt('/college-football?week=postseason');

    expect(collegeFootballNavItem()).toHaveAttribute('aria-current', 'page');
  });

  it('marks nothing as current on the home page', () => {
    renderAt('/');

    expect(screen.queryAllByRole('link', { current: 'page' })).toHaveLength(0);
    expect(collegeFootballNavItem()).not.toHaveAttribute('aria-current');
  });

  it('opens College Football from the menu', async () => {
    renderAt('/');

    fireEvent.click(collegeFootballNavItem());

    expect(await screen.findByRole('heading', { level: 1, name: 'College Football' })).toBeInTheDocument();
    expect(screen.getByTestId('location')).toHaveTextContent(/^\/college-football$/);
  });

  it('shows College Football when opened directly', () => {
    renderAt('/college-football');

    expect(screen.getByRole('heading', { level: 1, name: 'College Football' })).toBeInTheDocument();
    expect(navigation()).toBeInTheDocument();
  });

  it('home page describes the site, links to College Football and loads no game data', () => {
    renderAt('/');

    expect(screen.getByRole('heading', { level: 1, name: 'Should I watch it live?' })).toBeInTheDocument();
    expect(screen.getByText(/without spoilers/i)).toBeInTheDocument();
    expect(within(screen.getByRole('main')).getByRole('link', { name: /College Football/ })).toHaveAttribute(
      'href',
      '/college-football',
    );
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it('shows page not found with the menu for unknown paths', () => {
    renderAt('/basketball');

    expect(screen.getByRole('heading', { level: 1, name: 'Page not found' })).toBeInTheDocument();
    expect(collegeFootballNavItem()).toBeInTheDocument();
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
