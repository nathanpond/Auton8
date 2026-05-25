import { useMemo } from "react";
import { useQueryClient } from "@tanstack/react-query";
import { useBusSubscription } from "@/hooks/useBusSubscription";
import { SubscriptionEvent } from "@/lib/ws/subscription";
import { PROJECTS_QUERY_KEY } from "@/hooks/useContent";

// Subscribes to project/cabinet bus channels for the currently-selected
// scope in NotesPage and invalidates the matching react-query caches so
// the explorer rail refreshes without a manual page reload.
//
// Channel layout (server-side fan-out lives in ContentChannelResolver):
//   project:{projectId}  — fires for any cabinet/notebook/page/note event
//                          whose ancestor closure includes this project.
//                          We subscribe so newly-created cabinets land in
//                          the rail without a refresh.
//   cabinet:{cabinetId}  — fires for any notebook/page/note event under
//                          this cabinet. Drives notebook + page-tree
//                          refreshes inside the selected cabinet.
//
// Server-side IContentAuthorizer gates subscribe and per-message delivery
// so a viewer who can't see the project/cabinet never receives the frame —
// no client-side filtering needed.
//
// Invalidation is intentionally a little broad: we invalidate by the
// query-key prefix that matches the event's resourceKind, rather than
// surgically targeting (cabinet) → (notebook list of that cabinet). The
// active explorer has at most a handful of cabinets/notebooks loaded, so
// refetching all of them on a relevant event is cheap and stays correct
// even when the event payload omits a parent id.
export function useNotesLiveUpdates(
  projectId: string | null,
  cabinetId: string | null
): void {
  const qc = useQueryClient();
  const channels = useMemo(() => {
    const list: string[] = [];
    if (projectId) list.push(`project:${projectId}`);
    if (cabinetId) list.push(`cabinet:${cabinetId}`);
    return list;
  }, [projectId, cabinetId]);

  useBusSubscription(
    channels,
    (event) => {
      if (event.type !== "event") return;
      const parsed = tryParsePayload(event.payload);
      if (!parsed) return;
      handleContentEvent(parsed, qc);
    },
    { enabled: channels.length > 0 }
  );
}

type ContentEventEnvelope = {
  resourceKind?: string;
  resource?: Record<string, unknown>;
  details?: Record<string, unknown>;
};

function tryParsePayload(raw: string): ContentEventEnvelope | null {
  try {
    const parsed = JSON.parse(raw);
    return parsed && typeof parsed === "object" ? (parsed as ContentEventEnvelope) : null;
  } catch {
    return null;
  }
}

function handleContentEvent(
  event: ContentEventEnvelope,
  qc: ReturnType<typeof useQueryClient>
): void {
  const kind = event.resourceKind;
  if (!kind) return;
  // Resource-kind buckets follow the constants in ContentResourceKinds.
  // page.version / page.attachment etc. don't change the tree itself —
  // they affect history/attachment side panels and don't need a tree
  // invalidation. We invalidate only the panels that actually drive UI
  // for the explorer rail and editor pane.
  if (kind === "project") {
    qc.invalidateQueries({ queryKey: PROJECTS_QUERY_KEY });
    return;
  }
  if (kind === "cabinet") {
    qc.invalidateQueries({ queryKey: ["content", "cabinets"] });
    return;
  }
  if (kind === "notebook") {
    qc.invalidateQueries({ queryKey: ["content", "notebooks"] });
    return;
  }
  if (kind === "page") {
    qc.invalidateQueries({ queryKey: ["content", "page-tree"] });
    return;
  }
  if (kind === "note") {
    qc.invalidateQueries({ queryKey: ["content", "notes"] });
    return;
  }
  // page.version / note.version / page.attachment / comment / project.member
  // — no rail/editor-pane impact, skip.
}
