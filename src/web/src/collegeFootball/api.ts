// Response types of /api/college-football. They carry no scores or other result data by design.

export interface WeekOption {
  key: string;
  label: string;
}

export interface WeeksResponse {
  season: number;
  defaultWeek: string | null;
  weeks: WeekOption[];
}

export type Tier = 'mustWatch' | 'worthIt' | 'skip';

export interface GameSummary {
  id: number;
  homeTeam: string;
  awayTeam: string;
  kickoff: string;
  startTimeTbd: boolean;
  status: 'final' | 'notFinished';
  tier: Tier | null;
}

export interface WeekGamesResponse {
  season: number;
  week: WeekOption;
  updateFailed: boolean;
  games: GameSummary[];
}

export const weeksUrl = '/api/college-football/weeks';

export const weekUrl = (key: string) => `/api/college-football/weeks/${encodeURIComponent(key)}`;

export const tierLabels: Record<Tier, string> = {
  mustWatch: 'Must watch',
  worthIt: 'Worth it',
  skip: 'Skip',
};
