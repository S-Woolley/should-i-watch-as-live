import { Link, NavLink, Outlet } from 'react-router';

export default function Layout() {
  return (
    <div className="app">
      <header className="site-header">
        <Link to="/" className="site-name">
          Should I watch it live?
        </Link>
        <nav aria-label="Sports">
          <ul>
            <li>
              {/* NavLink sets aria-current="page" while on any /college-football URL. */}
              <NavLink to="/college-football">College Football</NavLink>
            </li>
          </ul>
        </nav>
      </header>
      <main>
        <Outlet />
      </main>
    </div>
  );
}
