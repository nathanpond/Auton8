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

// ---------- Claim → group mappings (#92) ----------

/**
 * One edge: "this claim value, from this provider, grants this group".
 *
 * An unmapped IdP group grants nothing. The mapping is the whole gate — a group
 * created at the identity provider has no effect in Auton8 until someone here
 * decides it should.
 */
export type GroupMapping = {
  id: string;
  providerId: string;
  claimType: string;
  claimValue: string;
  groupId: string;
  groupName: string;
  groupIsArchived: boolean;
  createdAtUtc: string;
  updatedAtUtc: string;
};

export type UpsertGroupMappingRequest = {
  claimType: string;
  claimValue: string;
  groupId: string;
};

/** A group the previewed claims would grant. */
export type PreviewedGroup = {
  id: string;
  name: string;
  isArchived: boolean;
};

export async function listGroupMappings(
  providerId: string,
  signal?: AbortSignal
): Promise<GroupMapping[]> {
  const res = await api.get<GroupMapping[]>(`${BASE}/${providerId}/group-mappings`, { signal });
  return res.data;
}

export async function createGroupMapping(
  providerId: string,
  request: UpsertGroupMappingRequest
): Promise<GroupMapping> {
  const res = await api.post<GroupMapping>(`${BASE}/${providerId}/group-mappings`, request);
  return res.data;
}

export async function updateGroupMapping(
  providerId: string,
  mappingId: string,
  request: UpsertGroupMappingRequest
): Promise<GroupMapping> {
  const res = await api.put<GroupMapping>(
    `${BASE}/${providerId}/group-mappings/${mappingId}`,
    request
  );
  return res.data;
}

export async function deleteGroupMapping(
  providerId: string,
  mappingId: string
): Promise<void> {
  await api.delete(`${BASE}/${providerId}/group-mappings/${mappingId}`);
}

/**
 * What a claim set would grant, without asking anyone to sign in.
 *
 * Answered by the same code the sign-in path uses, so this cannot drift into
 * being decorative — there is nothing for it to drift from.
 */
export async function previewGroupMappings(
  providerId: string,
  claims: Record<string, string[]>
): Promise<PreviewedGroup[]> {
  const res = await api.post<PreviewedGroup[]>(
    `${BASE}/${providerId}/group-mappings/preview`,
    { claims }
  );
  return res.data;
}
