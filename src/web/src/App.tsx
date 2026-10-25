import { BrowserRouter, Route, Routes } from 'react-router';
import './App.css';
import Layout from './Layout';
import CollegeFootballPage from './pages/CollegeFootballPage';
import HomePage from './pages/HomePage';
import NotFoundPage from './pages/NotFoundPage';

/** The site's routes, without a router so tests can render them in a MemoryRouter. */
export function AppRoutes() {
  return (
    <Routes>
      <Route element={<Layout />}>
        <Route index element={<HomePage />} />
        <Route path="college-football" element={<CollegeFootballPage />} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}

function App() {
  return (
    <BrowserRouter>
      <AppRoutes />
    </BrowserRouter>
  );
}

export default App;
