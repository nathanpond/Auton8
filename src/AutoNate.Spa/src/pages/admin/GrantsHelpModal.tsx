import { useEffect } from "react";
import { useRegistry } from "@/hooks/useAdmin";

type Props = {
  onClose: () => void;
};

// Detailed reference for the unified permissions page. The lists of actions
// and selector tags are pulled from /api/admin/registry so they always match
// what the server actually understands.
export default function GrantsHelpModal({ onClose }: Props) {
  const { data } = useRegistry();
  const kinds = data?.kinds ?? [];

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
              <h5 className="modal-title">How permissions work</h5>
              <button type="button" className="btn-close" aria-label="Close" onClick={onClose} />
            </div>
            <div className="modal-body">
              <p>
                Every permission in the system lives in one place — a <strong>grant</strong>{" "}
                that says <em>who</em> can do <em>what</em> on <em>which</em> things, and
                whether to <em>allow</em> or <em>deny</em> it.
              </p>

              <hr />

              <h6>1. Principal — who the grant applies to</h6>
              <p>The "Principal kind" + "Principal" pair is the <em>who</em>. You have three options:</p>
              <ul>
                <li>
                  <strong>user</strong> — applies only to that one person.
                </li>
                <li>
                  <strong>group</strong> — applies to every member of that group. Manage
                  membership on the <code>Groups</code> page.
                </li>
                <li>
                  <strong>role</strong> — applies to every user who is assigned that role,
                  whether they got the role directly or through a group. Manage role
                  assignments on the <code>Roles</code> page.
                </li>
              </ul>
              <p>
                A user's effective grants are the union of everything attached to them
                directly, to any group they belong to, and to any role they're assigned —
                directly or via a group.
              </p>

              <hr />

              <h6>2. Action — what they can do</h6>
              <p>
                A free-form lowercase verb that the endpoint will compare against. Use{" "}
                <code>*</code> for "any action." The vocabulary is per-entity-kind; here's
                what each kind currently understands:
              </p>
              <table className="table table-sm">
                <thead>
                  <tr>
                    <th>Kind</th>
                    <th>Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {kinds.map((k) => (
                    <tr key={k.kind}>
                      <td className="font-monospace">{k.kind}</td>
                      <td className="font-monospace">{k.actions.join(", ")}</td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <hr />

              <h6>3. Selector — which things the action applies to</h6>
              <p>
                A small path-like grammar that picks a set of entities. The visual builder
                handles the common cases; you can flip to "edit raw" for anything
                advanced.
              </p>

              <p className="mb-1"><strong>Shape:</strong></p>
              <pre className="bg-light p-2 mb-2 small">{`/<kind>/<ids>[<tag>=<value>;<tag>=<value>...]`}</pre>

              <p className="mb-1"><strong>Path part — kind and ids:</strong></p>
              <ul>
                <li>
                  <code>/record/*</code> — every record (wildcard id).
                </li>
                <li>
                  <code>/record/&lt;guid&gt;</code> — exactly that one record.
                </li>
                <li>
                  <code>/record/{"{a,b,c}"}</code> — those three specific record ids.
                </li>
                <li>
                  <code>/group/*</code>, <code>/role/*</code>, etc. — same shape for any
                  registered kind.
                </li>
              </ul>

              <p className="mb-1"><strong>Predicate part — tag filters in <code>[…]</code>:</strong></p>
              <ul>
                <li>
                  <code>[recordtype=lead]</code> — literal value, matched by short_code or
                  the kind's defined tag column.
                </li>
                <li>
                  <code>[assignee=user]</code> — the bare word <code>user</code> resolves
                  to the current actor at evaluation time.
                </li>
                <li>
                  <code>[assignee=user/&lt;guid&gt;]</code> — pin the value to a specific
                  user.
                </li>
                <li>
                  Combine with <code>;</code>: <code>[recordtype=lead;assignee=user]</code>{" "}
                  — both must match.
                </li>
              </ul>

              <p className="mb-1"><strong>Tags supported per kind:</strong></p>
              <table className="table table-sm">
                <thead>
                  <tr>
                    <th>Kind</th>
                    <th>Tags</th>
                  </tr>
                </thead>
                <tbody>
                  {kinds.map((k) => (
                    <tr key={k.kind}>
                      <td className="font-monospace">{k.kind}</td>
                      <td className="font-monospace">
                        {k.tags.length === 0 ? <em>(path filtering only)</em> : k.tags.join(", ")}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>

              <p className="mb-1"><strong>Multi-hop / supervisor pattern:</strong></p>
              <p>
                Tag values that resolve to a user can be followed by another bracketed
                predicate to walk one more edge through the user-to-user graph. The
                outer <code>=user</code> stops meaning "the actor" — instead it's "some
                user, constrained by the inner predicate." The inner predicate then
                walks an edge from the actor to that user.
              </p>
              <p className="mb-1">
                Concrete example: every supervisor sees records and workflow executions
                attributed to people they supervise.
              </p>
              <pre className="bg-light p-2 mb-2 small">{`# records assigned to anyone the actor supervises
/record/*[assignee=user[supervisor=user]]

# workflow executions started by anyone the actor supervises
/workflowexecution/*[startedby=user[supervisor=user]]`}</pre>
              <p>
                Each user evaluating the same selector sees a different result set —
                their own supervisees. To make this work you need two things in place:
              </p>
              <ul>
                <li>
                  <strong>Supervisor edges</strong> — declare who supervises whom.
                  Today this is set per-user via{" "}
                  <code>PUT /api/users/{`{userId}`}/supervisor</code> with body{" "}
                  <code>{`{ "supervisorUserId": "<guid>" }`}</code>. Each user has at
                  most one supervisor; passing <code>null</code> clears it.
                </li>
                <li>
                  <strong>The data the inner predicate walks.</strong> For
                  <code> records </code>(SQL-backed) the engine walks the{" "}
                  <code>entity_edges</code> table directly. For{" "}
                  <code>workflowexecution</code> and <code>workflowtask</code>{" "}
                  (Flowable-backed, evaluated in memory) the engine pre-loads the
                  actor's outbound user→user edges per request, so each new login
                  picks up hierarchy changes immediately.
                </li>
              </ul>
              <p className="mb-1">
                Constraints to know:
              </p>
              <ul>
                <li>Only two-hop nesting is supported (no triple nesting).</li>
                <li>
                  The inner predicate must be a single <code>&lt;edgeKind&gt;=user</code>{" "}
                  expression. Other shapes are rejected with a clear error.
                </li>
                <li>
                  Multi-level transitive supervision (your supervisee's supervisee)
                  isn't included — only the direct relationship counts.
                </li>
              </ul>

              <hr />

              <h6>4. Effect — allow or deny</h6>
              <ul>
                <li>
                  <strong>allow</strong> — grants access. A user must have at least one
                  matching <code>allow</code> grant to be permitted. With no allows, access
                  is closed by default.
                </li>
                <li>
                  <strong>deny</strong> — blocks access. <em>Deny always wins:</em> if any
                  matching deny exists, the user is denied even if other allows match.
                  Useful for carving exceptions out of broad allows.
                </li>
              </ul>

              <p className="mb-1"><strong>Combination rule:</strong></p>
              <pre className="bg-light p-2 mb-2 small">{`final = OR(matching allows) AND NOT OR(matching denies)`}</pre>

              <hr />

              <h6>5. Priority</h6>
              <p>
                An integer field that's currently informational only — the engine resolves
                conflicts purely via the deny-wins rule above. It's stored so future
                tooling (sorting, override-precedence schemes) can use it without a
                schema change.
              </p>

              <hr />

              <h6>6. Examples</h6>
              <p className="mb-1">
                <strong>Everyone in Sales can view leads.</strong>
              </p>
              <pre className="bg-light p-2 small">{`Principal kind: group
Principal:      Sales
Action:         view
Selector:       /record/*[recordtype=lead]
Effect:         allow`}</pre>

              <p className="mb-1">
                <strong>Alice can edit any record assigned to her.</strong>
              </p>
              <pre className="bg-light p-2 small">{`Principal kind: user
Principal:      alice
Action:         edit
Selector:       /record/*[assignee=user]
Effect:         allow`}</pre>

              <p className="mb-1">
                <strong>Editors role can edit everything except confidential records.</strong>
              </p>
              <pre className="bg-light p-2 small">{`# Allow on the role
Principal kind: role
Principal:      Editors
Action:         edit
Selector:       /record/*
Effect:         allow

# Deny carve-out on the same role
Principal kind: role
Principal:      Editors
Action:         edit
Selector:       /record/*[recordtype=confidential]
Effect:         deny`}</pre>

              <p className="mb-1">
                <strong>QA group can complete only their own workflow tasks.</strong>
              </p>
              <pre className="bg-light p-2 small">{`Principal kind: group
Principal:      QA
Action:         complete
Selector:       /workflowtask/*[assignee=user]
Effect:         allow`}</pre>

              <p className="mb-1">
                <strong>
                  Workflow Operators can cancel running executions, but only
                  Admins can delete the historical record.
                </strong>{" "}
                On a workflow execution, <code>cancel</code> halts a running
                process and marks it cancelled (history kept), while{" "}
                <code>delete</code> wipes the execution from Flowable entirely
                — both runtime and history. Treat them as separate
                permissions.
              </p>
              <pre className="bg-light p-2 small">{`# Operators can cancel any execution
Principal kind: role
Principal:      WorkflowOperator
Action:         cancel
Selector:       /workflowexecution/*
Effect:         allow

# Only Admins can wipe the historical record
Principal kind: role
Principal:      Admin
Action:         delete
Selector:       /workflowexecution/*
Effect:         allow`}</pre>

              <p className="mb-1">
                <strong>
                  Managers see records and executions attributed to the people
                  they supervise.
                </strong>
              </p>
              <pre className="bg-light p-2 small">{`# Set up the hierarchy (one-time, per supervisee):
PUT /api/users/<supervisee-guid>/supervisor
{ "supervisorUserId": "<manager-guid>" }

# Two grants on the Manager role:
Principal kind: role
Principal:      Manager
Action:         view
Selector:       /record/*[assignee=user[supervisor=user]]
Effect:         allow

Principal kind: role
Principal:      Manager
Action:         view
Selector:       /workflowexecution/*[startedby=user[supervisor=user]]
Effect:         allow`}</pre>

              <hr />

              <h6>7. Workflow</h6>
              <ol>
                <li>Pick a principal kind, then the principal.</li>
                <li>
                  Type or pick the action (or <code>*</code> for any). Use the action list
                  above as a reference.
                </li>
                <li>Use the visual builder for the selector; flip to raw for nesting/quoting.</li>
                <li>Choose <code>allow</code> or <code>deny</code>.</li>
                <li>Click <strong>Add</strong> — the grant takes effect on the next request.</li>
                <li>To remove a grant, click <strong>Revoke</strong> in its row in the table below.</li>
              </ol>

              <hr />

              <h6>8. Behavior notes</h6>
              <ul>
                <li>
                  <strong>SuperAdmin bypasses everything.</strong> Members of the built-in
                  SuperAdmin role pass every check; their grants don't matter.
                </li>
                <li>
                  <strong>Authorization must be enabled to enforce.</strong> When the
                  feature flag is off, all grants are stored but ignored. Flip{" "}
                  <code>Authorization:Enabled</code> + <code>Enforcement</code> in app
                  settings to turn enforcement on.
                </li>
                <li>
                  <strong>Cache invalidation is automatic.</strong> Adding or revoking a
                  grant bumps the auth cache version, so the change is visible on the
                  caller's next request.
                </li>
                <li>
                  <strong>Closed by default.</strong> A user with no matching allow grant
                  for a given (kind, action) sees nothing and is denied — even with no
                  deny rules in place.
                </li>
                <li>
                  <strong>Debug a real decision.</strong> The{" "}
                  <code>Effective Permissions</code> page replays the evaluator for any
                  user, action, and target — it shows the final allow/deny and which
                  grants matched (or didn't), so you can answer "why does Alice get a
                  403?" without guessing.
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
