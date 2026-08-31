import { useSearchParams, Navigate } from "react-router-dom";
import {
  Alert,
  Box,
  Button,
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

  // The saved SiteAppearance currently stores the old ColorAdmin demo path
  // (`/spa/assets/...`) which 404s in dev. Until the admin re-saves the cover
  // image via Site Configuration, fall through to the new default image when
  // the stored URL looks like that broken legacy path.
  const storedCover = effectiveAppearance.loginCoverImageUrl;
  const coverUrl =
    storedCover && !storedCover.startsWith("/spa/")
      ? storedCover
      : "/assets/img/login-bg/space.jpg";

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
              the error renders silently (WCAG 3.3.1 / 4.1.3, #17). */}
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

        <form onSubmit={form.onSubmit(onSubmit)}>
          <Stack gap="sm">
            {/* No autoFocus on either field: it drops a screen-reader user
                mid-form, past the brand and heading that say which site they
                are signing in to, and it is the whole of jsx-a11y's
                no-autofocus warning here. The form is two fields — Tab
                reaches them immediately (#17). */}
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
      </Card>
    </Box>
  );
}
