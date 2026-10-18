import { useEffect, useState } from 'react';
import './App.css';

interface Greeting {
  id: number;
  message: string;
  createdAt: string;
}

type State =
  | { status: 'loading' }
  | { status: 'loaded'; greeting: Greeting }
  | { status: 'error'; message: string };

function App() {
  const [state, setState] = useState<State>({ status: 'loading' });

  useEffect(() => {
    const controller = new AbortController();

    fetch('/api/greeting', { signal: controller.signal })
      .then(async (response) => {
        if (!response.ok) {
          throw new Error(`Request failed with status ${response.status}`);
        }
        const greeting = (await response.json()) as Greeting;
        setState({ status: 'loaded', greeting });
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted) return;
        setState({ status: 'error', message: err instanceof Error ? err.message : 'Request failed' });
      });

    return () => controller.abort();
  }, []);

  return (
    <main className="app">
      <h1>Should I watch it live?</h1>
      {state.status === 'loading' && <p role="status">Loading…</p>}
      {state.status === 'error' && <p role="alert">Couldn't load greeting: {state.message}</p>}
      {state.status === 'loaded' && <p className="greeting">{state.greeting.message}</p>}
    </main>
  );
}

export default App;
