import { useMemo, useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  ActionIcon,
  Alert,
  Box,
  Button,
  Group,
  Modal,
  Stack,
  Switch,
  Text,
  TextInput
} from "@mantine/core";
import PageHeader from "@/components/PageHeader";
import {
  useCreateUser,
  useDeleteUser,
  useResetUserPassword,
  useUnlockUser,
  useUpdateUser
} from "@/hooks/useUsers";
import { useRoleAssignments, useRoles } from "@/hooks/useAdmin";
import { permissionKey, usePermissionChecks } from "@/hooks/usePermissionChecks";
import { listUsers, listUsersPage } from "@/api/users";
import { LocalUser } from "@/types/flowable";
import {
  DataTable,
  DataTableFilterOption,
  DataTablePageRequest
} from "@/components/data-table/DataTable";
import {
  CreateUserForm,
  EditUserForm,
  ResetPasswordForm,
  createUserSchema,
  editUserSchema,
  resetPasswordSchema
} from "./userSchemas";

type ModalState =
  | { kind: "none" }
  | { kind: "add" }
  | { kind: "edit"; user: LocalUser }
  | { kind: "reset"; user: LocalUser }
  | { kind: "delete"; user: LocalUser };

type UserStatus = "Active" | "Disabled" | "Invited" | "Locked";

function getUserStatus(u: LocalUser): UserStatus {
  if (u.isLocked) return "Locked";
  if (!u.lastLoginDate) return "Invited";
  return "Active";
}

const STATUS_FILTERS: DataTableFilterOption<LocalUser>[] = [
  { id: "Active", label: "Active", predicate: (u) => getUserStatus(u) === "Active" },
  { id: "Disabled", label: "Disabled", predicate: (u) => getUserStatus(u) === "Disabled" },
  { id: "Invited", label: "Invited", predicate: (u) => getUserStatus(u) === "Invited" },
  { id: "Locked", label: "Locked", predicate: (u) => getUserStatus(u) === "Locked" }
];

const COLUMN_WIDTHS = ["22%", "22%", "22%", "20%", "14%", "90px"];

