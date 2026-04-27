import { useForm } from "react-hook-form";
import { useSearchParams, Navigate } from "react-router-dom";
import { submitLoginForm } from "@/api/auth";
import { useMe } from "@/hooks/useMe";
import SiteBrand from "@/components/SiteBrand";
import { useSiteAppearance } from "@/providers/SiteAppearanceProvider";

type FormValues = {
  username: string;
  password: string;
};

export default function Login() {
  const [searchParams] = useSearchParams();
  const { data: me, isLoading: meLoading } = useMe();
  const { effectiveAppearance } = useSiteAppearance();

  const error = searchParams.get("error");
  const prefilledUsername = searchParams.get("username") ?? "";
  const returnUrl = searchParams.get("returnUrl") ?? "/home";

  const { register, handleSubmit, formState: { isSubmitting } } = useForm<FormValues>({
    defaultValues: { username: prefilledUsername, password: "" }
  });

  if (!meLoading && me?.authenticated) {
    return <Navigate to="/home" replace />;
  }

  const onSubmit = (values: FormValues) => {
    submitLoginForm({ ...values, returnUrl });
  };

  return (
    <>
      {error === "invalid" && (
        <div
          className="position-fixed top-0 start-50 translate-middle-x mt-4 z-3"
          style={{ width: "min(100%, 420px)" }}
        >
          <div className="alert alert-danger mb-0" role="alert">
            Invalid username or password.
          </div>
        </div>
      )}

      <div className="login login-v2 fw-bold">
        <div className="login-cover">
          <div
            className="login-cover-img"
            style={{
              backgroundImage: effectiveAppearance.loginCoverImageUrl
                ? `url("${effectiveAppearance.loginCoverImageUrl}")`
                : undefined
            }}
            data-id="login-cover-image"
          ></div>
          <div className="login-cover-bg"></div>
        </div>

        <div className="login-container">
          <div className="login-header">
            <div className="brand">
              <div className="d-flex align-items-center">
                <SiteBrand
                  appearance={effectiveAppearance}
                  className="d-inline-flex align-items-center gap-2"
                  iconClassName="logo"
                  textClassName="d-inline-flex align-items-center"
                  imageClassName="site-appearance-brand-image"
                />
              </div>
              <small>
                {effectiveAppearance.loginTagline || "Sign in to continue to the automation dashboard"}
              </small>
            </div>
            <div className="icon">
              <i className="fa fa-lock"></i>
            </div>
          </div>

          <div className="login-content">
            <form onSubmit={handleSubmit(onSubmit)}>
              <div className="form-floating mb-20px">
                <input
                  type="text"
                  className="form-control fs-13px h-45px border-0"
                  placeholder="Username"
                  id="username"
                  {...register("username", { required: true })}
                />
                <label htmlFor="username" className="d-flex align-items-center text-gray-600 fs-13px">
                  Username
                </label>
              </div>
              <div className="form-floating mb-20px">
                <input
                  type="password"
                  className="form-control fs-13px h-45px border-0"
                  placeholder="Password"
                  id="password"
                  {...register("password", { required: true })}
                />
                <label htmlFor="password" className="d-flex align-items-center text-gray-600 fs-13px">
                  Password
                </label>
              </div>
              <div className="mb-20px">
                <button
                  type="submit"
                  className="btn btn-theme d-block w-100 h-45px btn-lg"
                  disabled={isSubmitting}
                >
                  Sign me in
                </button>
              </div>
            </form>
          </div>
        </div>
      </div>
    </>
  );
}
