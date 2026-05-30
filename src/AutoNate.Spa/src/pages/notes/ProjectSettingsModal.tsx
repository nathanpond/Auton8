import { useEffect, useMemo, useState } from "react";
import {
  ActionIcon,
  Alert,
  Avatar,
  Badge,
  Button,
  Group,
  Loader,
  Modal,
  Select,
  Stack,
  Text,
  Tooltip
} from "@mantine/core";
import {
  DerivedResourceDto,
  PrincipalKind,
  ProjectDto,
  ProjectMemberDto,
  ProjectMemberSource,
  ProjectRoleWire
} from "@/api/content";
import { modals } from "@mantine/modals";
import {
  useProjectMembers,
  useRemoveProjectMember,
  useRevokeDerivedGrant,
  useSetProjectMemberRole
} from "@/hooks/useContent";
import { useUsers } from "@/hooks/useUsers";
import { useMe } from "@/hooks/useMe";
import { LocalUser } from "@/types/flowable";
import { avatarUrl } from "@/lib/yjs/avatarUrl";
import "@/preferences/PreferencesModal.css";

type CategoryDef = {
  id: "general" | "permissions";
  label: string;
  icon: string;
};

const CATEGORIES: CategoryDef[] = [
  { id: "general", label: "General", icon: "fa-sliders" },
  { id: "permissions", label: "Permissions", icon: "fa-lock" }
];

const ROLE_OPTIONS: { value: ProjectRoleWire; label: string }[] = [
  { value: "owner", label: "Owner" },
  { value: "contributor", label: "Contributor" },
  { value: "commenter", label: "Commenter" },
  { value: "viewer", label: "Viewer" }
];

const ROLE_LABEL: Record<ProjectRoleWire, string> = {
  owner: "Owner",
  contributor: "Contributor",
  commenter: "Commenter",
  viewer: "Viewer"
};

type Props = {
  project: ProjectDto | null;
  opened: boolean;
  onClose: () => void;
};

export function ProjectSettingsModal({ project, opened, onClose }: Props) {
  const [activeCat, setActiveCat] = useState<CategoryDef["id"]>("general");

  useEffect(() => {
    if (opened) setActiveCat("general");
  }, [opened]);

  useEffect(() => {
    if (!opened) return;
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        e.preventDefault();
        onClose();
      }
    };
    window.addEventListener("keydown", onKey);
    return () => window.removeEventListener("keydown", onKey);
  }, [opened, onClose]);

  const currentCat = CATEGORIES.find((c) => c.id === activeCat) ?? CATEGORIES[0];
  const title = project ? `Project Settings — ${project.name}` : "Project Settings";

  return (
    <Modal
      opened={opened}
      onClose={onClose}
      size="auto"
      withCloseButton={false}
      padding={0}
      zIndex={1065}
      styles={{
        content: {
          width: "min(1120px, calc(100vw - 32px))",
          height: "min(720px, calc(100vh - 32px))"
        },
        body: { display: "flex", flexDirection: "column", minHeight: 0, height: "100%" }
      }}
    >
      <div className="pref-header" style={{ padding: "16px" }}>
        <h5 style={{ margin: 0, flex: 1 }}>{title}</h5>
        <Button variant="subtle" color="gray" size="xs" onClick={onClose} aria-label="Close">
          <i className="fa fa-times" />
        </Button>
      </div>

      <div className="pref-body">
        <aside className="pref-cats">
          {CATEGORIES.map((cat) => (
            <button
              key={cat.id}
              type="button"
              className={`pref-cat${activeCat === cat.id ? " active" : ""}`}
              onClick={() => setActiveCat(cat.id)}
            >
              <i className={`fa ${cat.icon} pref-cat-icon`} aria-hidden="true" />
              <span className="pref-cat-label">{cat.label}</span>
            </button>
          ))}
        </aside>

        <main className="pref-pane">
          {activeCat === "general" && (
            <>
              <header className="pref-content-header">
                <h3>{currentCat.label}</h3>
              </header>
              <div className="pref-empty-state">
                <i className={`fa ${currentCat.icon}`} aria-hidden="true" />
                <div>General settings will appear here.</div>
              </div>
            </>
          )}

          {activeCat === "permissions" && <PermissionsPanel project={project} />}
        </main>
      </div>

      <Group
        justify="flex-end"
        gap="xs"
        p="md"
        style={{ borderTop: "1px solid var(--mantine-color-default-border)" }}
      >
        <Button variant="default" onClick={onClose}>
          Close
        </Button>
      </Group>
    </Modal>
  );
}

