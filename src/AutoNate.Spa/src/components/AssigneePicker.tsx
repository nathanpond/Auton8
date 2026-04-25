import { useMemo } from "react";
import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";
import { useUsers } from "@/hooks/useUsers";

type Props = {
  value: string[];
  onChange: (ids: string[]) => void;
  disabled?: boolean;
};

export default function AssigneePicker({ value, onChange, disabled }: Props) {
  const { data: users = [] } = useUsers();
  const directory = useUserDirectory();

  const selectedLower = useMemo(() => new Set(value.map((id) => id.toLowerCase())), [value]);

  const available = useMemo(
    () =>
      users
        .filter((u) => !selectedLower.has(u.userId.toLowerCase()))
        .sort((a, b) => {
          const an = userDisplayName(a) ?? a.username;
          const bn = userDisplayName(b) ?? b.username;
          return an.localeCompare(bn);
        }),
    [users, selectedLower]
  );

  const remove = (id: string) => {
    onChange(value.filter((v) => v.toLowerCase() !== id.toLowerCase()));
  };

  return (
    <div>
      <div className="d-flex flex-wrap gap-2 mb-2">
        {value.length === 0 ? (
          <span className="text-body text-opacity-50 small">No one assigned</span>
        ) : (
          value.map((id) => {
            const u = directory.get(id);
            const name = userDisplayName(u) ?? `${id.substring(0, 8)}`;
            return (
              <span
                key={id}
                className="badge bg-secondary d-inline-flex align-items-center gap-2"
              >
                <span>{name}</span>
                <button
                  type="button"
                  className="btn-close btn-close-white"
                  aria-label={`Remove ${name}`}
                  onClick={() => remove(id)}
                  disabled={disabled}
                  style={{ fontSize: "0.55rem" }}
                />
              </span>
            );
          })
        )}
      </div>
      <select
        className="form-select"
        value=""
        onChange={(e) => {
          const id = e.target.value;
          if (!id) return;
          if (!selectedLower.has(id.toLowerCase())) {
            onChange([...value, id]);
          }
        }}
        disabled={disabled || available.length === 0}
      >
        <option value="">
          {available.length === 0 ? "All users assigned" : "Add assignee…"}
        </option>
        {available.map((u) => {
          const name = userDisplayName(u) ?? u.username;
          return (
            <option key={u.userId} value={u.userId}>
              {name}
            </option>
          );
        })}
      </select>
    </div>
  );
}
