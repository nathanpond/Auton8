import { api } from "@/api/client";
import type { YjsThreadStore } from "@blocknote/core/yjs";

type CommentEventType = "created" | "replied" | "resolved" | "reopened" | "deleted";

interface CommentEventBody {
  documentName: string;
  threadId: string;
  commentId?: string;
  eventType: CommentEventType;
}

// Best-effort fire-and-forget. .NET drops the event onto content.events
// alongside the existing PageUpdated webhook event. Failures are logged
// but don't surface to the user — the comment write itself already
// succeeded by the time we get here.
async function postCommentEvent(body: CommentEventBody): Promise<void> {
  try {
    await api.post("/api/yjs/comment-event", body);
  } catch (err) {
    // eslint-disable-next-line no-console
    console.warn(
      `[yjs] comment-event ${body.eventType} for ${body.documentName}/${body.threadId} failed:`,
      err
    );
  }
}

// Wraps a YjsThreadStore so successful comment writes also fire a
// granular audit event. The proxy intercepts only the methods that map
// onto our content.events catalog; everything else delegates untouched.
//
// `documentName` is the Yjs doc identifier (`page:<guid>` or
// `note:<guid>`); .NET resolves it back to the parent pageId so audit
// events stay page-scoped.
export function wrapThreadStoreWithAuditing<T extends YjsThreadStore>(
  store: T,
  documentName: string
): T {
  return new Proxy(store, {
    get(target, prop, receiver) {
      const original = Reflect.get(target, prop, receiver);
      if (typeof original !== "function") return original;

      switch (prop) {
        case "createThread":
          return async (
            opts: Parameters<YjsThreadStore["createThread"]>[0]
          ) => {
            const thread = await original.call(target, opts);
            void postCommentEvent({
              documentName,
              threadId: thread.id,
              commentId: thread.comments[0]?.id,
              eventType: "created"
            });
            return thread;
          };
        case "addComment":
          return async (
            opts: Parameters<YjsThreadStore["addComment"]>[0]
          ) => {
            const comment = await original.call(target, opts);
            void postCommentEvent({
              documentName,
              threadId: opts.threadId,
              commentId: comment.id,
              eventType: "replied"
            });
            return comment;
          };
        case "resolveThread":
          return async (
            opts: Parameters<YjsThreadStore["resolveThread"]>[0]
          ) => {
            await original.call(target, opts);
            void postCommentEvent({
              documentName,
              threadId: opts.threadId,
              eventType: "resolved"
            });
          };
        case "unresolveThread":
          return async (
            opts: Parameters<YjsThreadStore["unresolveThread"]>[0]
          ) => {
            await original.call(target, opts);
            void postCommentEvent({
              documentName,
              threadId: opts.threadId,
              eventType: "reopened"
            });
          };
        case "deleteThread":
          return async (
            opts: Parameters<YjsThreadStore["deleteThread"]>[0]
          ) => {
            await original.call(target, opts);
            void postCommentEvent({
              documentName,
              threadId: opts.threadId,
              eventType: "deleted"
            });
          };
        case "deleteComment":
          return async (
            opts: Parameters<YjsThreadStore["deleteComment"]>[0]
          ) => {
            await original.call(target, opts);
            void postCommentEvent({
              documentName,
              threadId: opts.threadId,
              commentId: opts.commentId,
              eventType: "deleted"
            });
          };
        default:
          return original.bind(target);
      }
    }
  });
}
