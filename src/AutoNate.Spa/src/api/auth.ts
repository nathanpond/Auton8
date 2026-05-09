import { api } from "./client";
import { CurrentUser } from "@/types/flowable";

export async function fetchCurrentUser(signal?: AbortSignal): Promise<CurrentUser> {
  const { data } = await api.get<CurrentUser>("/api/auth/me", { signal });
  return data;
}

export async function logout(): Promise<void> {
  await api.post("/api/auth/logout");
}

export type PermissionCheck = {
  kind: string;
  action: string;
  id: string;
};

export type PermissionCheckResult = PermissionCheck & { allowed: boolean };

// Batched per-instance permission check. Returns parallel results so the
// caller can map back by index or by (kind, action, id).
export async function checkPermissions(
  checks: PermissionCheck[],
  signal?: AbortSignal
): Promise<PermissionCheckResult[]> {
  if (checks.length === 0) return [];
  const { data } = await api.post<{ authenticated: boolean; results: PermissionCheckResult[] }>(
    "/api/auth/check",
    { checks },
    { signal }
  );
  return data.results ?? [];
}

/**
 * Native form submission to /account/login (existing server endpoint) so the
 * redirect-on-success flow keeps working. The SPA posts credentials through
 * the Vite proxy or same-origin in prod.
 *
 * The endpoint enforces antiforgery (defense against login CSRF), so the
 * SPA fetches a token + cookie pair from /api/auth/antiforgery first and
 * carries the token as a hidden form field whose name the server returns.
 * Same-origin requirement means the antiforgery cookie issued by the GET
 * is automatically included on the subsequent POST.
 */
export type LoginFormInput = {
  username: string;
  password: string;
  returnUrl?: string;
};

type AntiforgeryToken = {
  token: string;
  formFieldName: string;
  headerName: string;
};

export async function submitLoginForm(values: LoginFormInput): Promise<void> {
  const { data: tokens } = await api.get<AntiforgeryToken>("/api/auth/antiforgery");

  const form = document.createElement("form");
  form.method = "post";
  form.action = "/account/login";

  const appendField = (name: string, value: string) => {
    const input = document.createElement("input");
    input.type = "hidden";
    input.name = name;
    input.value = value;
    form.appendChild(input);
  };

  appendField(tokens.formFieldName, tokens.token);
  appendField("username", values.username);
  appendField("password", values.password);
  if (values.returnUrl) {
    appendField("returnUrl", values.returnUrl);
  }

  document.body.appendChild(form);
  form.submit();
}
