import { useState } from "react";
import { Alert, Box, Button, Code, Group, Modal, Stack, Text } from "@mantine/core";
import { Dropzone, MIME_TYPES } from "@mantine/dropzone";
import { Plugin } from "@/api/plugins";
import { useUpdatePlugin, useUploadPlugin } from "@/hooks/usePlugins";

type Props = {
  onClose: () => void;
  // When provided the modal runs in "update" mode: file is sent to the
  // per-id update endpoint, preserving the plugin's schema/role/data. When
  // null/undefined the modal uploads a brand-new plugin.
  updateTarget?: Plugin | null;
};

export default function UploadPluginModal({ onClose, updateTarget }: Props) {
  const upload = useUploadPlugin();
  const update = useUpdatePlugin();
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState<string | null>(null);

  const isUpdate = !!updateTarget;
  const pending = isUpdate ? update.isPending : upload.isPending;

  const submit = async (e: React.FormEvent) => {
    e.preventDefault();
    setError(null);
    if (!file) {
      setError("Choose a .zip file first.");
      return;
    }
    try {
      if (isUpdate && updateTarget) {
        await update.mutateAsync({ id: updateTarget.id, file });
      } else {
        await upload.mutateAsync(file);
      }
      onClose();
    } catch (err) {
      setError(describeError(err));
    }
  };

  return (
    <Modal
      opened
      onClose={onClose}
      title={isUpdate ? `Update plugin: ${updateTarget?.name}` : "Upload plugin"}
      centered
    >
      <Box component="form" onSubmit={submit}>
        <Stack gap="md">
          {isUpdate ? (
            <Text size="xs" c="dimmed">
              Choose a new plugin <Code>.zip</Code> file to replace{" "}
              <strong>{updateTarget?.name}</strong> (currently v{updateTarget?.version}).
              The plugin's per-plugin schema and stored data are preserved; only the code
              is swapped. If the plugin is currently enabled it will be re-enabled
              automatically after the swap.
            </Text>
          ) : (
            <Text size="xs" c="dimmed">
              Choose a plugin <Code>.zip</Code> file. The archive must contain a{" "}
              <Code>plugin.json</Code> manifest at the root and the entry assembly listed in it.
            </Text>
          )}

          <Dropzone
            accept={[MIME_TYPES.zip, "application/x-zip-compressed"]}
            maxFiles={1}
            multiple={false}
            disabled={pending}
            onDrop={(files) => {
              setError(null);
              setFile(files[0] ?? null);
            }}
            onReject={(rejections) => {
              const first = rejections[0]?.errors?.[0];
              setError(first?.message ?? "Drop a single .zip file.");
            }}
          >
            <Group justify="center" gap="md" mih={120} style={{ pointerEvents: "none" }}>
              <Dropzone.Accept>
                <i className="fa fa-arrow-up-from-bracket" style={{ fontSize: 32 }} />
              </Dropzone.Accept>
              <Dropzone.Reject>
                <i className="fa fa-circle-xmark" style={{ fontSize: 32, color: "var(--mantine-color-red-filled)" }} />
              </Dropzone.Reject>
              <Dropzone.Idle>
                <i className="fa fa-file-zipper" style={{ fontSize: 32, color: "var(--mantine-color-dimmed)" }} />
              </Dropzone.Idle>
              <div>
                <Text size="sm" fw={500}>
                  Drag a .zip here or click to browse
                </Text>
                <Text size="xs" c="dimmed" mt={4}>
                  One plugin archive at a time.
                </Text>
              </div>
            </Group>
          </Dropzone>

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
            <Button type="submit" disabled={!file} loading={pending}>
              {isUpdate ? "Update" : "Upload"}
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
