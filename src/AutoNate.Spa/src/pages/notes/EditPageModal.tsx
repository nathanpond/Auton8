import { useEffect, useState } from "react";
import { TextInput } from "@mantine/core";
import { PageTreeNodeDto } from "@/api/content";
import { NotesModal, btnGhostStyle, btnPrimaryStyle, notesInputStyles } from "./NotesModal";

type Props = {
  page: PageTreeNodeDto;
  onClose: () => void;
  onSave: (vars: { title: string }) => void;
  submitting?: boolean;
};

// Slim "rename page" modal. Pages don't carry icon/description in this design
// — body is edited in the tab itself — so this is title-only.
export function EditPageModal({ page, onClose, onSave, submitting }: Props) {
  const [title, setTitle] = useState(page.title);

  useEffect(() => {
    setTitle(page.title);
  }, [page.id]);

  const submit = () => {
    const trimmed = title.trim();
    if (!trimmed) return;
    onSave({ title: trimmed });
  };

  return (
    <NotesModal
      onClose={onClose}
      title="Rename page"
      icon="fa-pen-to-square"
      width="min(440px, 100%)"
      busy={submitting}
      footer={
        <>
          <button type="button" onClick={onClose} style={btnGhostStyle}>
            Cancel
          </button>
          <button
            type="button"
            onClick={submit}
            disabled={!title.trim() || submitting}
            style={{
              ...btnPrimaryStyle,
              opacity: !title.trim() || submitting ? 0.5 : 1,
              cursor: !title.trim() || submitting ? "not-allowed" : "pointer"
            }}
          >
            <i className="fa fa-check" style={{ fontSize: 10, marginRight: 6 }} />
            {submitting ? "Saving…" : "Rename"}
          </button>
        </>
      }
    >
      <TextInput
        label="Title"
        // Focus lands here rather than on the close button: the title is the
        // only thing this dialog edits.
        data-autofocus
        value={title}
        onChange={(e) => setTitle(e.currentTarget.value)}
        onKeyDown={(e) => {
          // Escape is handled by the dialog itself; only Enter-to-submit is
          // this field's business.
          if (e.key === "Enter") submit();
        }}
        styles={notesInputStyles}
      />
    </NotesModal>
  );
}
