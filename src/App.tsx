import { BrowserRouter } from "react-router-dom";

import { AppRoutes } from "./router";

interface AppProps {
  // Test seam used by component tests to swap to a MemoryRouter without
  // re-mounting providers.
  Router?: React.ComponentType<{ children: React.ReactNode }>;
}

export function App({ Router = BrowserRouter }: AppProps = {}): JSX.Element {
  return (
    <Router>
      <AppRoutes />
    </Router>
  );
}

export default App;
