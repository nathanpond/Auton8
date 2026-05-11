import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { Alert, Badge, Box, Button, Card, Code, Grid, Group, Switch, Text, TextInput, Title } from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import { Form, SaveFormRequest } from "@/api/forms";
import {
  useForm as useFormQuery,
  usePublishForm,
  useSaveForm
} from "@/hooks/useForms";
import JsxCodeEditor from "@/components/JsxCodeEditor";
import FormVersions from "./FormVersions";
import "./FormEditor.css";

type Buffer = {
  name: string;
  shortCode: string;
  formCode: string;
  siteAvailable: boolean;
};

const DEFAULT_DEV_PROPS = `{
  "data": {},
  "mode": "edit"
}
`;

export default function FormEditor() {
  const { id } = useParams<{ id: string }>();
  const { data: form, isLoading, error } = useFormQuery(id ?? null);
  const save = useSaveForm();
  const publish = usePublishForm();

  const [buffer, setBuffer] = useState<Buffer | null>(null);
  const [devPropsRaw, setDevPropsRaw] = useState(DEFAULT_DEV_PROPS);
  const [devPropsOpen, setDevPropsOpen] = useState(false);
  const [versionsOpen, setVersionsOpen] = useState(false);
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(
    null
  );

  // Hydrate the buffer once the form loads (or after a save/restore that
  // changes the canonical shape). Also restore any per-form dev-props from
  // localStorage so reopening the editor doesn't lose them.
  useEffect(() => {
    if (!form) return;
    setBuffer({
      name: form.name,
      shortCode: form.shortCode,
      formCode: form.formCode,
      siteAvailable: form.siteAvailable
    });
    const stored = readDevPropsFromStorage(form.shortCode);
    if (stored !== null) setDevPropsRaw(stored);
  }, [form?.id, form?.draftVersionNumber, form?.publishedVersionNumber]);

  // Persist dev-props per shortCode so the /formdev/<shortCode> tab can
  // pick them up via the storage event and re-render with the supplied
  // props. Editor and dev tab share the same browser; localStorage is the
  // cheapest cross-tab channel without a backend round-trip.
  useEffect(() => {
    if (!form) return;
    writeDevPropsToStorage(form.shortCode, devPropsRaw);
  }, [form?.shortCode, devPropsRaw]);

  const isDirty = useMemo(() => {
    if (!form || !buffer) return false;
    return (
      form.name !== buffer.name ||
      form.shortCode !== buffer.shortCode ||
      form.formCode !== buffer.formCode ||
      form.siteAvailable !== buffer.siteAvailable
    );
  }, [form, buffer]);

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
  if (error || !form || !buffer) {
    return (
      <Alert color="red" variant="light">
        <strong>Failed to load form.</strong>{" "}
        <Link to="/admin/config/forms">Back to list</Link>
      </Alert>
    );
  }

  const onSave = async () => {
    if (!buffer) return;
    const request: SaveFormRequest = {
      name: buffer.name.trim(),
      shortCode: buffer.shortCode.trim().toLowerCase(),
      formCode: buffer.formCode,
      siteAvailable: buffer.siteAvailable
    };
    try {
      await save.mutateAsync({ id: form.id, request });
      setFlash({ kind: "success", message: "Saved." });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const onPublish = async () => {
    if (isDirty) {
      if (
        !window.confirm(
          "You have unsaved changes. Save and publish? (Saving first is recommended so the published version matches the editor.)"
        )
      ) {
        return;
      }
      await onSave();
    }
    try {
      await publish.mutateAsync(form.id);
      setFlash({ kind: "success", message: "Published." });
    } catch (err) {
      setFlash({ kind: "error", message: describeError(err) });
    }
  };

  const devUrl = `/formdev/${encodeURIComponent(form.shortCode)}`;
  const liveUrl = `/form/${encodeURIComponent(form.shortCode)}`;

  return (
    <div className="form-editor">
      <PageHeader
        title={form.name || "Untitled form"}
        description={
          <Group gap={6} wrap="wrap">
            <Code>{form.shortCode}</Code>
            <span>· Draft v{form.draftVersionNumber}</span>
            {form.publishedVersionNumber !== null && (
              <span>· Published v{form.publishedVersionNumber}</span>
            )}
            <StatusChip form={form} dirty={isDirty} />
          </Group>
        }
        actions={
          <Group gap="xs" wrap="wrap">
            <Button
              component={Link}
              to="/admin/config/forms"
              variant="default"
              leftSection={<i className="fa fa-chevron-left" />}
            >
              Back
            </Button>
            <Button
              variant="default"
              leftSection={<i className="fa fa-clock-rotate-left" />}
              onClick={() => setVersionsOpen(true)}
            >
              Versions
            </Button>
            <Button
              component="a"
              href={devUrl}
              target="_blank"
              rel="noreferrer"
              variant="default"
              leftSection={<i className="fa fa-arrow-up-right-from-square" />}
            >
              Open dev
            </Button>
            {form.publishedVersionNumber !== null && form.siteAvailable && (
              <Button
                component="a"
                href={liveUrl}
                target="_blank"
                rel="noreferrer"
                variant="default"
                leftSection={<i className="fa fa-globe" />}
              >
                Open live
              </Button>
            )}
            <Button
              onClick={onSave}
              loading={save.isPending}
              disabled={!isDirty}
              leftSection={<i className="fa fa-floppy-disk" />}
            >
              Save
            </Button>
            <Button
              color="green"
              onClick={onPublish}
              loading={publish.isPending}
              leftSection={<i className="fa fa-rocket" />}
            >
              Publish
            </Button>
          </Group>
        }
      />

      {flash && (
        <Alert
          color={flash.kind === "success" ? "green" : "red"}
          variant="light"
          role={flash.kind === "success" ? "status" : "alert"}
          mb="sm"
        >
          {flash.message}
        </Alert>
      )}

      <Card withBorder shadow="sm" mb="md">
        <Title order={5} mb="md">
          Metadata
        </Title>
        <Grid>
          <Grid.Col span={{ base: 12, md: 7 }}>
            <TextInput
              label="Name"
              value={buffer.name}
              onChange={(e) => setBuffer({ ...buffer, name: e.currentTarget.value })}
            />
          </Grid.Col>
          <Grid.Col span={{ base: 12, md: 5 }}>
            <TextInput
              label="Short code"
              styles={{ input: { textTransform: "lowercase" } }}
              value={buffer.shortCode}
              onChange={(e) => setBuffer({ ...buffer, shortCode: e.currentTarget.value })}
            />
          </Grid.Col>
        </Grid>
        <Switch
          id="form-site-available"
          mt="md"
          checked={buffer.siteAvailable}
          onChange={(e) => setBuffer({ ...buffer, siteAvailable: e.currentTarget.checked })}
          label={
            <>
              Site-available (visible at <code>/form/{buffer.shortCode || "<short-code>"}</code>{" "}
              once published)
            </>
          }
        />
      </Card>

      <div
        className={`form-editor-layout${devPropsOpen ? " form-editor-layout--sidebar-open" : ""}`}
      >
        <section className="form-editor-main">
          <Box
            px="md"
            py="sm"
            style={{
              borderBottom: "1px solid var(--mantine-color-default-border)",
              background: "var(--mantine-color-default-hover)"
            }}
          >
            <Title order={5} m={0}>
              Form code (JSX)
            </Title>
          </Box>
          <div className="form-editor-canvas">
            <div className="form-editor-code">
              <JsxCodeEditor
                value={buffer.formCode}
                onChange={(v) => setBuffer({ ...buffer, formCode: v })}
                language="jsx"
                height="100%"
                placeholder={
                  "function Page({ data, onChange, onSubmit }) {\n" +
                  "  return <div>Hello</div>;\n" +
                  "}"
                }
              />
            </div>

            <div
              className="form-editor-rail"
              role="tablist"
              aria-orientation="vertical"
            >
              <button
                type="button"
                role="tab"
                aria-selected={devPropsOpen}
                className={`form-editor-rail-btn${devPropsOpen ? " is-active" : ""}`}
                onClick={() => setDevPropsOpen(true)}
                title="Dev props"
                aria-label="Open dev props"
              >
                <i className="fa fa-code" aria-hidden="true"></i>
              </button>
            </div>
          </div>
        </section>

        {devPropsOpen && (
          <aside
            className="form-editor-sidebar"
            role="region"
            aria-label="Dev props"
          >
            <div className="form-editor-sidebar-header">
              <h2 className="form-editor-sidebar-title">
                <i className="fa fa-code" aria-hidden="true"></i>
                <span>Dev props</span>
              </h2>
              <button
                type="button"
                className="form-editor-collapse-btn"
                onClick={() => setDevPropsOpen(false)}
                aria-label="Collapse dev props"
                title="Collapse"
              >
                <i className="fa fa-angles-right" aria-hidden="true"></i>
              </button>
            </div>
            <div className="form-editor-sidebar-body">
              <Text size="sm" c="dimmed" mb="xs">
                JSON forwarded to <code>Page(props)</code> when the form renders at{" "}
                <code>/formdev/{form.shortCode}</code>. Standard keys: <code>data</code>,{" "}
                <code>onChange</code>, <code>onSubmit</code>, <code>mode</code>,{" "}
                <code>context</code>.
              </Text>
              <div className="form-editor-sidebar-editor">
                <JsxCodeEditor
                  value={devPropsRaw}
                  onChange={setDevPropsRaw}
                  language="json"
                  height="100%"
                />
              </div>
            </div>
          </aside>
        )}
      </div>

      {versionsOpen && (
        <FormVersions
          formId={form.id}
          onClose={() => setVersionsOpen(false)}
          onRestored={() => {
            setVersionsOpen(false);
            setFlash({ kind: "success", message: "Restored — buffer reloaded." });
          }}
        />
      )}
    </div>
  );
}

function StatusChip({ form, dirty }: { form: Form; dirty: boolean }) {
  if (dirty)
    return (
      <Badge color="yellow" variant="filled" ml={8}>
        Unsaved
      </Badge>
    );
  if (form.publishedVersionNumber === null) {
    return (
      <Badge color="gray" variant="filled" ml={8}>
        Draft
      </Badge>
    );
  }
  if (form.isDraft) {
    return (
      <Badge color="yellow" variant="filled" ml={8}>
        Has unpublished changes
      </Badge>
    );
  }
  return form.siteAvailable ? (
    <Badge color="green" variant="filled" ml={8}>
      Live
    </Badge>
  ) : (
    <Badge color="green" variant="filled" ml={8}>
      Published
    </Badge>
  );
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { reason?: string } } }).response;
    return response?.data?.reason ?? error.message;
  }
  return String(error);
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

function writeDevPropsToStorage(shortCode: string, raw: string): void {
  try {
    window.localStorage.setItem(devPropsStorageKey(shortCode), raw);
  } catch {
    /* localStorage may be disabled (private mode); silently skip. */
  }
}
