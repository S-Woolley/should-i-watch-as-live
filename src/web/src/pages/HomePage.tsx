import { useEffect } from 'react';
import { Link } from 'react-router';

export default function HomePage() {
  useEffect(() => {
    document.title = 'Should I Watch It Live?';
  }, []);

  return (
    <section>
      <h1>Should I watch it live?</h1>
      <p>
        Missed the game? Find out which finished games are worth watching as if they were live, without spoilers.
        Every game is rated <strong>Must watch</strong>, <strong>Worth it</strong> or <strong>Skip</strong>, and
        scores are never shown.
      </p>
      <p>
        <Link to="/college-football">See this week&apos;s College Football games</Link>
      </p>
    </section>
  );
}
