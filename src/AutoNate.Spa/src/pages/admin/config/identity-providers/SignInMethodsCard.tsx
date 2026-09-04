import { toast } from "@/components/notifications/toast";
import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Alert, Card, Group, Stack, Switch, Text, Title } from "@mantine/core";
import {
  SignInMethods,
  getStoredSignInMethods,
  updateSignInMethods
} from "@/api/identityProviders";

/**
 * Which sign-in methods are enabled (#94).
 *
 * Deliberately here rather than on the generic Features settings page. That page
 * renders any registry-declared boolean as a plain toggle with no cross-field
 * validation, so switching local sign-in off there would be one click from a
 * lockout with no explanation. These three depend on the providers listed below
 * them, so they belong beside them.
 */
export function SignInMethodsCard() {
  const queryClient = useQueryClient();
  const [draft, setDraft] = useState<SignInMethods | null>(null);
  const [refusal, setRefusal] = useState<string | null>(null);

  const stored = useQuery({
    queryKey: ["sign-in-methods", "stored"] as const,
    queryFn: ({ signal }) => getStoredSignInMethods(signal)
  });

  useEffect(() => {
    if (stored.data) {
      setDraft({
        local: stored.data.local,
        oidc: stored.data.oidc,
        saml: stored.data.saml
      });
    }
  }, [stored.data]);

  const save = useMutation({
    mutationFn: (methods: SignInMethods) => updateSignInMethods(methods),
    onSuccess: async () => {
      setRefusal(null);
      toast.success("Sign-in methods updated.");
      await queryClient.invalidateQueries({ queryKey: ["sign-in-methods"] });
    },
    onError: (error, attempted) => {
      // The refusal stays on the page rather than becoming a toast: it is a
      // condition of the configuration, still true after a reload, and it
      // explains what has to happen before the change can be made. A toast
      // would take that away while the administrator was still reading it.
      setRefusal(readError(error));
      // Snap back, so the switches show what is actually stored. Leaving them
      // on the refused state would read as saved.
      setDraft((current) => (current ? { ...current, ...invert(attempted, current) } : current));
      void stored.refetch();
    }
  });

  if (stored.isLoading || !draft) {
    return null;
  }

  const set = (patch: Partial<SignInMethods>) => {
    const next = { ...draft, ...patch };
    setDraft(next);
    setRefusal(null);
    save.mutate(next);
  };

  return (
    <Card withBorder radius="md" padding="md">
      <Stack gap="sm">
        <Stack gap={2}>
          <Title order={5}>Sign-in methods</Title>
          <Text size="sm" c="dimmed">
            Which ways in the login page offers. Turning one off refuses it on the server
            too — a hidden button is not a disabled method.
          </Text>
        </Stack>

        {stored.data?.overrideActive && (
          <Alert color="orange" variant="light" title="Break-glass override is active">
            <code>AUTONATE_FORCE_LOCAL_SIGNIN</code> is set on the host, so local sign-in
            is available whatever this page says. Unset it once the intended methods work
            — until then the site accepts passwords it may be configured to refuse.
          </Alert>
        )}

        {refusal !== null && (
          <Alert color="red" variant="light" title="That configuration was refused">
            {refusal}
          </Alert>
        )}

        <Group gap="xl">
          <Switch
            label="Local (username and password)"
            checked={draft.local}
            disabled={save.isPending}
            onChange={(e) => {
              const checked = e.currentTarget.checked;
              set({ local: checked });
            }}
          />
          <Switch
            label="OIDC"
            checked={draft.oidc}
            disabled={save.isPending}
            onChange={(e) => {
              const checked = e.currentTarget.checked;
              set({ oidc: checked });
            }}
          />
          <Switch
            label="SAML"
            checked={draft.saml}
            disabled={save.isPending}
            onChange={(e) => {
              const checked = e.currentTarget.checked;
              set({ saml: checked });
            }}
          />
        </Group>

        <Text size="xs" c="dimmed">
          Local sign-in cannot be turned off until a federated provider is enabled and has
          completed a sign-in at least once. Configured is not the same as working, and the
          gap between them is where an install locks itself out. Existing sessions are not
          ended by a change here; it applies to the next sign-in.
        </Text>
      </Stack>
    </Card>
  );
}

/** The fields the attempted save changed, back to what they were. */
function invert(attempted: SignInMethods, current: SignInMethods): Partial<SignInMethods> {
  const patch: Partial<SignInMethods> = {};
  if (attempted.local !== current.local) patch.local = !attempted.local;
  if (attempted.oidc !== current.oidc) patch.oidc = !attempted.oidc;
  if (attempted.saml !== current.saml) patch.saml = !attempted.saml;
  return patch;
}

function readError(error: unknown): string {
  const response = (error as { response?: { data?: { error?: string } } })?.response;
  return response?.data?.error ?? (error as Error)?.message ?? "Unexpected error.";
}