export default function ManageUsers() {
  const [modal, setModal] = useState<ModalState>({ kind: "none" });
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);

  const unlockCheck = useMemo(
    () => [{ kind: "user", action: "unlock", id: "*" }],
    []
  );
  const { data: unlockPermissions } = usePermissionChecks(unlockCheck);
  const canUnlock = unlockPermissions?.get(permissionKey(unlockCheck[0])) ?? false;

  const { data: roles = [] } = useRoles();
  const superAdminRoleId = useMemo(
    () => roles.find((r) => r.isSystem && r.name === "SuperAdmin")?.id ?? null,
    [roles]
  );
  const { data: adminAssignments = [] } = useRoleAssignments(superAdminRoleId);
  const adminUserIds = useMemo(
    () =>
      new Set(
        adminAssignments
          .filter((a) => a.principalKind === "user")
          .map((a) => a.principalId)
      ),
    [adminAssignments]
  );

  const columns = useMemo<ColumnDef<LocalUser>[]>(
    () => [
      {
        id: "username",
        accessorKey: "username",
        header: "User",
        cell: ({ row }) => {
          const isAdmin = adminUserIds.has(row.original.userId);
          return (
            <div className="manage-users-identity">
              <UserAvatar isAdmin={isAdmin} />
              <span className="manage-users-identity-name">{row.original.username}</span>
            </div>
          );
        }
      },
      {
        id: "fullName",
        header: "Full name",
        accessorFn: (u) => `${u.firstName} ${u.lastName}`.trim()
      },
      {
        id: "lastName",
        accessorKey: "lastName",
        header: "Last name"
      },
      {
        id: "lastLogin",
        header: "Last login",
        accessorFn: (u) => u.lastLoginDate ?? "",
        cell: ({ row }) => (
          <span
            className={
              row.original.lastLoginDate
                ? "manage-users-last-login"
                : "manage-users-last-login-never"
            }
          >
            {formatLastLogin(row.original.lastLoginDate)}
          </span>
        )
      },
      {
        id: "status",
        header: "Status",
        accessorFn: (u) => getUserStatus(u),
        cell: ({ row }) => <StatusDot status={getUserStatus(row.original)} />
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <Group gap="xs">
            <ActionIcon
              variant="subtle"
              color="gray"
              size="sm"
              title="Reset password"
              aria-label={`Reset password for ${row.original.username}`}
              onClick={(e) => {
                e.stopPropagation();
                setModal({ kind: "reset", user: row.original });
              }}
            >
              <i className="fa fa-key" />
            </ActionIcon>
            <ActionIcon
              variant="outline"
              color="red"
              size="sm"
              title="Delete user"
              aria-label={`Delete ${row.original.username}`}
              onClick={(e) => {
                e.stopPropagation();
                setModal({ kind: "delete", user: row.original });
              }}
            >
              <i className="fa fa-trash" />
            </ActionIcon>
          </Group>
        )
      }
    ],
    [adminUserIds]
  );

  const close = () => setModal({ kind: "none" });

  const loadPage = async (req: DataTablePageRequest) => {
    const result = await listUsersPage({
      page: req.page,
      pageSize: req.pageSize,
      search: req.search || undefined,
      sort: req.sort?.id,
      sortDir: req.sort ? (req.sort.desc ? "desc" : "asc") : undefined,
      status: req.filter ?? undefined
    });
    return { items: result.items, totalCount: result.totalCount };
  };

  return (
    <>
      <PageHeader
        title="Manage Users"
        description="Manage local users with search, sorting, paging, and quick account actions."
      />

      {flash && (
        <Alert
          color={flash.kind === "success" ? "green" : "red"}
          variant="light"
          role={flash.kind === "success" ? "status" : "alert"}
          mb="sm"
        >
          {flash.message}
        </Alert>
      )}

      <DataTable<LocalUser>
        mode="auto"
        autoThreshold={1000}
        loadAll={() => listUsers()}
        loadPage={loadPage}
        queryKey={["users"]}
        columns={columns}
        rowKey={(u) => String(u.id)}
        columnWidths={COLUMN_WIDTHS}
        initialSort={[{ id: "username", desc: false }]}
        searchPlaceholder="Search users…"
        filters={STATUS_FILTERS}
        onRowClick={(user) => setModal({ kind: "edit", user })}
        getRowAriaLabel={(user) => `Edit ${user.username}`}
        emptyMessage="No users found."
        loadingMessage="Loading users…"
        globalFilterFn={(u, search) => {
          const needle = search.toLowerCase();
          return `${u.username} ${u.firstName} ${u.lastName} ${u.email ?? ""}`
            .toLowerCase()
            .includes(needle);
        }}
        toolbarRight={
          <Button
            leftSection={<i className="fa fa-plus" />}
            onClick={() => setModal({ kind: "add" })}
          >
            Add user
          </Button>
        }
      />

      {modal.kind === "add" && (
        <AddUserModal
          onClose={close}
          onSuccess={(u) => {
            setFlash({ kind: "success", message: `Added ${u.username}.` });
            close();
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
      {modal.kind === "edit" && (
        <EditUserModal
          user={modal.user}
          canUnlock={canUnlock}
          onClose={close}
          onSuccess={(u) => {
            setFlash({ kind: "success", message: `Updated ${u.username}.` });
            close();
          }}
          onUnlocked={(u) => {
            setFlash({ kind: "success", message: `Unlocked ${u.username}.` });
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
      {modal.kind === "reset" && (
        <ResetPasswordModal
          user={modal.user}
          onClose={close}
          onSuccess={() => {
            setFlash({ kind: "success", message: `Reset password for ${modal.user.username}.` });
            close();
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
      {modal.kind === "delete" && (
        <DeleteUserModal
          user={modal.user}
          onClose={close}
          onSuccess={() => {
            setFlash({ kind: "success", message: `Deleted ${modal.user.username}.` });
            close();
          }}
          onError={(m) => setFlash({ kind: "error", message: m })}
        />
      )}
    </>
  );
}

function UserAvatar({ isAdmin }: { isAdmin: boolean }) {
  const label = isAdmin ? "Admin" : "User";
  return (
    <span
      className={`user-avatar user-avatar-${isAdmin ? "admin" : "user"}`}
      title={label}
      aria-label={label}
    >
      {isAdmin ? "A" : "U"}
    </span>
  );
}

function StatusDot({ status }: { status: UserStatus }) {
  return (
    <span className="manage-users-status">
      <span
        className={`manage-users-status-indicator manage-users-status-${status.toLowerCase()}`}
        aria-hidden="true"
      />
      {status}
    </span>
  );
}

type AddProps = {
  onClose: () => void;
  onSuccess: (user: LocalUser) => void;
  onError: (message: string) => void;
};

function AddUserModal({ onClose, onSuccess, onError }: AddProps) {
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<CreateUserForm>({
    resolver: zodResolver(createUserSchema),
    defaultValues: { username: "", firstName: "", lastName: "", password: "", email: "" }
  });
  const create = useCreateUser();

  const onSubmit = async (values: CreateUserForm) => {
    try {
      const user = await create.mutateAsync({
        username: values.username,
        firstName: values.firstName,
        lastName: values.lastName,
        password: values.password,
        email: values.email || undefined
      });
      onSuccess(user);
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title="Add User">
      <Box component="form" onSubmit={handleSubmit(onSubmit)}>
        <Stack gap="md">
          <TextInput label="Username" error={errors.username?.message} {...register("username")} />
          <TextInput label="First Name" error={errors.firstName?.message} {...register("firstName")} />
          <TextInput label="Last Name" error={errors.lastName?.message} {...register("lastName")} />
          <TextInput
            label="Email"
            type="email"
            error={errors.email?.message}
            {...register("email")}
          />
          <TextInput
            label="Password"
            type="password"
            error={errors.password?.message}
            {...register("password")}
          />
        </Stack>
        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={isSubmitting}>
            Add User
          </Button>
        </Group>
      </Box>
    </Modal>
  );
}

type EditProps = AddProps & {
  user: LocalUser;
  canUnlock: boolean;
  onUnlocked: (user: LocalUser) => void;
};

function EditUserModal({ user, canUnlock, onClose, onSuccess, onUnlocked, onError }: EditProps) {
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<EditUserForm>({
    resolver: zodResolver(editUserSchema),
    defaultValues: {
      username: user.username,
      firstName: user.firstName,
      lastName: user.lastName,
      email: user.email ?? ""
    }
  });
  const update = useUpdateUser();
  const unlock = useUnlockUser();
  const [isLocked, setIsLocked] = useState(user.isLocked);

  const onSubmit = async (values: EditUserForm) => {
    try {
      const updated = await update.mutateAsync({ id: user.id, request: values });
      onSuccess(updated);
    } catch (err) {
      onError(describeError(err));
    }
  };

  const onToggleLocked = async () => {
    if (!isLocked) return;
    try {
      const updated = await unlock.mutateAsync(user.id);
      setIsLocked(false);
      onUnlocked(updated);
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title={`Edit ${user.username}`}>
      <Box component="form" onSubmit={handleSubmit(onSubmit)}>
        <Stack gap="md">
          <TextInput label="Username" error={errors.username?.message} {...register("username")} />
          <TextInput label="First Name" error={errors.firstName?.message} {...register("firstName")} />
          <TextInput label="Last Name" error={errors.lastName?.message} {...register("lastName")} />
          <TextInput
            label="Email"
            type="email"
            error={errors.email?.message}
            {...register("email")}
          />
          <Box>
            <Text size="sm" fw={500} mb={4}>
              Account locked
            </Text>
            <Switch
              id="account-locked-switch"
              checked={isLocked}
              disabled={!isLocked || !canUnlock || unlock.isPending}
              onChange={onToggleLocked}
              label={
                isLocked
                  ? canUnlock
                    ? "Locked — toggle off to unlock"
                    : "Locked (you don't have permission to unlock)"
                  : "Active"
              }
            />
            {user.failedLoginAttempts > 0 && isLocked && (
              <Text size="sm" c="dimmed" mt={4}>
                {user.failedLoginAttempts} failed login attempts recorded.
              </Text>
            )}
          </Box>
        </Stack>
        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" loading={isSubmitting}>
            Save
          </Button>
        </Group>
      </Box>
    </Modal>
  );
}

type ResetProps = {
  user: LocalUser;
  onClose: () => void;
  onSuccess: () => void;
  onError: (message: string) => void;
};

function ResetPasswordModal({ user, onClose, onSuccess, onError }: ResetProps) {
  const {
    register,
    handleSubmit,
    formState: { errors, isSubmitting }
  } = useForm<ResetPasswordForm>({
    resolver: zodResolver(resetPasswordSchema),
    defaultValues: { password: "" }
  });
  const reset = useResetUserPassword();

  const onSubmit = async (values: ResetPasswordForm) => {
    try {
      await reset.mutateAsync({ id: user.id, password: values.password });
      onSuccess();
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title={`Reset password for ${user.username}`}>
      <Box component="form" onSubmit={handleSubmit(onSubmit)}>
        <TextInput
          label="New password"
          type="password"
          error={errors.password?.message}
          {...register("password")}
        />
        <Group justify="flex-end" mt="md" gap="xs">
          <Button variant="default" onClick={onClose}>
            Cancel
          </Button>
          <Button type="submit" color="yellow" loading={isSubmitting}>
            Reset password
          </Button>
        </Group>
      </Box>
    </Modal>
  );
}

type DeleteProps = {
  user: LocalUser;
  onClose: () => void;
  onSuccess: () => void;
  onError: (message: string) => void;
};

function DeleteUserModal({ user, onClose, onSuccess, onError }: DeleteProps) {
  const del = useDeleteUser();

  const onConfirm = async () => {
    try {
      await del.mutateAsync(user.id);
      onSuccess();
    } catch (err) {
      onError(describeError(err));
    }
  };

  return (
    <Modal opened onClose={onClose} title={`Delete ${user.username}?`}>
      <Text>
        This will permanently delete <strong>{user.username}</strong>. The action cannot be undone.
      </Text>
      <Group justify="flex-end" mt="md" gap="xs">
        <Button variant="default" onClick={onClose}>
          Cancel
        </Button>
        <Button color="red" onClick={onConfirm} loading={del.isPending}>
          Delete user
        </Button>
      </Group>
    </Modal>
  );
}

function formatLastLogin(value: string | null): string {
  if (!value) return "Never";
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return value;
  }
  return date.toLocaleString();
}

function describeError(error: unknown): string {
  if (error instanceof Error) {
    const response = (error as { response?: { data?: { message?: string } } }).response;
    return response?.data?.message ?? error.message;
  }
  return String(error);
}
