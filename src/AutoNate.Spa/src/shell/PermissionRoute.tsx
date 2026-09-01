import type { ReactElement } from "react";
import { useMemo } from "react";
import { Alert, Anchor, Box, Center, Loader, Stack, Text, Title } from "@mantine/core";
import { Link } from "react-router-dom";
import { permissionKey, usePermissionChecks } from "@/hooks/usePermissionChecks";

type Props = {
  // The (kind, action) the backend gates this area on. Kind-level checks pass
  // id="*", mirroring RequireKindPermissionFilter.
  kind: string;
  action: string;
  id?: string;
  children: ReactElement;
};

// Route-level permission guard (#85).
//
// ProtectedRoute answers "are you signed in?" and nothing else, so a user with
// no grants could deep-link into an admin shell and get the full chrome — nav,
// headings, empty tables — while every API call behind it returned 403. The
// backend held, so this was never exposure; it was an affordance defect, and
// those are their own kind of harm: the page looks broken rather than
// forbidden, and the user cannot tell which.
//
// Deliberately renders an explicit "you don't have access" panel rather than
// redirecting. A silent bounce to the dashboard is indistinguishable from a
// dead link, and leaves someone who genuinely needs access with nothing to
// take to an administrator.
export default function PermissionRoute({ kind, action, id = "*", children }: Props) {
  const checks = useMemo(() => [{ kind, action, id }], [kind, action, id]);
  const { data, isLoading } = usePermissionChecks(checks);

  if (isLoading) {
    return (
      <Center mih="60vh">
        <Loader />
      </Center>
    );
  }

  const allowed = data?.get(permissionKey(checks[0])) ?? false;
  if (allowed) return children;

  return (
    <Box py="xl">
      <Stack align="center" gap="md">
        <Title order={2}>You don&apos;t have access to this area</Title>
        <Alert color="gray" variant="light" maw={560}>
          <Text size="sm">
            This section needs the <strong>{kind}</strong> &ldquo;{action}&rdquo; permission.
            Ask an administrator to grant it if you need access.
          </Text>
        </Alert>
        <Anchor component={Link} to="/">
          Back to the dashboard
        </Anchor>
      </Stack>
    </Box>
  );
}
