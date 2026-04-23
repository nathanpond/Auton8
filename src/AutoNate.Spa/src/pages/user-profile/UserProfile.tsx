import { useMe } from "@/hooks/useMe";

export default function UserProfile() {
  const { data, isLoading } = useMe();

  if (isLoading) {
    return <p className="text-body text-opacity-75">Loading...</p>;
  }

  if (!data || data.authenticated !== true) {
    return <p className="text-body text-opacity-75">No authenticated user.</p>;
  }

  const displayName = `${data.firstName ?? ""} ${data.lastName ?? ""}`.trim();

  return (
    <>
      <div className="page-head">
        <h1 className="page-header mb-1">User Profile</h1>
        <p className="page-head-copy">Review the local account details for the signed-in user.</p>
      </div>
      <div className="row g-3">
        <div className="col-xl-6">
          <div className="card border-0 shadow-sm">
            <div className="card-body">
              <div className="d-flex align-items-center gap-3 mb-4">
                <div className="image image-icon bg-gray-800 text-gray-600 fs-2">
                  <i className="fa fa-user"></i>
                </div>
                <div>
                  <h2 className="h4 mb-1">{displayName || data.username}</h2>
                  <div className="text-body text-opacity-50">{data.username}</div>
                </div>
              </div>

              <dl className="row mb-0">
                <dt className="col-sm-4">First Name</dt>
                <dd className="col-sm-8">{data.firstName}</dd>

                <dt className="col-sm-4">Last Name</dt>
                <dd className="col-sm-8">{data.lastName}</dd>

                <dt className="col-sm-4">Email</dt>
                <dd className="col-sm-8">{data.email}</dd>

                <dt className="col-sm-4">User ID</dt>
                <dd className="col-sm-8 text-break">{data.userId}</dd>

                {data.idpKey && (
                  <>
                    <dt className="col-sm-4">IdP Key</dt>
                    <dd className="col-sm-8 text-break">{data.idpKey}</dd>
                  </>
                )}

                <dt className="col-sm-4">Auth Source</dt>
                <dd className="col-sm-8">{data.authSource}</dd>
              </dl>
            </div>
          </div>
        </div>
      </div>
    </>
  );
}