function PermissionsPanel({ project }: { project: ProjectDto | null }) {
  const meQuery = useMe();
  const me = meQuery.data && meQuery.data.authenticated ? meQuery.data : null;
  const isSuperAdmin = !!me?.isSuperAdmin;

  const membersQuery = useProjectMembers(project?.id ?? null);
  const usersQuery = useUsers();

  const setRole = useSetProjectMemberRole(project?.id ?? null);
  const removeMember = useRemoveProjectMember(project?.id ?? null);
  const revokeGrant = useRevokeDerivedGrant(project?.id ?? null);

  const [newUserId, setNewUserId] = useState<string | null>(null);
  const [newRole, setNewRole] = useState<ProjectRoleWire>("contributor");
  const [mutationError, setMutationError] = useState<string | null>(null);

  const members = membersQuery.data?.members ?? [];
  // Server's verdict — true for project owners, super-admins, and holders
  // of a wildcard content grant. Mirrors backend gating, so the SPA enables
  // controls for viewers who aren't literal project_members rows.
  const viewerCanManage = !!membersQuery.data?.viewerCanManage;
  const users = usersQuery.data ?? [];

  const usersByUserId = useMemo(() => {
    const m = new Map<string, LocalUser>();
    for (const u of users) m.set(u.userId, u);
    return m;
  }, [users]);

  const myRole = useMemo<ProjectRoleWire | null>(() => {
    if (!me) return null;
    // Real project_members rows are always user principals.
    const row = members.find(
      (mem) =>
        mem.source === "member" &&
        mem.principalKind === "user" &&
        mem.principalId === me.userId
    );
    return row?.role ?? null;
  }, [members, me]);

  // Synthesized rows (super-admin / wildcard / grant) don't represent literal
  // project_members records — they can't be edited or removed and shouldn't
  // shrink the "add a member" candidate list.
  const isSynthesized = (m: ProjectMemberDto) => m.source !== "member";

  const canEdit = viewerCanManage || isSuperAdmin || myRole === "owner";

  // Users who can be added (i.e. aren't already real members). Synthesized
  // entries don't take up a slot. Reset the picker value when the candidate
  // list changes underneath it.
  const candidates = useMemo(() => {
    const taken = new Set(
      members
        .filter((m) => m.source === "member" && m.principalKind === "user")
        .map((m) => m.principalId)
    );
    return users.filter((u) => !taken.has(u.userId));
  }, [users, members]);

  useEffect(() => {
    if (newUserId && !candidates.find((c) => c.userId === newUserId)) {
      setNewUserId(null);
    }
  }, [candidates, newUserId]);

  const candidateOptions = useMemo(
    () =>
      candidates
        .map((u) => ({ value: u.userId, label: displayLabel(u) }))
        .sort((a, b) => a.label.localeCompare(b.label)),
    [candidates]
  );

  const principalDisplay = (m: ProjectMemberDto): string => {
    if (m.principalKind === "user") {
      return displayLabel(usersByUserId.get(m.principalId)) || m.principalId;
    }
    return m.principalName ?? m.principalId;
  };

  const projectMemberRows = useMemo(() => {
    return members
      .filter((m) => m.source === "member")
      .sort((a, b) => {
        const ra = roleRank(a.role);
        const rb = roleRank(b.role);
        if (ra !== rb) return ra - rb;
        return principalDisplay(a).localeCompare(principalDisplay(b));
      });
    // principalDisplay only depends on `usersByUserId`, captured here.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [members, usersByUserId]);

  const derivedRows = useMemo(() => {
    return members
      .filter((m) => isSynthesized(m))
      .sort((a, b) => {
        const sa = sourceRank(a.source);
        const sb = sourceRank(b.source);
        if (sa !== sb) return sa - sb;
        const ka = principalKindRank(a.principalKind);
        const kb = principalKindRank(b.principalKind);
        if (ka !== kb) return ka - kb;
        const nameCmp = principalDisplay(a).localeCompare(principalDisplay(b));
        if (nameCmp !== 0) return nameCmp;
        const actionCmp = (a.action ?? "").localeCompare(b.action ?? "");
        if (actionCmp !== 0) return actionCmp;
        return (a.grantId ?? "").localeCompare(b.grantId ?? "");
      });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [members, usersByUserId]);

  if (!project) {
    return (
      <div className="pref-empty-state">
        <i className="fa fa-lock" aria-hidden="true" />
        <div>Select a project to manage permissions.</div>
      </div>
    );
  }

  const onAdd = async () => {
    if (!newUserId) return;
    setMutationError(null);
    try {
      await setRole.mutateAsync({ userId: newUserId, role: newRole });
      setNewUserId(null);
      setNewRole("contributor");
    } catch (err) {
      setMutationError(describeError(err));
    }
  };

  const onChangeRole = async (userId: string, role: ProjectRoleWire) => {
    setMutationError(null);
    try {
      await setRole.mutateAsync({ userId, role });
    } catch (err) {
      setMutationError(describeError(err));
    }
  };

  const onRevokeGrant = (member: ProjectMemberDto) => {
    if (!member.grantId || !member.revokable) return;
    setMutationError(null);
    const principalLabel =
      member.principalKind === "user"
        ? displayLabel(usersByUserId.get(member.principalId)) || member.principalId
        : member.principalName ?? `${member.principalKind} ${member.principalId.slice(0, 8)}…`;
    const grantId = member.grantId;
    modals.openConfirmModal({
      title: "Revoke grant",
      zIndex: 1066,
      children: (
        <Stack gap="xs">
          <Text size="sm">
            Revoke <strong>{principalLabel}</strong>'s{" "}
            <strong>{member.action ?? "grant"}</strong> grant?
          </Text>
          <Text size="sm" c="dimmed">
            The grant will be deleted. Principals whose only path to the listed resources was this
            grant will lose access. Other paths (super admin, wildcard grants, project membership,
            other grants) are unaffected.
          </Text>
        </Stack>
      ),
      labels: { confirm: "Revoke", cancel: "Cancel" },
      confirmProps: { color: "red" },
      onConfirm: async () => {
        try {
          await revokeGrant.mutateAsync(grantId);
        } catch (err) {
          setMutationError(describeError(err));
        }
      }
    });
  };

  const onRemove = async (userId: string) => {
    setMutationError(null);
    try {
      await removeMember.mutateAsync(userId);
    } catch (err) {
      setMutationError(describeError(err));
    }
  };

  return (
    <>
      <header className="pref-content-header">
        <h3>Permissions</h3>
        <p>
          Manage who has access to <strong>{project.name}</strong>. Owners can manage members and
          unlock deletion. Contributors can edit content. Viewers can only read.
        </p>
      </header>

      {mutationError && (
        <Alert color="red" mb="md" onClose={() => setMutationError(null)} withCloseButton>
          {mutationError}
        </Alert>
      )}

      {canEdit && (
        <Stack gap="xs" mb="lg">
          <Text size="sm" fw={600}>
            Add member
          </Text>
          <Group gap="xs" align="flex-end" wrap="nowrap">
            <Select
              placeholder={candidateOptions.length ? "Pick a user…" : "No users to add"}
              data={candidateOptions}
              value={newUserId}
              onChange={setNewUserId}
              searchable
              clearable
              nothingFoundMessage="No matching users"
              disabled={!candidateOptions.length || setRole.isPending}
              comboboxProps={{ zIndex: 1066 }}
              style={{ flex: 1, minWidth: 240 }}
            />
            <Select
              data={ROLE_OPTIONS}
              value={newRole}
              onChange={(v) => v && setNewRole(v as ProjectRoleWire)}
              allowDeselect={false}
              disabled={setRole.isPending}
              comboboxProps={{ zIndex: 1066 }}
              style={{ width: 160 }}
            />
            <Button
              onClick={onAdd}
              disabled={!newUserId || setRole.isPending}
              loading={setRole.isPending && !!newUserId}
            >
              Add
            </Button>
          </Group>
        </Stack>
      )}

      <Stack gap={0}>
        <Group gap="xs" align="baseline" mb="xs">
          <Text size="sm" fw={600}>
            Project Permissions
          </Text>
        </Group>
        <SectionHeader label="Member" />

        {membersQuery.isLoading ? (
          <Group justify="center" p="lg">
            <Loader size="sm" />
          </Group>
        ) : projectMemberRows.length === 0 ? (
          <div className="pref-empty-state">
            <i className="fa fa-users" aria-hidden="true" />
            <div>No members yet.</div>
          </div>
        ) : (
          projectMemberRows.map((m) => (
            <MemberRow
              key={`${m.source}:${m.principalKind}:${m.principalId}`}
              member={m}
              user={usersByUserId.get(m.principalId) ?? null}
              isSelf={!!me && m.principalKind === "user" && m.principalId === me.userId}
              canEdit={canEdit}
              busy={setRole.isPending || removeMember.isPending}
              onChangeRole={(role) => onChangeRole(m.principalId, role)}
              onRemove={() => onRemove(m.principalId)}
            />
          ))
        )}
      </Stack>

      {derivedRows.length > 0 && (
        <Stack gap={0} mt="xl">
          <Group gap="xs" align="baseline" mb="xs">
            <Text size="sm" fw={600}>
              Derived Permissions
            </Text>
            <Text size="xs" c="dimmed">
              One row per grant or SuperAdmin assignment. Grants scoped entirely to this project
              can be revoked here. Grants that also reach resources outside this project must be
              managed under <strong>Admin → Permissions</strong>.
            </Text>
          </Group>
          <SectionHeader label="Principal" roleColumn="Access" />
          {derivedRows.map((m) => (
            <MemberRow
              key={`${m.source}:${m.grantId ?? "_"}:${m.principalKind}:${m.principalId}`}
              member={m}
              user={
                m.principalKind === "user" ? usersByUserId.get(m.principalId) ?? null : null
              }
              isSelf={!!me && m.principalKind === "user" && m.principalId === me.userId}
              canEdit={false}
              busy={revokeGrant.isPending}
              canRevokeGrants={canEdit}
              onRevokeGrant={() => onRevokeGrant(m)}
              onChangeRole={() => {}}
              onRemove={() => {}}
            />
          ))}
        </Stack>
      )}
    </>
  );
}

function SectionHeader({ label, roleColumn = "Role" }: { label: string; roleColumn?: string }) {
  return (
    <Group
      justify="space-between"
      align="center"
      py="xs"
      px="xs"
      style={{
        borderBottom: "1px solid var(--mantine-color-default-border)",
        fontSize: 12,
        fontWeight: 600,
        color: "var(--mantine-color-dimmed)",
        textTransform: "uppercase",
        letterSpacing: "0.04em"
      }}
    >
      <Text size="xs" fw={700} c="dimmed" style={{ flex: 1 }}>
        {label}
      </Text>
      <Text size="xs" fw={700} c="dimmed" style={{ width: 160 }}>
        {roleColumn}
      </Text>
      <span style={{ width: 36 }} />
    </Group>
  );
}

function MemberRow({
  member,
  user,
  isSelf,
  canEdit,
  busy,
  canRevokeGrants = false,
  onChangeRole,
  onRemove,
  onRevokeGrant
}: {
  member: ProjectMemberDto;
  user: LocalUser | null;
  isSelf: boolean;
  canEdit: boolean;
  busy: boolean;
  canRevokeGrants?: boolean;
  onChangeRole: (role: ProjectRoleWire) => void;
  onRemove: () => void;
  onRevokeGrant?: () => void;
}) {
  const isUserPrincipal = member.principalKind === "user";
  const displayName = isUserPrincipal
    ? user
      ? displayLabel(user)
      : `Unknown user (${member.principalId.slice(0, 8)}…)`
    : member.principalName ?? `Unknown ${member.principalKind} (${member.principalId.slice(0, 8)}…)`;
  const subtitle = isUserPrincipal
    ? user
      ? user.username
      : member.principalId
    : principalKindLabel(member.principalKind);
  const synthesized = member.source !== "member";
  const grantResources = member.source === "grant" ? (member.resources ?? []) : [];
  return (
    <Group
      justify="space-between"
      align="flex-start"
      py="xs"
      px="xs"
      wrap="nowrap"
      style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}
    >
      <Group gap="sm" wrap="nowrap" style={{ flex: 1, minWidth: 0 }} align="flex-start">
        {isUserPrincipal ? (
          <Avatar src={avatarUrl(member.principalId, displayName)} radius="xl" size="sm" />
        ) : (
          <Avatar radius="xl" size="sm" color={member.principalKind === "group" ? "indigo" : "orange"}>
            <i
              className={`fa ${member.principalKind === "group" ? "fa-users" : "fa-shield-halved"}`}
              aria-hidden="true"
            />
          </Avatar>
        )}
        <Stack gap={2} style={{ minWidth: 0, flex: 1 }}>
          <Group gap="xs" wrap="nowrap">
            <Text size="sm" fw={600} truncate>
              {displayName}
            </Text>
            {!isUserPrincipal && (
              <Badge size="xs" variant="light" color="gray">
                {principalKindLabel(member.principalKind)}
              </Badge>
            )}
            {isSelf && (
              <Badge size="xs" variant="light" color="gray">
                you
              </Badge>
            )}
            {member.source === "super-admin" && (
              <Tooltip label="Has access via SuperAdmin role" withArrow zIndex={1067}>
                <Badge size="xs" variant="light" color="grape">
                  super admin
                </Badge>
              </Tooltip>
            )}
            {member.source === "wildcard" && (
              <Tooltip label="Has access via a wildcard permission grant" withArrow zIndex={1067}>
                <Badge size="xs" variant="light" color="blue">
                  full access
                </Badge>
              </Tooltip>
            )}
            {member.source === "grant" && (
              <Tooltip
                label="Has access only to the listed resources"
                withArrow
                zIndex={1067}
              >
                <Badge size="xs" variant="light" color="teal">
                  partial access
                </Badge>
              </Tooltip>
            )}
          </Group>
          <Text size="xs" c="dimmed" truncate>
            {subtitle}
          </Text>
          {grantResources.length > 0 && (
            <Group gap={4} wrap="wrap" mt={4}>
              {grantResources.map((r) => {
                const label = r.name ?? `${r.kind} (${r.id.slice(0, 8)}…)`;
                const href = r.locator != null ? `/notes/${r.locator}` : null;
                return (
                  <Tooltip
                    key={`${r.kind}:${r.id}`}
                    label={
                      href
                        ? `${r.kind}: ${label} — opens in new tab`
                        : `${r.kind}: ${label}`
                    }
                    withArrow
                    zIndex={1067}
                  >
                    <Text
                      component={href ? "a" : "span"}
                      href={href ?? undefined}
                      target={href ? "_blank" : undefined}
                      rel={href ? "noopener noreferrer" : undefined}
                      size="xs"
                      style={{
                        display: "inline-flex",
                        alignItems: "center",
                        gap: 4,
                        textDecoration: "none",
                        color: "inherit",
                        cursor: href ? "pointer" : "default",
                        padding: "1px 6px",
                        border: "1px solid var(--mantine-color-default-border)",
                        borderRadius: "var(--mantine-radius-xl)",
                        lineHeight: 1.5
                      }}
                    >
                      <i
                        className={`fa ${resourceKindIcon(r.kind)}`}
                        style={{ fontSize: 10 }}
                        aria-hidden="true"
                      />
                      {label}
                    </Text>
                  </Tooltip>
                );
              })}
            </Group>
          )}
        </Stack>
      </Group>

      <div style={{ width: 160 }}>
        {canEdit ? (
          <Select
            data={ROLE_OPTIONS}
            value={member.role}
            onChange={(v) => v && onChangeRole(v as ProjectRoleWire)}
            allowDeselect={false}
            disabled={busy}
            comboboxProps={{ zIndex: 1066 }}
            size="xs"
          />
        ) : (
          <Text size="sm" c={synthesized ? "dimmed" : undefined}>
            {member.source === "grant" || member.source === "wildcard"
              ? member.action ?? "—"
              : ROLE_LABEL[member.role]}
          </Text>
        )}
      </div>

      <div style={{ width: 36, display: "flex", justifyContent: "flex-end" }}>
        {member.source === "member" ? (
          canEdit && (
            <Tooltip label={isSelf ? "Remove yourself" : "Remove member"} withArrow zIndex={1067}>
              <ActionIcon
                variant="subtle"
                color="red"
                onClick={onRemove}
                disabled={busy}
                aria-label="Remove member"
              >
                <i className="fa fa-trash" />
              </ActionIcon>
            </Tooltip>
          )
        ) : member.source === "grant" && canRevokeGrants && onRevokeGrant ? (
          <Tooltip
            label={
              member.revokable
                ? "Revoke this grant"
                : "This grant also targets resources outside this project — contact your administrator to manage it"
            }
            withArrow
            multiline={!member.revokable}
            w={member.revokable ? undefined : 260}
            zIndex={1067}
          >
            {/* Wrap disabled ActionIcon so the Tooltip still fires on hover. */}
            <span style={{ display: "inline-flex" }}>
              <ActionIcon
                variant="subtle"
                color="red"
                onClick={onRevokeGrant}
                disabled={busy || !member.revokable}
                aria-label="Revoke grant"
              >
                <i className="fa fa-trash" />
              </ActionIcon>
            </span>
          </Tooltip>
        ) : null}
      </div>
    </Group>
  );
}

function displayLabel(user: LocalUser | null | undefined): string {
  if (!user) return "";
  const full = `${user.firstName ?? ""} ${user.lastName ?? ""}`.trim();
  if (full && user.username) return `${full} (${user.username})`;
  return full || user.username || "";
}

function roleRank(role: ProjectRoleWire): number {
  return role === "owner" ? 0 : role === "contributor" ? 1 : 2;
}

function sourceRank(source: ProjectMemberSource): number {
  return source === "member" ? 0 : source === "super-admin" ? 1 : source === "wildcard" ? 2 : 3;
}

function principalKindRank(kind: PrincipalKind): number {
  return kind === "user" ? 0 : kind === "group" ? 1 : 2;
}

function principalKindLabel(kind: PrincipalKind): string {
  return kind === "user" ? "user" : kind === "group" ? "group" : "role";
}

function resourceKindIcon(kind: DerivedResourceDto["kind"]): string {
  switch (kind) {
    case "project":
      return "fa-folder-tree";
    case "cabinet":
      return "fa-folder";
    case "notebook":
      return "fa-book";
    case "page":
      return "fa-file-lines";
  }
}

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string }; status?: number } }).response;
    if (resp?.status === 403) return "You don't have permission to manage members.";
    return resp?.data?.error ?? resp?.data?.message ?? "Request failed.";
  }
  return err instanceof Error ? err.message : "Request failed.";
}
