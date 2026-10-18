import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import App from './App';

describe('App', () => {
  afterEach(() => vi.unstubAllGlobals());

  it('shows the greeting from the API', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        new Response(JSON.stringify({ id: 1, message: 'Hello from Azure SQL!', createdAt: '2026-01-01T00:00:00Z' })),
      ),
    );

    render(<App />);

    expect(await screen.findByText('Hello from Azure SQL!')).toBeInTheDocument();
  });

  it('shows an error when the API fails', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(null, { status: 500 })));

    render(<App />);

    expect(await screen.findByRole('alert')).toHaveTextContent('status 500');
  });
});
