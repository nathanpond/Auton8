import { Link } from "react-router-dom";
import { Box, Button, Code, Divider, Group, Modal, Text, Title } from "@mantine/core";

type Props = {
  onClose: () => void;
};

// Reference for the Roles admin page. Roles are containers; the actual
// permissions a role conveys live in permission_grants (principal_kind='role')
// and are managed on the Permissions page.
export default function RolesHelpModal({ onClose }: Props) {
  return (
    <Modal opened onClose={onClose} title="How roles work" size="lg">
      <Box>
        <Text>
          A <strong>role</strong> is a named handle you attach permissions to and hand out to
          people. By itself it does nothing — it gains meaning when you (a) attach permission
          rules to it on the <Link to="/admin/grants">Permissions</Link> page and (b) assign
          it to users or groups on this page.
        </Text>

        <Divider my="md" />

        <Title order={6}>1. The three-step model</Title>
        <ol>
          <li>
            <strong>Create the role</strong> here — give it a name like <Code>Editors</Code>{" "}
            or <Code>Sales</Code>. Description is optional.
          </li>
          <li>
            <strong>Attach permissions to the role</strong> on the{" "}
            <Link to="/admin/grants">Permissions</Link> page. Pick principal kind{" "}
            <Code>role</Code>, pick this role, then add as many{" "}
            <Code>(action, selector, allow|deny)</Code> rows as you need. The role itself is
            just a label; everything it lets people do lives in those rows.
          </li>
          <li>
            <strong>Assign the role</strong> to a user or group here. Anyone the role is
            assigned to picks up every permission attached to that role on their next request.
          </li>
        </ol>

        <Divider my="md" />

        <Title order={6}>2. Creating and deleting roles</Title>
        <ul>
          <li>Names are unique system-wide. Re-using a name returns a 400.</li>
          <li>
            Built-in <strong>system roles</strong> (currently just <Code>SuperAdmin</Code>)
            can&apos;t be renamed or deleted. The Delete button is hidden for them.
          </li>
          <li>
            Deleting a normal role <strong>cascades</strong>: all permission grants attached
            to it are removed, and any role assignments referring to it are removed too. Users
            currently relying on that role lose access immediately.
          </li>
        </ul>

        <Divider my="md" />

        <Title order={6}>3. Assignments</Title>
        <Text>
          Click a role on the left to open its Assignments panel. Each row says &quot;this role
          applies to that principal.&quot;
        </Text>
        <ul>
          <li>
            <strong>Principal kind</strong>: <Code>user</Code> or <Code>group</Code>. (You
            can&apos;t assign a role to another role — permissions on roles flow through the
            unified grants table.)
          </li>
          <li>
            <strong>Principal</strong>: the specific user or group.
          </li>
          <li>
            <strong>Scope</strong> (optional): a selector that further restricts where this
            assignment applies. The grant graph stores it today, but the evaluator doesn&apos;t
            yet narrow grants by per-assignment scope — treat this field as <em>reserved for
            future use</em>. Leave it blank for normal assignments.
          </li>
        </ul>
        <Text>
          Click <strong>Revoke</strong> to remove an assignment. The user/group loses access
          on the next request.
        </Text>

        <Divider my="md" />

        <Title order={6}>4. How a user&apos;s effective permissions are computed</Title>
        <Text>
          When a user makes a request, the evaluator unions every grant that reaches them
          through any of these chains:
        </Text>
        <ul>
          <li>
            <Code>permission_grants</Code> attached directly to <em>them</em> (principal_kind
            = user).
          </li>
          <li>
            <Code>permission_grants</Code> attached to a <em>group</em> they&apos;re a member of.
          </li>
          <li>
            <Code>permission_grants</Code> attached to a <em>role</em> they&apos;re assigned —
            directly here, or indirectly via a group.
          </li>
        </ul>
        <Text mb={4}>
          <strong>Combination:</strong>
        </Text>
        <Code block mb="sm">
          {`final = OR(matching allows from any source) AND NOT OR(matching denies from any source)`}
        </Code>
        <Text>
          Deny always wins. A deny on the role blocks the user even if their group has an
          allow.
        </Text>

        <Divider my="md" />

        <Title order={6}>5. SuperAdmin</Title>
        <ul>
          <li>
            Built-in. Members bypass <em>every</em> authorization check; their grants don&apos;t
            matter.
          </li>
          <li>
            Be careful with the Assignments panel — you <em>can</em> revoke your own
            SuperAdmin membership. If no one else has it either, you may lock yourself out of
            the admin pages once enforcement is on.
          </li>
          <li>
            On a fresh install with{" "}
            <Code>Authorization:AssignSuperAdminToAllExistingUsers=true</Code> (the default),
            every existing user gets SuperAdmin once. After that, new users start with no
            roles and you grant them as needed.
          </li>
        </ul>

        <Divider my="md" />

        <Title order={6}>6. Common patterns</Title>
        <Text mb={4}>
          <strong>Read-only role for a group of viewers:</strong>
        </Text>
        <ol>
          <li>Create role <Code>Viewer</Code>.</li>
          <li>
            On Permissions: add <Code>view</Code> grant on <Code>/record/*</Code>,{" "}
            <Code>allow</Code>, principal kind <Code>role</Code>, principal <Code>Viewer</Code>.
          </li>
          <li>Here: assign <Code>Viewer</Code> to your <Code>Viewers</Code> group.</li>
        </ol>

        <Text mb={4}>
          <strong>Per-user assignee role:</strong>
        </Text>
        <ol>
          <li>Create role <Code>AssignedRecordHandler</Code>.</li>
          <li>
            On Permissions: add <Code>view</Code> + <Code>edit</Code> grants on{" "}
            <Code>/record/*[assignee=user]</Code>, <Code>allow</Code>.
          </li>
          <li>
            Here: assign the role to every user who handles their own records. Each user gets
            the same selector — <Code>user</Code> resolves to the caller, so they each see
            only their own.
          </li>
        </ol>

        <Divider my="md" />

        <Title order={6}>7. Behavior notes</Title>
        <ul>
          <li>
            <strong>Authorization must be on to enforce.</strong> While the feature flag is
            off, role assignments are recorded but ignored.
          </li>
          <li>
            <strong>Cache invalidation is automatic.</strong> Assigning or revoking bumps the
            auth cache version; the change is visible on the caller&apos;s next request.
          </li>
          <li>
            <strong>Closed by default.</strong> A user with no assigned roles and no
            direct/group grants for a given (kind, action) is denied — even when no deny
            rules exist.
          </li>
          <li>
            <strong>Roles can&apos;t nest.</strong> A role isn&apos;t a member of another role. Compose
            access via groups + multiple role assignments instead.
          </li>
        </ul>
      </Box>
      <Group justify="flex-end" mt="md">
        <Button onClick={onClose}>Close</Button>
      </Group>
    </Modal>
  );
}
