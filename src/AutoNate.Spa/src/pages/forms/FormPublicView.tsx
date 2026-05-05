import { useParams } from "react-router-dom";
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
      <div className="p-4 text-muted">
        <i className="fa fa-spinner fa-spin me-2" />
        Loading form…
      </div>
    );
  }

  if (error) {
    return (
      <div className="p-4">
        <div className="alert alert-danger">
          Failed to load form: {(error as Error).message}
        </div>
      </div>
    );
  }

  if (!snapshot) {
    return (
      <div className="p-4">
        <div className="alert alert-warning">
          No published form available at <code>/form/{shortCode}</code>.
        </div>
      </div>
    );
  }

  return (
    <div className="form-public-view p-3">
      <JsxFormHost source={snapshot.formCode} mode="edit" />
    </div>
  );
}
