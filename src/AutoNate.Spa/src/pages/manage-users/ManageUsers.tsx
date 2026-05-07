import { useMemo, useState } from "react";
import { ColumnDef } from "@tanstack/react-table";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
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

// Status is derived from the LocalUser flags. There is no "Disabled" field on
// the data model today, so that filter never matches — kept here so the chip
// row matches the spec and lights up automatically once the field exists.
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

  // Kind-level check for the unlock switch on the edit modal. Backend gate
  // uses id="*" (RequireKindPermissionFilter), so the SPA mirrors that.
  const unlockCheck = useMemo(
    () => [{ kind: "user", action: "unlock", id: "*" }],
    []
  );
  const { data: unlockPermissions } = usePermissionChecks(unlockCheck);
  const canUnlock = unlockPermissions?.get(permissionKey(unlockCheck[0])) ?? false;

  // Admin badge: light up the avatar for direct SuperAdmin role assignees.
  // Group-mediated admin membership isn't reflected here — the badge is a
  // hint, not an authorization check.
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
          <span className={row.original.lastLoginDate ? "manage-users-last-login" : "manage-users-last-login-never"}>
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
          <div className="data-table-row-actions">
            <button
              type="button"
              className="btn btn-icon"
              title="Reset password"
              aria-label={`Reset password for ${row.original.username}`}
              onClick={(e) => {
                e.stopPropagation();
                setModal({ kind: "reset", user: row.original });
              }}
            >
              <i className="fa fa-key"></i>
            </button>
            <button
              type="button"
              className="btn btn-icon btn-icon-danger"
              title="Delete user"
              aria-label={`Delete ${row.original.username}`}
              onClick={(e) => {
                e.stopPropagation();
                setModal({ kind: "delete", user: row.original });
              }}
            >
              <i className="fa fa-trash"></i>
            </button>
          </div>
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
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Manage Users</h1>
          <p className="page-head-copy">
            Manage local users with search, sorting, paging, and quick account actions.
          </p>
        </div>
      </div>

      {flash && (
        <div
          className={`alert ${flash.kind === "success" ? "alert-success" : "alert-danger"}`}
          role={flash.kind === "success" ? "status" : "alert"}
        >
          {flash.message}
        </div>
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
          <button
            type="button"
            className="btn btn-add-user"
            onClick={() => setModal({ kind: "add" })}
          >
            <i className="fa fa-plus me-2"></i>Add user
          </button>
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
      <span className={`manage-users-status-indicator manage-users-status-${status.toLowerCase()}`} aria-hidden="true" />
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
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<CreateUserForm>({
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
    <ModalShell title="Add User" onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)}>
        <div className="modal-body">
          <FormField label="Username" error={errors.username?.message}>
            <input className="form-control" {...register("username")} />
          </FormField>
          <FormField label="First Name" error={errors.firstName?.message}>
            <input className="form-control" {...register("firstName")} />
          </FormField>
          <FormField label="Last Name" error={errors.lastName?.message}>
            <input className="form-control" {...register("lastName")} />
          </FormField>
          <FormField label="Email" error={errors.email?.message}>
            <input className="form-control" type="email" {...register("email")} />
          </FormField>
          <FormField label="Password" error={errors.password?.message}>
            <input className="form-control" type="password" {...register("password")} />
          </FormField>
        </div>
        <div className="modal-footer">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            Add User
          </button>
        </div>
      </form>
    </ModalShell>
  );
}

type EditProps = AddProps & {
  user: LocalUser;
  canUnlock: boolean;
  onUnlocked: (user: LocalUser) => void;
};

function EditUserModal({ user, canUnlock, onClose, onSuccess, onUnlocked, onError }: EditProps) {
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<EditUserForm>({
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
    // Lockout is set automatically after 3 failed logins. The switch only
    // ever flips locked → unlocked; flipping back is not exposed.
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
    <ModalShell title={`Edit ${user.username}`} onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)}>
        <div className="modal-body">
          <FormField label="Username" error={errors.username?.message}>
            <input className="form-control" {...register("username")} />
          </FormField>
          <FormField label="First Name" error={errors.firstName?.message}>
            <input className="form-control" {...register("firstName")} />
          </FormField>
          <FormField label="Last Name" error={errors.lastName?.message}>
            <input className="form-control" {...register("lastName")} />
          </FormField>
          <FormField label="Email" error={errors.email?.message}>
            <input className="form-control" type="email" {...register("email")} />
          </FormField>
          <div className="mb-3">
            <label className="form-label">Account locked</label>
            <div className="form-check form-switch">
              <input
                id="account-locked-switch"
                className="form-check-input"
                type="checkbox"
                role="switch"
                checked={isLocked}
                disabled={!isLocked || !canUnlock || unlock.isPending}
                onChange={onToggleLocked}
              />
              <label className="form-check-label" htmlFor="account-locked-switch">
                {isLocked
                  ? canUnlock
                    ? "Locked — toggle off to unlock"
                    : "Locked (you don't have permission to unlock)"
                  : "Active"}
              </label>
            </div>
            {user.failedLoginAttempts > 0 && isLocked && (
              <div className="text-body-secondary small mt-1">
                {user.failedLoginAttempts} failed login attempts recorded.
              </div>
            )}
          </div>
        </div>
        <div className="modal-footer">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="btn btn-primary" disabled={isSubmitting}>
            Save
          </button>
        </div>
      </form>
    </ModalShell>
  );
}

type ResetProps = {
  user: LocalUser;
  onClose: () => void;
  onSuccess: () => void;
  onError: (message: string) => void;
};

function ResetPasswordModal({ user, onClose, onSuccess, onError }: ResetProps) {
  const { register, handleSubmit, formState: { errors, isSubmitting } } = useForm<ResetPasswordForm>({
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
    <ModalShell title={`Reset password for ${user.username}`} onClose={onClose}>
      <form onSubmit={handleSubmit(onSubmit)}>
        <div className="modal-body">
          <FormField label="New password" error={errors.password?.message}>
            <input className="form-control" type="password" {...register("password")} />
          </FormField>
        </div>
        <div className="modal-footer">
          <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="btn btn-warning" disabled={isSubmitting}>
            Reset password
          </button>
        </div>
      </form>
    </ModalShell>
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
    <ModalShell title={`Delete ${user.username}?`} onClose={onClose}>
      <div className="modal-body">
        <p className="mb-0">
          This will permanently delete <strong>{user.username}</strong>. The action cannot be undone.
        </p>
      </div>
      <div className="modal-footer">
        <button type="button" className="btn btn-outline-secondary" onClick={onClose}>
          Cancel
        </button>
        <button type="button" className="btn btn-danger" onClick={onConfirm} disabled={del.isPending}>
          Delete user
        </button>
      </div>
    </ModalShell>
  );
}

function ModalShell({
  title,
  onClose,
  children
}: {
  title: string;
  onClose: () => void;
  children: React.ReactNode;
}) {
  return (
    <>
      <div className="modal fade show d-block" tabIndex={-1} role="dialog" aria-modal="true">
        <div className="modal-dialog">
          <div className="modal-content">
            <div className="modal-header">
              <h5 className="modal-title">{title}</h5>
              <button type="button" className="btn-close" aria-label="Close" onClick={onClose} />
            </div>
            {children}
          </div>
        </div>
      </div>
      <div className="modal-backdrop fade show" />
    </>
  );
}

function FormField({
  label,
  error,
  children
}: {
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="mb-3">
      <label className="form-label">{label}</label>
      {children}
      {error && <div className="text-danger small mt-1">{error}</div>}
    </div>
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
