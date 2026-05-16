import { ThreadStoreAuth } from "@blocknote/core/comments";

// All-false implementation of BlockNote's ThreadStoreAuth. Used by
// useBlockNoteWithYjs when the connection role is "viewer" — every comment
// affordance (Add comment, reply, reaction, resolve, delete) is hidden so
// the user doesn't attempt a write the server would silently reject.
//
// BlockNote's Comment / Thread components consult these methods to decide
// which buttons to render. With every method returning false the UI
// degrades to a read-only thread display, preserving the ability to read
// existing comments while preventing any new writes.
//
// If BlockNote adds new methods to ThreadStoreAuth in a future version,
// our subclass loses TypeScript coverage and the base abstract throws.
// Pin or audit at upgrade time.
export class ReadOnlyThreadStoreAuth extends ThreadStoreAuth {
  canCreateThread(): boolean {
    return false;
  }
  canAddComment(): boolean {
    return false;
  }
  canUpdateComment(): boolean {
    return false;
  }
  canDeleteComment(): boolean {
    return false;
  }
  canDeleteThread(): boolean {
    return false;
  }
  canResolveThread(): boolean {
    return false;
  }
  canUnresolveThread(): boolean {
    return false;
  }
  canAddReaction(): boolean {
    return false;
  }
  canDeleteReaction(): boolean {
    return false;
  }
}
