import { useUserDirectory, userDisplayName } from "@/hooks/useUserDirectory";

type Props = {
  userId: string | null | undefined;
  /** When true, renders a leading "by" prefix. Useful in audit captions. */
  withByPrefix?: boolean;
};

/**
 * Renders a user reference (e.g. an audit-log actor) using the user's display
 * name when known, falling back to a short Guid suffix when the user
 * directory is loading or doesn't contain that id (e.g. deleted user).
 */
export default function UserBadge({ userId, withByPrefix }: Props) {
  const directory = useUserDirectory();

  if (!userId) {
    return <span className="text-body text-opacity-50">unknown</span>;
  }

  const user = directory.get(userId);
  const name = userDisplayName(user);

  if (name) {
    const username = user?.username ? `@${user.username}` : null;
    return (
      <span title={username ? `${name} (${username})` : userId}>
        {withByPrefix ? "by " : ""}
        {name}
      </span>
    );
  }

  // Fallback: short hex form so debugging is still possible. The full id is in
  // the title attribute for hover.
  return (
    <span title={userId} className="text-body text-opacity-75">
      {withByPrefix ? "by " : ""}
      <code>{userId.substring(0, 8)}</code>
    </span>
  );
}
