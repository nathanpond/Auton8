import { useState } from "react";
import { Alert, Box, Button, Code, FileInput, Group, Modal, Stack, Text } from "@mantine/core";
import { useUploadPlugin } from "@/hooks/usePlugins";

type Props = {
  onClose: () => void;
};

export default function UploadPluginModal({ onClose }: Props) {
  const upload = useUploadPlugin();
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!file) {
      setError("Choose a .zip file first.");
      return;
    }
    try {
      await upload.mutateAsync(file);
      onClose();
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title="Upload plugin" centered>
      <Box component="form" onSubmit={submit}>
        <Stack gap="md">
          <Text size="xs" c="dimmed">
            Choose a plugin <Code>.zip</Code> file. The archive must contain a{" "}
            <Code>plugin.json</Code> manifest at the root and the entry assembly listed in it.
          </Text>
          <FileInput
            accept=".zip,application/zip"
            placeholder="Pick a .zip file"
            value={file}
            onChange={setFile}
          />
          {file && (
            <Text size="xs" c="dimmed">
              Selected: <strong>{file.name}</strong> ({Math.round(file.size / 1024)} KB)
            </Text>
          )}
          {error && (
            <Alert color="red" variant="light">
              {error}
            </Alert>
          )}
          <Group justify="flex-end" gap="xs">
            <Button variant="default" onClick={onClose}>
              Cancel
            </Button>
            <Button type="submit" disabled={!file} loading={upload.isPending}>
              Upload
            </Button>
          </Group>
        </Stack>
      </Box>
    </Modal>
  );
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string } } }).response;
    return resp?.data?.error ?? resp?.data?.message ?? String(err);
  }
  return err instanceof Error ? err.message : String(err);
}
