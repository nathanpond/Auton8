import { useEffect, useMemo, useState } from "react";
import { Link, useParams } from "react-router-dom";
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
      <div className="p-3 text-muted">
        <i className="fa fa-spinner fa-spin me-2" />
        Loading form…
      </div>
    );
  }
  if (error || !form || !buffer) {
    return (
      <div className="alert alert-danger">
        <strong>Failed to load form.</strong>{" "}
        <Link to="/admin/config/forms">Back to list</Link>
      </div>
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
      <div className="page-head d-flex flex-wrap gap-3 align-items-start justify-content-between">
        <div>
          <h1 className="page-header mb-1">{form.name || "Untitled form"}</h1>
          <p className="page-head-copy mb-0">
            <code>{form.shortCode}</code> · Draft v{form.draftVersionNumber}
            {form.publishedVersionNumber !== null && (
              <> · Published v{form.publishedVersionNumber}</>
            )}{" "}
            <StatusChip form={form} dirty={isDirty} />
          </p>
        </div>
        <div className="d-flex flex-wrap gap-2">
          <Link className="btn btn-outline-secondary" to="/admin/config/forms">
            <i className="fa fa-chevron-left me-1" /> Back
          </Link>
          <button
            type="button"
            className="btn btn-outline-secondary"
            onClick={() => setVersionsOpen(true)}
          >
            <i className="fa fa-clock-rotate-left me-1" /> Versions
          </button>
          <a
            className="btn btn-outline-secondary"
            href={devUrl}
            target="_blank"
            rel="noreferrer"
          >
            <i className="fa fa-arrow-up-right-from-square me-1" /> Open dev
          </a>
          {form.publishedVersionNumber !== null && form.siteAvailable && (
            <a
              className="btn btn-outline-secondary"
              href={liveUrl}
              target="_blank"
              rel="noreferrer"
            >
              <i className="fa fa-globe me-1" /> Open live
            </a>
          )}
          <button
            type="button"
            className="btn btn-primary"
            onClick={onSave}
            disabled={save.isPending || !isDirty}
          >
            <i className="fa fa-floppy-disk me-1" /> Save
          </button>
          <button
            type="button"
            className="btn btn-success"
            onClick={onPublish}
            disabled={publish.isPending}
          >
            <i className="fa fa-rocket me-1" /> Publish
          </button>
        </div>
      </div>

      {flash && (
        <div
          className={`alert ${flash.kind === "success" ? "alert-success" : "alert-danger"}`}
          role={flash.kind === "success" ? "status" : "alert"}
        >
          {flash.message}
        </div>
      )}

      <div className="panel panel-inverse mb-3">
        <div className="panel-heading">
          <h4 className="panel-title">Metadata</h4>
        </div>
        <div className="panel-body">
          <div className="row g-3">
            <div className="col-md-7">
              <label className="form-label">Name</label>
              <input
                className="form-control"
                value={buffer.name}
                onChange={(e) => setBuffer({ ...buffer, name: e.target.value })}
              />
            </div>
            <div className="col-md-5">
              <label className="form-label">Short code</label>
              <input
                className="form-control text-lowercase"
                value={buffer.shortCode}
                onChange={(e) =>
                  setBuffer({ ...buffer, shortCode: e.target.value })
                }
              />
            </div>
          </div>
          <div className="form-check form-switch mt-3">
            <input
              type="checkbox"
              className="form-check-input"
              id="form-site-available"
              checked={buffer.siteAvailable}
              onChange={(e) =>
                setBuffer({ ...buffer, siteAvailable: e.target.checked })
              }
            />
            <label className="form-check-label" htmlFor="form-site-available">
              Site-available (visible at <code>/form/{buffer.shortCode || "<short-code>"}</code>{" "}
              once published)
            </label>
          </div>
        </div>
      </div>

      <div
        className={`form-editor-layout${devPropsOpen ? " form-editor-layout--sidebar-open" : ""}`}
      >
        <section className="form-editor-main panel panel-inverse mb-0">
          <div className="panel-heading">
            <h4 className="panel-title">Form code (JSX)</h4>
          </div>
          <div className="panel-body p-0 form-editor-canvas">
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
                <i className="bi bi-braces" aria-hidden="true"></i>
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
                <i className="bi bi-braces" aria-hidden="true"></i>
                <span>Dev props</span>
              </h2>
              <button
                type="button"
                className="form-editor-collapse-btn"
                onClick={() => setDevPropsOpen(false)}
                aria-label="Collapse dev props"
                title="Collapse"
              >
                <i className="bi bi-chevron-double-right" aria-hidden="true"></i>
              </button>
            </div>
            <div className="form-editor-sidebar-body">
              <p className="form-text mb-2">
                JSON forwarded to <code>Page(props)</code> when the form renders at{" "}
                <code>/formdev/{form.shortCode}</code>. Standard keys: <code>data</code>,{" "}
                <code>onChange</code>, <code>onSubmit</code>, <code>mode</code>,{" "}
                <code>context</code>.
              </p>
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
  if (dirty) return <span className="badge bg-warning text-dark ms-2">Unsaved</span>;
  if (form.publishedVersionNumber === null) {
    return <span className="badge bg-secondary ms-2">Draft</span>;
  }
  if (form.isDraft) {
    return <span className="badge bg-warning text-dark ms-2">Has unpublished changes</span>;
  }
  return form.siteAvailable ? (
    <span className="badge bg-success ms-2">Live</span>
  ) : (
    <span className="badge bg-success ms-2">Published</span>
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
