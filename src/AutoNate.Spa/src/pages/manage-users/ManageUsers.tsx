import { useMemo, useState } from "react";
import {
  ColumnDef,
  PaginationState,
  SortingState,
  flexRender,
  getCoreRowModel,
  getFilteredRowModel,
  getPaginationRowModel,
  getSortedRowModel,
  useReactTable
} from "@tanstack/react-table";
import { useForm } from "react-hook-form";
import { zodResolver } from "@hookform/resolvers/zod";
import {
  useCreateUser,
  useDeleteUser,
  useResetUserPassword,
  useUpdateUser,
  useUsers
} from "@/hooks/useUsers";
import { LocalUser } from "@/types/flowable";
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

const PAGE_SIZE_OPTIONS = [10, 25, 50, 100];

export default function ManageUsers() {
  const { data: users = [], isLoading } = useUsers();
  const [modal, setModal] = useState<ModalState>({ kind: "none" });
  const [flash, setFlash] = useState<{ kind: "success" | "error"; message: string } | null>(null);
  const [globalFilter, setGlobalFilter] = useState("");
  const [sorting, setSorting] = useState<SortingState>([{ id: "username", desc: false }]);
  const [pagination, setPagination] = useState<PaginationState>({ pageIndex: 0, pageSize: 25 });

  const columns = useMemo<ColumnDef<LocalUser>[]>(
    () => [
      {
        id: "username",
        accessorKey: "username",
        header: "Username",
        cell: ({ row }) => (
          <button
            type="button"
            className="btn btn-link p-0 text-decoration-none fw-semibold align-baseline"
            onClick={() => setModal({ kind: "edit", user: row.original })}
          >
            {row.original.username}
          </button>
        )
      },
      {
        id: "fullName",
        header: "Full Name",
        accessorFn: (u) => `${u.firstName} ${u.lastName}`.trim()
      },
      {
        id: "lastName",
        accessorKey: "lastName",
        header: "Last Name"
      },
      {
        id: "lastLogin",
        header: "Last Login",
        accessorFn: (u) => u.lastLoginDate ?? "",
        cell: ({ row }) => formatLastLogin(row.original.lastLoginDate)
      },
      {
        id: "actions",
        header: "Actions",
        enableSorting: false,
        enableGlobalFilter: false,
        cell: ({ row }) => (
          <div className="d-flex align-items-center gap-2">
            <button
              type="button"
              className="btn btn-outline-warning btn-sm"
              title="Reset password"
              onClick={() => setModal({ kind: "reset", user: row.original })}
            >
              <i className="fa fa-key"></i>
            </button>
            <button
              type="button"
              className="btn btn-outline-danger btn-sm"
              title="Delete user"
              onClick={() => setModal({ kind: "delete", user: row.original })}
            >
              <i className="fa fa-trash"></i>
            </button>
          </div>
        )
      }
    ],
    []
  );

  const table = useReactTable({
    data: users,
    columns,
    state: { sorting, globalFilter, pagination },
    onSortingChange: setSorting,
    onGlobalFilterChange: setGlobalFilter,
    onPaginationChange: setPagination,
    getCoreRowModel: getCoreRowModel(),
    getSortedRowModel: getSortedRowModel(),
    getFilteredRowModel: getFilteredRowModel(),
    getPaginationRowModel: getPaginationRowModel(),
    globalFilterFn: (row, _columnId, value) => {
      const needle = String(value).toLowerCase();
      const u = row.original;
      return `${u.username} ${u.firstName} ${u.lastName} ${u.email}`.toLowerCase().includes(needle);
    }
  });

  const { pageIndex, pageSize } = table.getState().pagination;
  const filteredCount = table.getFilteredRowModel().rows.length;
  const totalPages = table.getPageCount();
  const pageButtons = useMemo(() => buildPageWindow(pageIndex, totalPages, 7), [pageIndex, totalPages]);
  const close = () => setModal({ kind: "none" });

  return (
    <>
      <div className="page-head">
        <div>
          <h1 className="page-header mb-1">Manage Users</h1>
          <p className="page-head-copy">
            Manage local users with client-side search, sorting, paging, and quick account actions.
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

      <div className="panel panel-inverse">
        <div className="panel-heading">
          <h4 className="panel-title">Users</h4>
        </div>
        <div className="panel-body">
          <div className="manage-users-toolbar d-flex flex-column flex-lg-row justify-content-between align-items-lg-center gap-3 mb-3">
            <div className="manage-users-toolbar-start d-flex flex-column flex-sm-row align-items-sm-center gap-2">
              <button
                type="button"
                className="btn btn-primary"
                onClick={() => setModal({ kind: "add" })}
              >
                <i className="fa fa-plus me-2"></i>Add User
              </button>
            </div>
            <div className="manage-users-toolbar-end">
              <label className="d-flex align-items-center gap-2 mb-0">
                <span>Search:</span>
                <input
                  type="search"
                  className="form-control form-control-sm"
                  value={globalFilter}
                  onChange={(e) => {
                    setGlobalFilter(e.target.value);
                    table.setPageIndex(0);
                  }}
                />
              </label>
            </div>
          </div>

          <div className="table-responsive">
            <table
              id="manage-users-table"
              width="100%"
              className="table table-striped table-bordered align-middle text-nowrap"
            >
              <thead>
                {table.getHeaderGroups().map((headerGroup) => (
                  <tr key={headerGroup.id}>
                    {headerGroup.headers.map((header) => {
                      const canSort = header.column.getCanSort();
                      const sortDir = header.column.getIsSorted();
                      return (
                        <th
                          key={header.id}
                          className="text-nowrap"
                          onClick={canSort ? header.column.getToggleSortingHandler() : undefined}
                          style={canSort ? { cursor: "pointer", userSelect: "none" } : undefined}
                          aria-sort={
                            sortDir === "asc"
                              ? "ascending"
                              : sortDir === "desc"
                                ? "descending"
                                : canSort
                                  ? "none"
                                  : undefined
                          }
                        >
                          {header.isPlaceholder
                            ? null
                            : flexRender(header.column.columnDef.header, header.getContext())}
                          {canSort && <SortIndicator dir={sortDir || null} />}
                        </th>
                      );
                    })}
                  </tr>
                ))}
              </thead>
              <tbody>
                {isLoading && (
                  <tr>
                    <td colSpan={columns.length} className="text-center text-body text-opacity-50 p-4">
                      Loading users...
                    </td>
                  </tr>
                )}
                {!isLoading && table.getRowModel().rows.length === 0 && (
                  <tr>
                    <td colSpan={columns.length} className="text-center text-body text-opacity-50 p-4">
                      No users found.
                    </td>
                  </tr>
                )}
                {table.getRowModel().rows.map((row) => (
                  <tr key={row.id}>
                    {row.getVisibleCells().map((cell) => (
                      <td key={cell.id}>
                        {flexRender(cell.column.columnDef.cell, cell.getContext())}
                      </td>
                    ))}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>

          <div className="manage-users-footer d-flex flex-column flex-lg-row justify-content-between align-items-lg-center gap-3 mt-3">
            <div className="manage-users-footer-start d-flex flex-column flex-sm-row align-items-sm-center gap-3">
              <label className="manage-users-length d-flex align-items-center gap-2 mb-0">
                <select
                  className="form-select form-select-sm"
                  value={pageSize}
                  onChange={(e) => table.setPageSize(Number(e.target.value))}
                  style={{ width: "auto" }}
                >
                  {PAGE_SIZE_OPTIONS.map((n) => (
                    <option key={n} value={n}>
                      {n}
                    </option>
                  ))}
                </select>
                <span>entries per page</span>
              </label>
              <div className="manage-users-info">
                {filteredCount === 0
                  ? "Showing 0 entries"
                  : `Showing ${pageIndex * pageSize + 1} to ${Math.min(
                      (pageIndex + 1) * pageSize,
                      filteredCount
                    )} of ${filteredCount} entries`}
              </div>
            </div>
            <nav aria-label="Table pagination" className="manage-users-paging">
              <ul className="pagination pagination-sm mb-0">
                <li className={`page-item ${!table.getCanPreviousPage() ? "disabled" : ""}`}>
                  <button
                    type="button"
                    className="page-link"
                    onClick={() => table.previousPage()}
                    disabled={!table.getCanPreviousPage()}
                  >
                    Previous
                  </button>
                </li>
                {pageButtons.map((p) => (
                  <li key={p} className={`page-item ${p === pageIndex ? "active" : ""}`}>
                    <button
                      type="button"
                      className="page-link"
                      onClick={() => table.setPageIndex(p)}
                    >
                      {p + 1}
                    </button>
                  </li>
                ))}
                <li className={`page-item ${!table.getCanNextPage() ? "disabled" : ""}`}>
                  <button
                    type="button"
                    className="page-link"
                    onClick={() => table.nextPage()}
                    disabled={!table.getCanNextPage()}
                  >
                    Next
                  </button>
                </li>
              </ul>
            </nav>
          </div>
        </div>
      </div>

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
          onClose={close}
          onSuccess={(u) => {
            setFlash({ kind: "success", message: `Updated ${u.username}.` });
            close();
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

function SortIndicator({ dir }: { dir: "asc" | "desc" | null }) {
  if (dir === "asc") return <i className="fa fa-sort-up ms-1"></i>;
  if (dir === "desc") return <i className="fa fa-sort-down ms-1"></i>;
  return <i className="fa fa-sort ms-1 text-body text-opacity-25"></i>;
}

function buildPageWindow(pageIndex: number, totalPages: number, max: number): number[] {
  if (totalPages <= 0) return [0];
  const half = Math.floor(max / 2);
  let start = Math.max(0, pageIndex - half);
  const end = Math.min(totalPages, start + max);
  start = Math.max(0, end - max);
  const out: number[] = [];
  for (let i = start; i < end; i++) out.push(i);
  return out;
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

type EditProps = AddProps & { user: LocalUser };

function EditUserModal({ user, onClose, onSuccess, onError }: EditProps) {
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

  const onSubmit = async (values: EditUserForm) => {
    try {
      const updated = await update.mutateAsync({ id: user.id, request: values });
      onSuccess(updated);
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
