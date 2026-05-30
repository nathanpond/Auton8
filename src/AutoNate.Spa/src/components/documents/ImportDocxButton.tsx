import { useRef } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "@mantine/core";
import { notifications } from "@mantine/notifications";
import { useImportDocx } from "@/hooks/useDocuments";

// Shared button + hidden file input for the Phase 7 import flow. Sits
// in folder views (accepts .docx) and the template gallery (accepts
// .dotx). The button click delegates to the native file picker; on
// file pick we POST multipart to `/api/content/documents/import`, then
// navigate to the editor with `?import=1` so the editor route fetches
// the stashed buffer and parses it into Yjs on first mount.

type Props = {
  projectId: string;
  // For .docx imports the new doc lands at a specific folder (or project
  // root when null). For .dotx imports we ignore folderId server-side
  // because templates live in their own gallery — pass anyway for
  // consistency; backend silently drops it when the resulting kind is
  // 'template'.
  folderId?: string | null;
  // Controls the file picker's accept filter + button label. The backend
  // dispatches kind from the extension; this prop just shapes the UI
  // surface and what the OS file picker shows by default.
  accept: ".docx" | ".dotx";
  label?: string;
  size?: "xs" | "sm" | "md";
  variant?: "default" | "subtle" | "filled" | "light" | "outline";
  leftSectionIcon?: string;
};

export default function ImportDocxButton({
  projectId,
  folderId = null,
  accept,
  label,
  size = "xs",
  variant = "default",
  leftSectionIcon = "fa fa-file-arrow-up"
}: Props) {
  const navigate = useNavigate();
  const importDocx = useImportDocx();
  const fileInputRef = useRef<HTMLInputElement | null>(null);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    // Reset the input so picking the SAME file twice (after a failed
    // upload) still triggers `onChange`. Browsers suppress the event
    // when value doesn't change otherwise.
    e.target.value = "";
    if (!file) return;
    const fileName = file.name.toLowerCase();
    if (accept === ".docx" && !fileName.endsWith(".docx")) {
      notifications.show({
        message: "Only .docx files are accepted here. Use the template gallery for .dotx.",
        color: "yellow"
      });
      return;
    }
    if (accept === ".dotx" && !fileName.endsWith(".dotx")) {
      notifications.show({
        message: "Only .dotx (template) files are accepted here. Use the folder view for .docx.",
        color: "yellow"
      });
      return;
    }

    try {
      const created = await importDocx.mutateAsync({
        file,
        projectId,
        folderId
      });
      // Editor route handles the rest: fetch buffer → parse → PATCH
      // body_jsonb → DELETE stash → navigate to live mode.
      navigate(`/documents/edit/${created.id}?import=1`);
    } catch (err) {
      console.error("[import] upload failed", err);
      notifications.show({
        message:
          "Upload failed. The server rejected the file — confirm it's a real .docx / .dotx.",
        color: "red"
      });
    }
  };

  return (
    <>
      <Button
        size={size}
        variant={variant}
        leftSection={<i className={leftSectionIcon} aria-hidden />}
        loading={importDocx.isPending}
        onClick={() => fileInputRef.current?.click()}
      >
        {label ?? (accept === ".dotx" ? "Import .dotx" : "Import .docx")}
      </Button>
      <input
        ref={fileInputRef}
        type="file"
        accept={accept}
        style={{ display: "none" }}
        onChange={handleFileChange}
      />
    </>
  );
}
