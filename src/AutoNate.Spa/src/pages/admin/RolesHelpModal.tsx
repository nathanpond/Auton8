import { useEffect } from "react";
import { Link } from "react-router-dom";

type Props = {
  onClose: () => void;
};

// Reference for the Roles admin page. Roles are containers; the actual
// permissions a role conveys live in permission_grants (principal_kind='role')
// and are managed on the Permissions page. This modal explains the model so a
// new admin doesn't have to piece it together from two pages.
export default function RolesHelpModal({ onClose }: Props) {
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") onClose();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [onClose]);

  return (
    <>
      <div className="modal fade show d-block" tabIndex={-1} role="dialog" aria-modal="true">
        <div className="modal-dialog modal-lg modal-dialog-scrollable">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">How roles work</h5>
              <button type="button" className="btn-close" aria-label="Close" onClick={onClose} />
            </div>
            <div className="modal-body">
              <p>
                A <strong>role</strong> is a named handle you attach permissions to and
                hand out to people. By itself it does nothing — it gains meaning when
                you (a) attach permission rules to it on the{" "}
                <Link to="/admin/grants">Permissions</Link> page and (b) assign it to
                users or groups on this page.
              </p>

              <hr />

              <h6>1. The three-step model</h6>
              <ol>
                <li>
                  <strong>Create the role</strong> here — give it a name like{" "}
                  <code>Editors</code> or <code>Sales</code>. Description is optional.
                </li>
                <li>
                  <strong>Attach permissions to the role</strong> on the{" "}
                  <Link to="/admin/grants">Permissions</Link> page. Pick principal
                  kind <code>role</code>, pick this role, then add as many{" "}
                  <code>(action, selector, allow|deny)</code> rows as you need. The role
                  itself is just a label; everything it lets people do lives in those
                  rows.
                </li>
                <li>
                  <strong>Assign the role</strong> to a user or group here. Anyone the
                  role is assigned to picks up every permission attached to that role
                  on their next request.
                </li>
              </ol>

              <hr />

              <h6>2. Creating and deleting roles</h6>
              <ul>
                <li>
                  Names are unique system-wide. Re-using a name returns a 400.
                </li>
                <li>
                  Built-in <strong>system roles</strong> (currently just{" "}
                  <code>SuperAdmin</code>) can't be renamed or deleted. The Delete
                  button is hidden for them.
                </li>
                <li>
                  Deleting a normal role <strong>cascades</strong>: all permission
                  grants attached to it are removed, and any role assignments referring
                  to it are removed too. Users currently relying on that role lose
                  access immediately.
                </li>
              </ul>

              <hr />

              <h6>3. Assignments</h6>
              <p>
                Click a role on the left to open its Assignments panel. Each row says
                "this role applies to that principal."
              </p>
              <ul>
                <li>
                  <strong>Principal kind</strong>: <code>user</code> or{" "}
                  <code>group</code>. (You can't assign a role to another role —
                  permissions on roles flow through the unified grants table.)
                </li>
                <li>
                  <strong>Principal</strong>: the specific user or group.
                </li>
                <li>
                  <strong>Scope</strong> (optional): a selector that further restricts
                  where this assignment applies. The grant graph stores it today, but
                  the evaluator doesn't yet narrow grants by per-assignment scope —
                  treat this field as <em>reserved for future use</em>. Leave it blank
                  for normal assignments.
                </li>
              </ul>

              <p>
                Click <strong>Revoke</strong> to remove an assignment. The user/group
                loses access on the next request.
              </p>

              <hr />

              <h6>4. How a user's effective permissions are computed</h6>
              <p>
                When a user makes a request, the evaluator unions every grant that
                reaches them through any of these chains:
              </p>
              <ul>
                <li>
                  <code>permission_grants</code> attached directly to <em>them</em>{" "}
                  (principal_kind = user).
                </li>
                <li>
                  <code>permission_grants</code> attached to a <em>group</em> they're a
                  member of.
                </li>
                <li>
                  <code>permission_grants</code> attached to a <em>role</em> they're
                  assigned — directly here, or indirectly via a group.
                </li>
              </ul>
              <p className="mb-1"><strong>Combination:</strong></p>
              <pre className="bg-light p-2 mb-2 small">{`final = OR(matching allows from any source) AND NOT OR(matching denies from any source)`}</pre>
              <p>
                Deny always wins. A deny on the role blocks the user even if their
                group has an allow.
              </p>

              <hr />

              <h6>5. SuperAdmin</h6>
              <ul>
                <li>
                  Built-in. Members bypass <em>every</em> authorization check; their
                  grants don't matter.
                </li>
                <li>
                  Be careful with the Assignments panel — you <em>can</em> revoke your
                  own SuperAdmin membership. If no one else has it either, you may
                  lock yourself out of the admin pages once enforcement is on.
                </li>
                <li>
                  On a fresh install with{" "}
                  <code>Authorization:AssignSuperAdminToAllExistingUsers=true</code>{" "}
                  (the default), every existing user gets SuperAdmin once. After
                  that, new users start with no roles and you grant them as needed.
                </li>
              </ul>

              <hr />

              <h6>6. Common patterns</h6>
              <p className="mb-1"><strong>Read-only role for a group of viewers:</strong></p>
              <ol>
                <li>Create role <code>Viewer</code>.</li>
                <li>
                  On Permissions: add <code>view</code> grant on{" "}
                  <code>/record/*</code>, <code>allow</code>, principal kind{" "}
                  <code>role</code>, principal <code>Viewer</code>.
                </li>
                <li>
                  Here: assign <code>Viewer</code> to your <code>Viewers</code> group.
                </li>
              </ol>

              <p className="mb-1"><strong>Per-user assignee role:</strong></p>
              <ol>
                <li>Create role <code>AssignedRecordHandler</code>.</li>
                <li>
                  On Permissions: add <code>view</code> + <code>edit</code> grants on{" "}
                  <code>/record/*[assignee=user]</code>, <code>allow</code>.
                </li>
                <li>
                  Here: assign the role to every user who handles their own records.
                  Each user gets the same selector — <code>user</code> resolves to the
                  caller, so they each see only their own.
                </li>
              </ol>

              <hr />

              <h6>7. Behavior notes</h6>
              <ul>
                <li>
                  <strong>Authorization must be on to enforce.</strong> While the
                  feature flag is off, role assignments are recorded but ignored.
                </li>
                <li>
                  <strong>Cache invalidation is automatic.</strong> Assigning or
                  revoking bumps the auth cache version; the change is visible on the
                  caller's next request.
                </li>
                <li>
                  <strong>Closed by default.</strong> A user with no assigned roles
                  and no direct/group grants for a given (kind, action) is denied —
                  even when no deny rules exist.
                </li>
                <li>
                  <strong>Roles can't nest.</strong> A role isn't a member of another
                  role. Compose access via groups + multiple role assignments instead.
                </li>
              </ul>
            </div>
            <div className="modal-footer">
              <button type="button" className="btn btn-primary" onClick={onClose}>
                Close
              </button>
            </div>
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" onClick={onClose} />
    </>
  );
}
