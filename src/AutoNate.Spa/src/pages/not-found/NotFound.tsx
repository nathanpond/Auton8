import { Link } from "react-router-dom";

export default function NotFound() {
  return (
    <div className="p-5 text-center">
      <h1 className="display-1 mb-2">404</h1>
      <p className="lead mb-3">We couldn't find the page you were looking for.</p>
      <Link to="/home" className="btn btn-success px-3">
        Go Home
      </Link>
    </div>
  );
}
