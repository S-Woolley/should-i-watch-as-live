import { useEffect } from 'react';
import { Link } from 'react-router';

export default function NotFoundPage() {
  useEffect(() => {
    document.title = 'Page not found';
  }, []);

  return (
    <section>
      <h1>Page not found</h1>
      <p>
        There&apos;s nothing here. Try <Link to="/college-football">College Football</Link>.
      </p>
    </section>
  );
}
