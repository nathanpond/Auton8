import type { ReactElement } from "react";
import { Navigate, useLocation } from "react-router-dom";
import { useMe } from "@/hooks/useMe";

type Props = {
  children: ReactElement;
};

export default function ProtectedRoute({ children }: Props) {
  const location = useLocation();
  const { data, isLoading } = useMe();

  if (isLoading) {
    return <FullPageLoader />;
  }

  if (!data || data.authenticated !== true) {
    const returnUrl = encodeURIComponent(location.pathname + location.search);
    return <Navigate to={`/?returnUrl=${returnUrl}`} replace />;
  }

  return children;
}

function FullPageLoader() {
  return (
    <div className="d-flex justify-content-center align-items-center vh-100">
      <div className="spinner-border text-primary" role="status">
        <span className="visually-hidden">Loading...</span>
      </div>
    </div>
  );
}
