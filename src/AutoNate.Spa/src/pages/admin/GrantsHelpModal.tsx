import { Box, Button, Code, Divider, Group, Modal, Table, Text, Title } from "@mantine/core";
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

  return (
    <Modal opened onClose={onClose} title="How permissions work" size="lg">
      <Box>
        <Text>
          Every permission in the system lives in one place — a <strong>grant</strong> that
          says <em>who</em> can do <em>what</em> on <em>which</em> things, and whether to{" "}
          <em>allow</em> or <em>deny</em> it.
        </Text>

        <Divider my="md" />

        <Title order={6}>1. Principal — who the grant applies to</Title>
        <Text>The &quot;Principal kind&quot; + &quot;Principal&quot; pair is the <em>who</em>. You have three options:</Text>
        <ul>
          <li><strong>user</strong> — applies only to that one person.</li>
          <li>
            <strong>group</strong> — applies to every member of that group. Manage membership
            on the <Code>Groups</Code> page.
          </li>
          <li>
            <strong>role</strong> — applies to every user who is assigned that role, whether
            they got the role directly or through a group. Manage role assignments on the{" "}
            <Code>Roles</Code> page.
          </li>
        </ul>
        <Text>
          A user&apos;s effective grants are the union of everything attached to them directly, to
          any group they belong to, and to any role they&apos;re assigned — directly or via a
          group.
        </Text>

        <Divider my="md" />

        <Title order={6}>2. Action — what they can do</Title>
        <Text>
          A free-form lowercase verb that the endpoint will compare against. Use <Code>*</Code>{" "}
          for &quot;any action.&quot; The vocabulary is per-entity-kind; here&apos;s what each kind currently
          understands:
        </Text>
        <Table withTableBorder withColumnBorders striped mt="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Kind</Table.Th>
              <Table.Th>Actions</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {kinds.map((k) => (
              <Table.Tr key={k.kind}>
                <Table.Td><Code>{k.kind}</Code></Table.Td>
                <Table.Td><Code>{k.actions.join(", ")}</Code></Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>

        <Divider my="md" />

        <Title order={6}>3. Selector — which things the action applies to</Title>
        <Text>
          A small path-like grammar that picks a set of entities. The visual builder handles
          the common cases; you can flip to &quot;edit raw&quot; for anything advanced.
        </Text>

        <Text mb={4}><strong>Shape:</strong></Text>
        <Code block mb="sm">{`/<kind>/<ids>[<tag>=<value>;<tag>=<value>...]`}</Code>

        <Text mb={4}><strong>Path part — kind and ids:</strong></Text>
        <ul>
          <li><Code>/record/*</Code> — every record (wildcard id).</li>
          <li><Code>{`/record/<guid>`}</Code> — exactly that one record.</li>
          <li><Code>{`/record/{a,b,c}`}</Code> — those three specific record ids.</li>
          <li><Code>/group/*</Code>, <Code>/role/*</Code>, etc. — same shape for any registered kind.</li>
        </ul>

        <Text mb={4}><strong>Predicate part — tag filters in <Code>[…]</Code>:</strong></Text>
        <ul>
          <li>
            <Code>[recordtype=lead]</Code> — literal value, matched by short_code or the
            kind&apos;s defined tag column.
          </li>
          <li>
            <Code>[assignee=user]</Code> — the bare word <Code>user</Code> resolves to the
            current actor at evaluation time.
          </li>
          <li><Code>{`[assignee=user/<guid>]`}</Code> — pin the value to a specific user.</li>
          <li>
            Combine with <Code>;</Code>: <Code>[recordtype=lead;assignee=user]</Code> — both
            must match.
          </li>
        </ul>

        <Text mb={4}><strong>Tags supported per kind:</strong></Text>
        <Table withTableBorder withColumnBorders striped mt="xs">
          <Table.Thead>
            <Table.Tr>
              <Table.Th>Kind</Table.Th>
              <Table.Th>Tags</Table.Th>
            </Table.Tr>
          </Table.Thead>
          <Table.Tbody>
            {kinds.map((k) => (
              <Table.Tr key={k.kind}>
                <Table.Td><Code>{k.kind}</Code></Table.Td>
                <Table.Td>
                  {k.tags.length === 0 ? <em>(path filtering only)</em> : <Code>{k.tags.join(", ")}</Code>}
                </Table.Td>
              </Table.Tr>
            ))}
          </Table.Tbody>
        </Table>

        <Text mt="md" mb={4}><strong>Multi-hop / supervisor pattern:</strong></Text>
        <Text>
          Tag values that resolve to a user can be followed by another bracketed predicate to
          walk one more edge through the user-to-user graph. The outer <Code>=user</Code>{" "}
          stops meaning &quot;the actor&quot; — instead it&apos;s &quot;some user, constrained by the inner
          predicate.&quot; The inner predicate then walks an edge from the actor to that user.
        </Text>
        <Text mb={4}>
          Concrete example: every supervisor sees records and workflow executions attributed
          to people they supervise.
        </Text>
        <Code block mb="sm">{`# records assigned to anyone the actor supervises
/record/*[assignee=user[supervisor=user]]

# workflow executions started by anyone the actor supervises
/workflowexecution/*[startedby=user[supervisor=user]]`}</Code>
        <Text>
          Each user evaluating the same selector sees a different result set — their own
          supervisees. To make this work you need two things in place:
        </Text>
        <ul>
          <li>
            <strong>Supervisor edges</strong> — declare who supervises whom. Today this is
            set per-user via <Code>{`PUT /api/users/{userId}/supervisor`}</Code> with body{" "}
            <Code>{`{ "supervisorUserId": "<guid>" }`}</Code>. Each user has at most one
            supervisor; passing <Code>null</Code> clears it.
          </li>
          <li>
            <strong>The data the inner predicate walks.</strong> For <Code>records</Code>{" "}
            (SQL-backed) the engine walks the <Code>entity_edges</Code> table directly. For{" "}
            <Code>workflowexecution</Code> and <Code>workflowtask</Code> (Flowable-backed,
            evaluated in memory) the engine pre-loads the actor&apos;s outbound user→user edges
            per request, so each new login picks up hierarchy changes immediately.
          </li>
        </ul>
        <Text mb={4}>Constraints to know:</Text>
        <ul>
          <li>Only two-hop nesting is supported (no triple nesting).</li>
          <li>
            The inner predicate must be a single <Code>{`<edgeKind>=user`}</Code> expression.
            Other shapes are rejected with a clear error.
          </li>
          <li>
            Multi-level transitive supervision (your supervisee&apos;s supervisee) isn&apos;t included
            — only the direct relationship counts.
          </li>
        </ul>

        <Divider my="md" />

        <Title order={6}>4. Effect — allow or deny</Title>
        <ul>
          <li>
            <strong>allow</strong> — grants access. A user must have at least one matching{" "}
            <Code>allow</Code> grant to be permitted. With no allows, access is closed by
            default.
          </li>
          <li>
            <strong>deny</strong> — blocks access. <em>Deny always wins:</em> if any matching
            deny exists, the user is denied even if other allows match. Useful for carving
            exceptions out of broad allows.
          </li>
        </ul>

        <Text mb={4}><strong>Combination rule:</strong></Text>
        <Code block mb="sm">{`final = OR(matching allows) AND NOT OR(matching denies)`}</Code>

        <Divider my="md" />

        <Title order={6}>5. Priority</Title>
        <Text>
          An integer field that&apos;s currently informational only — the engine resolves
          conflicts purely via the deny-wins rule above. It&apos;s stored so future tooling
          (sorting, override-precedence schemes) can use it without a schema change.
        </Text>

        <Divider my="md" />

        <Title order={6}>6. Examples</Title>
        <Text mb={4}><strong>Everyone in Sales can view leads.</strong></Text>
        <Code block mb="sm">{`Principal kind: group
Principal:      Sales
Action:         view
Selector:       /record/*[recordtype=lead]
Effect:         allow`}</Code>

        <Text mb={4}><strong>Alice can edit any record assigned to her.</strong></Text>
        <Code block mb="sm">{`Principal kind: user
Principal:      alice
Action:         edit
Selector:       /record/*[assignee=user]
Effect:         allow`}</Code>

        <Text mb={4}>
          <strong>Editors role can edit everything except confidential records.</strong>
        </Text>
        <Code block mb="sm">{`# Allow on the role
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
Effect:         deny`}</Code>

        <Text mb={4}>
          <strong>
            Editors can archive records, but only Admins can permanently delete them.
          </strong>{" "}
          On a record, <Code>archive</Code> hides the row from default reads but the data
          (and history, comments, edges) stays intact and can be restored.{" "}
          <Code>delete</Code> permanently removes the record and cascade-clears its
          comments, history, edges, and watches — there&apos;s no undo. Treat them as separate
          permissions and reserve <Code>delete</Code> for trusted roles.
        </Text>
        <Code block mb="sm">{`# Editors can archive
Principal kind: role
Principal:      Editors
Action:         archive
Selector:       /record/*
Effect:         allow

# Only Admins can permanently delete
Principal kind: role
Principal:      Admin
Action:         delete
Selector:       /record/*
Effect:         allow`}</Code>

        <Text mb={4}>
          <strong>QA group can complete only their own workflow tasks.</strong>
        </Text>
        <Code block mb="sm">{`Principal kind: group
Principal:      QA
Action:         complete
Selector:       /workflowtask/*[assignee=user]
Effect:         allow`}</Code>

        <Text mb={4}>
          <strong>
            Workflow Operators can cancel running executions, but only Admins can delete the
            historical record.
          </strong>{" "}
          On a workflow execution, <Code>cancel</Code> halts a running process and marks it
          cancelled (history kept), while <Code>delete</Code> wipes the execution from
          Flowable entirely — both runtime and history. <Code>deleteall</Code> is the
          bulk-wipe action behind the &quot;Delete All Executions&quot; button on the Workflow
          Executions page; it isn&apos;t tied to a specific instance, so the selector must use{" "}
          <Code>/workflowexecution/*</Code>. Treat them as separate permissions.
        </Text>
        <Code block mb="sm">{`# Operators can cancel any execution
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
Effect:         allow

# Only Admins can wipe every execution at once
Principal kind: role
Principal:      Admin
Action:         deleteall
Selector:       /workflowexecution/*
Effect:         allow`}</Code>

        <Text mb={4}>
          <strong>Admins can move a running execution to a different BPMN step.</strong>{" "}
          The <Code>movestate</Code> action gates the &quot;Move Execution Here&quot; right-click option
          on the workflow executions diagram. It cancels every active token on the run and
          starts a fresh token at the chosen node — process variables persist, but pending
          user/service tasks at the cancelled nodes are discarded. Keep this separate from{" "}
          <Code>override</Code> so admins can grant variable / reassign overrides without
          granting state moves.
        </Text>
        <Code block mb="sm">{`Principal kind: role
Principal:      Admin
Action:         movestate
Selector:       /workflowexecution/*
Effect:         allow`}</Code>

        <Text mb={4}>
          <strong>
            Managers see records and executions attributed to the people they supervise.
          </strong>
        </Text>
        <Code block mb="sm">{`# Set up the hierarchy (one-time, per supervisee):
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
Effect:         allow`}</Code>

        <Divider my="md" />

        <Title order={6}>7. Workflow</Title>
        <ol>
          <li>Pick a principal kind, then the principal.</li>
          <li>Type or pick the action (or <Code>*</Code> for any). Use the action list above as a reference.</li>
          <li>Use the visual builder for the selector; flip to raw for nesting/quoting.</li>
          <li>Choose <Code>allow</Code> or <Code>deny</Code>.</li>
          <li>Click <strong>Add</strong> — the grant takes effect on the next request.</li>
          <li>To remove a grant, click <strong>Revoke</strong> in its row in the table below.</li>
        </ol>

        <Divider my="md" />

        <Title order={6}>8. Behavior notes</Title>
        <ul>
          <li>
            <strong>SuperAdmin bypasses everything.</strong> Members of the built-in
            SuperAdmin role pass every check; their grants don&apos;t matter.
          </li>
          <li>
            <strong>Authorization must be enabled to enforce.</strong> When the feature flag
            is off, all grants are stored but ignored. Flip <Code>Authorization:Enabled</Code>{" "}
            + <Code>Enforcement</Code> in app settings to turn enforcement on.
          </li>
          <li>
            <strong>Cache invalidation is automatic.</strong> Adding or revoking a grant
            bumps the auth cache version, so the change is visible on the caller&apos;s next
            request.
          </li>
          <li>
            <strong>Closed by default.</strong> A user with no matching allow grant for a
            given (kind, action) sees nothing and is denied — even with no deny rules in
            place.
          </li>
          <li>
            <strong>Debug a real decision.</strong> The <Code>Effective Permissions</Code>{" "}
            page replays the evaluator for any user, action, and target — it shows the final
            allow/deny and which grants matched (or didn&apos;t), so you can answer &quot;why does
            Alice get a 403?&quot; without guessing.
          </li>
        </ul>
      </Box>
      <Group justify="flex-end" mt="md">
        <Button onClick={onClose}>Close</Button>
      </Group>
    </Modal>
  );
}
