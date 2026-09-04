import { api } from "./client";

export type IdentityProviderKind = "oidc" | "saml";

/**
 * A configured identity provider.
 *
 * There is deliberately no `secret` field. The backend DTO has nowhere to put a
 * plaintext, so a secret set here can never be read back — `hasSecret` and
 * `secretFingerprint` are what the screen needs: whether one is set, and enough
 * to tell two apart.
 */
export type IdentityProvider = {
  id: string;
  kind: IdentityProviderKind;
  displayName: string;
  slug: string;
  isEnabled: boolean;
  oidcAuthority: string | null;
  oidcClientId: string | null;
  oidcScopes: string | null;
  samlEntityId: string | null;
  samlMetadataUrl: string | null;
  hasSamlMetadataXml: boolean;
  samlSigningCertificate: string | null;
  hasSecret: boolean;
  secretFingerprint: string | null;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type CreateIdentityProviderRequest = {
  kind: IdentityProviderKind;
  displayName: string;
  slug?: string;
  isEnabled?: boolean;
  oidcAuthority?: string | null;
  oidcClientId?: string | null;
  oidcScopes?: string | null;
  samlEntityId?: string | null;
  samlMetadataUrl?: string | null;
  samlMetadataXml?: string | null;
  samlSigningCertificate?: string | null;
  secret?: string | null;
};

/**
 * Every field is optional and omission means "leave alone".
 *
 * `secret` in particular: omitting it keeps the stored secret, sending `""`
 * clears it. A rename must not silently wipe the credential.
 */
export type UpdateIdentityProviderRequest = Partial<
  Omit<CreateIdentityProviderRequest, "kind" | "slug">
>;

export type IdentityProviderTestResult = {
  success: boolean;
  summary: string;
  findings: string[];
};

const BASE = "/api/admin/identity-providers";

export async function listIdentityProviders(
  signal?: AbortSignal
): Promise<IdentityProvider[]> {
  const res = await api.get<IdentityProvider[]>(BASE, { signal });
  return res.data;
}

export async function getIdentityProvider(
  id: string,
  signal?: AbortSignal
): Promise<IdentityProvider> {
  const res = await api.get<IdentityProvider>(`${BASE}/${id}`, { signal });
  return res.data;
}

export async function createIdentityProvider(
  request: CreateIdentityProviderRequest
): Promise<IdentityProvider> {
  const res = await api.post<IdentityProvider>(BASE, request);
  return res.data;
}

export async function updateIdentityProvider(
  id: string,
  request: UpdateIdentityProviderRequest
): Promise<IdentityProvider> {
  // PATCH, not PUT: the backend distinguishes "field omitted" from "field
  // cleared", which is what keeps an edit to the display name from wiping the
  // stored secret.
  const res = await api.patch<IdentityProvider>(`${BASE}/${id}`, request);
  return res.data;
}

export async function setIdentityProviderEnabled(
  id: string,
  enabled: boolean
): Promise<IdentityProvider> {
  const res = await api.post<IdentityProvider>(
    `${BASE}/${id}/${enabled ? "enable" : "disable"}`
  );
  return res.data;
}

export async function deleteIdentityProvider(id: string): Promise<void> {
  await api.delete(`${BASE}/${id}`);
}

export async function testIdentityProvider(
  id: string
): Promise<IdentityProviderTestResult> {
  const res = await api.post<IdentityProviderTestResult>(`${BASE}/${id}/test`);
  return res.data;
}

/** What a signed-out visitor may know about a provider: enough to draw a button. */
export type EnabledProviderSummary = {
  slug: string;
  displayName: string;
  kind: IdentityProviderKind;
};

/**
 * Enabled providers, for the login page.
 *
 * Deliberately a separate anonymous endpoint rather than the admin list: a
 * signed-out visitor gets display name, kind and slug, never the authority,
 * client id or anything about the secret.
 */
export async function listEnabledProviders(
  signal?: AbortSignal
): Promise<EnabledProviderSummary[]> {
  const res = await api.get<EnabledProviderSummary[]>("/api/auth/providers", { signal });
  return res.data;
}
