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
 */
export type LoginFormInput = {
  username: string;
  password: string;
  returnUrl?: string;
};

export function submitLoginForm(values: LoginFormInput): void {
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

  appendField("username", values.username);
  appendField("password", values.password);
  if (values.returnUrl) {
    appendField("returnUrl", values.returnUrl);
  }

  document.body.appendChild(form);
  form.submit();
}
