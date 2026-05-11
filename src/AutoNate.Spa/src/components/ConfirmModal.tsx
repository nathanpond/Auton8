import { useEffect, useRef } from "react";
import { Button, Group, Modal, Stack } from "@mantine/core";

type ConfirmVariant = "danger" | "warning" | "primary";

type Props = {
  title: string;
  message: React.ReactNode;
  confirmLabel?: string;
  cancelLabel?: string;
  variant?: ConfirmVariant;
  busy?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
};

const variantColor: Record<ConfirmVariant, string | undefined> = {
  danger: "red",
  warning: "yellow",
  primary: undefined
};

// Lightweight confirmation modal used in place of window.confirm. Mantine
// Modal handles focus trap, Escape, and backdrop click; the caller owns
// mount/unmount.
export default function ConfirmModal({
  title,
  message,
  confirmLabel = "Confirm",
  cancelLabel = "Cancel",
  variant = "primary",
  busy = false,
  onConfirm,
  onCancel
}: Props) {
  const confirmRef = useRef<HTMLButtonElement | null>(null);

  useEffect(() => {
    confirmRef.current?.focus();
  }, []);

  return (
    <Modal
      opened
      onClose={onCancel}
      title={title}
      closeOnClickOutside={!busy}
      closeOnEscape={!busy}
      withCloseButton={!busy}
      centered
    >
      <Stack gap="md">
        <div>{message}</div>
        <Group justify="flex-end" gap="xs">
          <Button variant="default" onClick={onCancel} disabled={busy}>
            {cancelLabel}
          </Button>
          <Button
            ref={confirmRef}
            color={variantColor[variant]}
            onClick={onConfirm}
            loading={busy}
          >
            {confirmLabel}
          </Button>
        </Group>
      </Stack>
    </Modal>
  );
}
