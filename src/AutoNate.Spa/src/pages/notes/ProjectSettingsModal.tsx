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
import { ProjectDto, ProjectMemberDto, ProjectRoleWire } from "@/api/content";
import {
  useProjectMembers,
  useRemoveProjectMember,
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
  { value: "viewer", label: "Viewer" }
];

const ROLE_LABEL: Record<ProjectRoleWire, string> = {
  owner: "Owner",
  contributor: "Contributor",
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

  const [newUserId, setNewUserId] = useState<string | null>(null);
  const [newRole, setNewRole] = useState<ProjectRoleWire>("contributor");
  const [mutationError, setMutationError] = useState<string | null>(null);

  const members = membersQuery.data ?? [];
  const users = usersQuery.data ?? [];

  const usersByUserId = useMemo(() => {
    const m = new Map<string, LocalUser>();
    for (const u of users) m.set(u.userId, u);
    return m;
  }, [users]);

  const myRole = useMemo<ProjectRoleWire | null>(() => {
    if (!me) return null;
    const row = members.find((mem) => mem.userId === me.userId);
    return row?.role ?? null;
  }, [members, me]);

  const canEdit = isSuperAdmin || myRole === "owner";

  // Users who can be added (i.e. aren't already members). Reset the picker
  // value when the candidate list changes underneath it.
  const candidates = useMemo(() => {
    const taken = new Set(members.map((m) => m.userId));
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

  const memberRows = useMemo(() => {
    return [...members].sort((a, b) => {
      const ra = roleRank(a.role);
      const rb = roleRank(b.role);
      if (ra !== rb) return ra - rb;
      const na = displayLabel(usersByUserId.get(a.userId)) || a.userId;
      const nb = displayLabel(usersByUserId.get(b.userId)) || b.userId;
      return na.localeCompare(nb);
    });
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
            Member
          </Text>
          <Text size="xs" fw={700} c="dimmed" style={{ width: 160 }}>
            Role
          </Text>
          <span style={{ width: 36 }} />
        </Group>

        {membersQuery.isLoading ? (
          <Group justify="center" p="lg">
            <Loader size="sm" />
          </Group>
        ) : memberRows.length === 0 ? (
          <div className="pref-empty-state">
            <i className="fa fa-users" aria-hidden="true" />
            <div>No members yet.</div>
          </div>
        ) : (
          memberRows.map((m) => (
            <MemberRow
              key={m.userId}
              member={m}
              user={usersByUserId.get(m.userId) ?? null}
              isSelf={!!me && m.userId === me.userId}
              canEdit={canEdit}
              busy={setRole.isPending || removeMember.isPending}
              onChangeRole={(role) => onChangeRole(m.userId, role)}
              onRemove={() => onRemove(m.userId)}
            />
          ))
        )}
      </Stack>
    </>
  );
}

function MemberRow({
  member,
  user,
  isSelf,
  canEdit,
  busy,
  onChangeRole,
  onRemove
}: {
  member: ProjectMemberDto;
  user: LocalUser | null;
  isSelf: boolean;
  canEdit: boolean;
  busy: boolean;
  onChangeRole: (role: ProjectRoleWire) => void;
  onRemove: () => void;
}) {
  const displayName = user ? displayLabel(user) : `Unknown user (${member.userId.slice(0, 8)}…)`;
  const subtitle = user ? user.username : member.userId;
  return (
    <Group
      justify="space-between"
      align="center"
      py="xs"
      px="xs"
      wrap="nowrap"
      style={{ borderBottom: "1px solid var(--mantine-color-default-border)" }}
    >
      <Group gap="sm" wrap="nowrap" style={{ flex: 1, minWidth: 0 }}>
        <Avatar src={avatarUrl(member.userId, displayName)} radius="xl" size="sm" />
        <Stack gap={0} style={{ minWidth: 0 }}>
          <Group gap="xs" wrap="nowrap">
            <Text size="sm" fw={600} truncate>
              {displayName}
            </Text>
            {isSelf && (
              <Badge size="xs" variant="light" color="gray">
                you
              </Badge>
            )}
          </Group>
          <Text size="xs" c="dimmed" truncate>
            {subtitle}
          </Text>
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
          <Text size="sm">{ROLE_LABEL[member.role]}</Text>
        )}
      </div>

      <div style={{ width: 36, display: "flex", justifyContent: "flex-end" }}>
        {canEdit && (
          <Tooltip label={isSelf ? "Remove yourself" : "Remove member"} withArrow>
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
        )}
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

function describeError(err: unknown): string {
  if (typeof err === "object" && err && "response" in err) {
    const resp = (err as { response?: { data?: { error?: string; message?: string }; status?: number } }).response;
    if (resp?.status === 403) return "You don't have permission to manage members.";
    return resp?.data?.error ?? resp?.data?.message ?? "Request failed.";
  }
  return err instanceof Error ? err.message : "Request failed.";
}
