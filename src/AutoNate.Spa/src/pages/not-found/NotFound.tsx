import { Link } from "react-router-dom";
import { Button, Center, Stack, Text, Title } from "@mantine/core";

export default function NotFound() {
  return (
    <Center p="xl" mih="60vh">
      <Stack align="center" gap="md">
        <Title order={1} fz={96} fw={700} lh={1}>
          404
        </Title>
        <Text size="lg">We couldn&apos;t find the page you were looking for.</Text>
        {/* green.9 rather than the default green shade: white on the
            lighter fill measures below 4.5:1. */}
        <Button component={Link} to="/home" color="green.9" size="md">
          Go Home
        </Button>
      </Stack>
    </Center>
  );
}
