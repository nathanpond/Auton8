import { useParams } from "react-router-dom";
import { Alert, Box, Text } from "@mantine/core";
import { JsxFormHost } from "@/components/JsxFormHost";
import { useFormPublishedSnapshot } from "@/hooks/useForms";

// Public, signed-in render of a published form. The backend enforces both
// `published_version_number IS NOT NULL` and `site_available=true` before
// returning the snapshot, so a 404 here covers both the unpublished and the
// hidden cases.
export default function FormPublicView() {
  const { shortCode } = useParams<{ shortCode: string }>();
  const { data: snapshot, isLoading, error } = useFormPublishedSnapshot(
    shortCode ?? null
  );

  if (isLoading) {
    return (
      <Box p="md">
        <Text c="dimmed">
          <i className="fa fa-spinner fa-spin" style={{ marginRight: 8 }} />
          Loading form…
        </Text>
      </Box>
    );
  }

  if (error) {
    return (
      <Box p="md">
        <Alert color="red" variant="light">
          Failed to load form: {(error as Error).message}
        </Alert>
      </Box>
    );
  }

  if (!snapshot) {
    return (
      <Box p="md">
        <Alert color="yellow" variant="light">
          No published form available at <code>/form/{shortCode}</code>.
        </Alert>
      </Box>
    );
  }

  return (
    <Box className="form-public-view" p="md">
      <JsxFormHost source={snapshot.formCode} mode="edit" />
    </Box>
  );
}
