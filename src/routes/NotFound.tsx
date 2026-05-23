import { Link } from "react-router-dom";

export default function NotFound(): JSX.Element {
  return (
    <main style={{ padding: "32px" }}>
      <h1>Not found</h1>
      <p>
        That route does not exist in Honua Console. <Link to="/">Return home</Link>.
      </p>
    </main>
  );
}
