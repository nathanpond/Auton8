import axios, { AxiosError } from "axios";

const LOGIN_PATH = "/";

export const api = axios.create({
  baseURL: "/",
  headers: {
    "Content-Type": "application/json"
  },
  withCredentials: true
});

api.interceptors.response.use(
  (response) => response,
  (error: AxiosError) => {
    const status = error.response?.status;
    const url = error.config?.url ?? "";

    // Auth probe and login endpoints return 401 as normal flow; don't redirect those.
    const isAuthProbe = url.includes("/api/auth/me");

    if (status === 401 && !isAuthProbe) {
      if (typeof window !== "undefined" && window.location.pathname !== LOGIN_PATH) {
        const returnUrl = window.location.pathname + window.location.search;
        window.location.href = `${LOGIN_PATH}?returnUrl=${encodeURIComponent(returnUrl)}`;
      }
    }

    return Promise.reject(error);
  }
);
