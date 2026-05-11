import { useEffect, useMemo, useState } from "react";
import { useParams } from "react-router-dom";
import { Alert, Box, Code, Text } from "@mantine/core";
import { JsxFormHost } from "@/components/JsxFormHost";
import { useFormDevSnapshot } from "@/hooks/useForms";

// Renders the *draft* of a form for live-preview workflow. Polls every 1s
// (see useFormDevSnapshot), so the editor + this tab feel hot-reloaded.
// Also subscribes to localStorage so the editor's dev-props sidebar feeds
// `data` / `mode` / `context` straight into this tab without a server hop.
export default function FormDevView() {
  const { shortCode } = useParams<{ shortCode: string }>();
  const { data: snapshot, isLoading, error, dataUpdatedAt } = useFormDevSnapshot(
    shortCode ?? null
  );
  const [lastPoll, setLastPoll] = useState<string>("—");
  const [devPropsRaw, setDevPropsRaw] = useState<string | null>(() =>
    shortCode ? readDevPropsFromStorage(shortCode) : null
  );

  useEffect(() => {
    if (!dataUpdatedAt) return;
    const t = new Date(dataUpdatedAt);
    setLastPoll(t.toLocaleTimeString());
  }, [dataUpdatedAt]);

  // Same-origin tabs receive a `storage` event whenever another tab calls
  // setItem on a key. Listen for ours and re-render with the new payload.
  useEffect(() => {
    if (!shortCode) return;
    const key = devPropsStorageKey(shortCode);
    const onStorage = (event: StorageEvent) => {
      if (event.key !== key) return;
      setDevPropsRaw(event.newValue);
    };
    window.addEventListener("storage", onStorage);
    return () => window.removeEventListener("storage", onStorage);
  }, [shortCode]);

  const devProps = useMemo<Record<string, unknown>>(() => {
    if (!devPropsRaw) return {};
    try {
      return JSON.parse(devPropsRaw) as Record<string, unknown>;
    } catch {
      return {};
    }
  }, [devPropsRaw]);

  const headerNote = useMemo(() => {
    if (!snapshot) return "";
    return `Dev preview · v${snapshot.draftVersionNumber} · last poll ${lastPoll}`;
  }, [snapshot, lastPoll]);

  if (isLoading && !snapshot) {
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
    const status = (error as { response?: { status?: number } }).response?.status;
    if (status === 403) {
      return (
        <Box p="md">
          <Alert color="red" variant="light">
            You don&apos;t have permission to view forms (Form.View required).
          </Alert>
        </Box>
      );
    }
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
          No form found with short code <Code>{shortCode}</Code>.
        </Alert>
      </Box>
    );
  }

  return (
    <div className="form-dev-view">
      <Box
        px="md"
        py="xs"
        style={{
          background: "var(--mantine-color-yellow-1)",
          borderBottom: "1px solid var(--mantine-color-default-border)",
          display: "flex",
          justifyContent: "space-between",
          alignItems: "center"
        }}
      >
        <div>
          <strong>{snapshot.name}</strong> <Code>{snapshot.shortCode}</Code>
        </div>
        <Text size="xs" c="dimmed">
          {headerNote}
        </Text>
      </Box>
      <Box p="md">
        <JsxFormHost
          source={snapshot.formCode}
          data={devProps.data}
          mode={(devProps.mode as "edit" | "view") ?? "edit"}
          context={(devProps.context as Record<string, unknown>) ?? {}}
          extras={devProps as Record<string, unknown>}
        />
      </Box>
    </div>
  );
}

function devPropsStorageKey(shortCode: string): string {
  return `form-dev-props:${shortCode}`;
}

function readDevPropsFromStorage(shortCode: string): string | null {
  try {
    return window.localStorage.getItem(devPropsStorageKey(shortCode));
  } catch {
    return null;
  }
}
