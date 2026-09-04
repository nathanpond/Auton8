import { useEffect, useState } from "react";
import { useSearchParams, Navigate } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { getSignInMethods, listEnabledProviders } from "@/api/identityProviders";
import {
  Alert,
  Box,
  Button,
  Divider,
  Card,
  Group,
  PasswordInput,
  Stack,
  Text,
  TextInput,
  ThemeIcon
} from "@mantine/core";
import { useForm } from "@mantine/form";
import { submitLoginForm } from "@/api/auth";
import { useMe } from "@/hooks/useMe";
import SiteBrand from "@/components/SiteBrand";
import { useSiteAppearance } from "@/providers/SiteAppearanceProvider";

type FormValues = {
  username: string;
  password: string;
};

/**
 * Shipped with the app, so it cannot itself 404 — which matters, because it is
 * what every other failure falls back to.
 */
const DEFAULT_COVER = "/assets/img/login-bg/space.jpg";

export default function Login() {
  const [searchParams] = useSearchParams();
  const { data: me, isLoading: meLoading } = useMe();
  const { effectiveAppearance } = useSiteAppearance();

  const error = searchParams.get("error");
  const prefilledUsername = searchParams.get("username") ?? "";
  const returnUrl = searchParams.get("returnUrl") ?? "/home";

  const form = useForm<FormValues>({
    initialValues: { username: prefilledUsername, password: "" },
    validate: {
      username: (v) => (v.trim().length === 0 ? "Username is required" : null),
      password: (v) => (v.length === 0 ? "Password is required" : null)
    }
  });

  // Enabled providers, for the federated buttons. A failure here must not stop
  // the local form rendering — an IdP being unreachable should not lock
  // everyone out of the password path.
  const providersQuery = useQuery({
    queryKey: ["enabled-identity-providers"],
    queryFn: ({ signal }) => listEnabledProviders(signal),
    retry: false
  });
  const providers = providersQuery.data ?? [];

  // Which methods to draw (#94). Defaults to local-on if the call fails, for
  // the same reason the providers query does: a request that did not come back
  // must not be the thing that hides the last way in. The server refuses a
  // disabled method regardless, so an optimistic default here costs a clearer
  // error message, never a bypass.
  const methodsQuery = useQuery({
    queryKey: ["sign-in-methods"],
    queryFn: ({ signal }) => getSignInMethods(signal),
    retry: false
  });
  const localEnabled = methodsQuery.data?.local ?? true;

  // Above the early return below: hooks must run in the same order on every
  // render, and an authenticated visitor returns before this point. eslint's
  // rules-of-hooks caught this when the block sat lower down.
  // Two different ways the cover can be wrong, and they need different
  // handling.
  //
  // The first is known-bad by shape: saved appearance may still hold the old
  // ColorAdmin demo path (`/spa/assets/...`), which 404s. That is cheap to
  // reject without asking the network.
  const storedCover = effectiveAppearance.loginCoverImageUrl;
  const requestedCover =
    storedCover && !storedCover.startsWith("/spa/")
      ? storedCover
      : DEFAULT_COVER;

  // The second is a URL that is perfectly well-formed and simply is not there.
  // The cover is a CSS `background-image`, and CSS has no error event — a 404
  // renders as an empty box behind the card with nothing to indicate why. So
  // the image is preloaded and the default swapped in if it fails to load.
  //
  // Starting from `requestedCover` rather than the default means the common
  // case paints immediately and never flashes the wrong background; only a
  // genuine failure causes a swap.
  const [coverUrl, setCoverUrl] = useState(requestedCover);

  useEffect(() => {
    setCoverUrl(requestedCover);
    if (requestedCover === DEFAULT_COVER) return;

    let cancelled = false;
    const probe = new Image();
    probe.onerror = () => {
      if (!cancelled) setCoverUrl(DEFAULT_COVER);
    };
    probe.src = requestedCover;

    // The login page is where someone lands and leaves quickly; without this a
    // late error from an abandoned probe would set state on an unmounted page.
    return () => {
      cancelled = true;
      probe.onerror = null;
    };
  }, [requestedCover]);

  if (!meLoading && me?.authenticated) {
    return <Navigate to="/home" replace />;
  }

  const onSubmit = async (values: FormValues) => {
    try {
      await submitLoginForm({ ...values, returnUrl });
    } catch (err) {
      // Token fetch failed (network glitch / server down). Redirect to
      // /?error=invalid so the existing error banner surfaces something
      // instead of leaving the submit button silently hung.
      console.error("Failed to obtain antiforgery token before login submit", err);
      window.location.href = "/?error=invalid";
    }
  };


  return (
    <Box
      style={{
        position: "fixed",
        inset: 0,
        display: "flex",
        alignItems: "center",
        justifyContent: "center",
        padding: 16,
        overflow: "auto"
      }}
    >
      {/* Cover image + dark overlay. Pinned behind the card. */}
      <Box
        aria-hidden
        style={{
          position: "absolute",
          inset: 0,
          backgroundImage: coverUrl ? `url("${coverUrl}")` : undefined,
          backgroundSize: "cover",
          backgroundPosition: "center",
          backgroundColor: "#0d1117"
        }}
      />
      <Box
        aria-hidden
        style={{
          position: "absolute",
          inset: 0,
          background: "rgba(0,0,0,0.35)"
        }}
      />

      {(error === "invalid" || error === "locked") && (
        <Box
          style={{
            position: "fixed",
            top: 16,
            left: "50%",
            transform: "translateX(-50%)",
            width: "min(100%, 420px)",
            zIndex: 3
          }}
        >
          {/* role="alert" so a failed sign-in is announced. Without it the
              user is left on a form that appears to have done nothing —
              the error renders silently (WCAG 3.3.1 / 4.1.3, archived-17). */}
          <Alert color="red" variant="filled" radius="md" role="alert">
            {error === "locked"
              ? "This account is locked after too many failed sign-in attempts. Contact an administrator to unlock it."
              : "Invalid username or password."}
          </Alert>
        </Box>
      )}

      <Card
        shadow="xl"
        radius="md"
        padding="xl"
        withBorder={false}
        style={{ position: "relative", width: "min(100%, 420px)", zIndex: 1 }}
      >
        <Group justify="space-between" align="flex-start" wrap="nowrap" mb="lg">
          <Stack gap={4}>
            <SiteBrand
              appearance={effectiveAppearance}
              style={{ display: "inline-flex", alignItems: "center", gap: 8 }}
              iconClassName=""
              textClassName=""
              imageClassName=""
            />
            <Text size="xs" c="dimmed">
              {effectiveAppearance.loginTagline ||
                "Sign in to continue to the automation dashboard"}
            </Text>
          </Stack>
          <ThemeIcon variant="light" color="gray" size="lg" radius="md">
            <i className="fa fa-lock" />
          </ThemeIcon>
        </Group>

        {/* The challenge path is chosen by kind, not by assuming one protocol:
            a SAML provider sent to the OIDC challenge gets a redirect to
            nowhere, and the symptom — "the button does nothing" — says
            nothing about the cause. */}
        {providers.length > 0 && (
          <Stack gap="xs" mb="md">
            {providers.map((p) => (
              <Button
                key={p.slug}
                component="a"
                href={`/api/auth/${p.kind === "saml" ? "saml" : "oidc"}/${encodeURIComponent(p.slug)}/challenge?returnUrl=${encodeURIComponent(returnUrl)}`}
                variant="default"
                fullWidth
                size="md"
                leftSection={<i className="fa fa-right-to-bracket" aria-hidden="true" />}
              >
                Continue with {p.displayName}
              </Button>
            ))}
            {localEnabled && (
              <Divider label="or sign in with a password" labelPosition="center" my="xs" />
            )}
          </Stack>
        )}

        {!localEnabled && providers.length === 0 && (
          <Alert color="red" variant="light" title="No way to sign in">
            This site has no sign-in method available. An administrator needs to enable
            one, or an operator can set the break-glass environment variable documented
            in DEPLOYMENT.md to restore password sign-in.
          </Alert>
        )}

        {localEnabled && (
        <form onSubmit={form.onSubmit(onSubmit)}>
          <Stack gap="sm">
            {/* No autoFocus on either field: it drops a screen-reader user
                mid-form, past the brand and heading that say which site they
                are signing in to, and it is the whole of jsx-a11y's
                no-autofocus warning here. The form is two fields — Tab
                reaches them immediately (archived-17). */}
            <TextInput
              label="Username"
              placeholder="Username"
              autoComplete="username"
              {...form.getInputProps("username")}
            />
            <PasswordInput
              label="Password"
              placeholder="Password"
              autoComplete="current-password"
              {...form.getInputProps("password")}
            />
            <Button
              type="submit"
              fullWidth
              size="md"
              loading={form.submitting}
              mt="xs"
            >
              Sign me in
            </Button>
          </Stack>
        </form>
        )}
      </Card>
    </Box>
  );
}
