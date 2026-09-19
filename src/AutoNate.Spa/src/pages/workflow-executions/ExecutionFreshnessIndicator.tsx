import { Alert, Button, Group, Text } from "@mantine/core";
import { useExecutionFreshness } from "@/hooks/useExecutions";
import { describeFreshness, freshnessMessage } from "@/lib/executionFreshness";

interface Props {
  /** Refreshes the list itself; the indicator refreshes its own query. */
  onRefresh: () => void;
  refreshDisabled?: boolean;
}

/**
 * Says how current the executions view is, and whether it is still updating (#109).
 *
 * Making the cache the read model traded absolute freshness for a single source
 * of truth. That trade is only honest if a user can see it — otherwise the page
 * shows a moment that has passed and presents it as now.
 *
 * An in-page element rather than a toast, per the project's rule: this is a
 * condition belonging to the page. It is still true after a reload and should
 * still be there when the user comes back to look.
 */
export function ExecutionFreshnessIndicator({ onRefresh, refreshDisabled }: Props) {
  const { data, refetch, isFetching } = useExecutionFreshness();
  const view = describeFreshness(data, Date.now());
  const message = freshnessMessage(view);

  const handleRefresh = () => {
    onRefresh();
    void refetch();
  };

  return (
    // ONE PERSISTENT LIVE REGION, always rendered.
    //
    // A live region announces changes to its CONTENTS; one that appears at the
    // same moment as the text it holds may not be announced at all, because the
    // assistive technology has nothing to compare against. So the container is
    // always here and only the sentence inside it changes.
    //
    // `polite`, not `assertive`: this is a status, and interrupting whatever the
    // user is reading to say the data is a minute old would be worse than
    // useless.
    <Group
      gap="xs"
      align="center"
      wrap="nowrap"
      role="status"
      aria-live="polite"
      data-testid="execution-freshness"
    >
      {view.state === "not-updating" ? (
        // Distinct from ordinary staleness, which is the third criterion. "Not
        // updating" is not a slower version of "a minute old" -- the data will
        // not get any newer -- so it reads as a warning rather than as dimmed
        // text a user has to interpret.
        <Alert
          color="orange"
          variant="light"
          py={4}
          px="sm"
          // NOT role="alert", which is Mantine's default for Alert.
          //
          // Two things were wrong with the default. It nests an ASSERTIVE live
          // region inside the polite one above, so a state change would interrupt
          // whatever the user is reading -- the opposite of what the container is
          // for. And `role="alert"` on this page already means "an error banner is
          // showing": WorkflowOverrideTests asserts none is visible, and this
          // indicator made a status masquerade as one. That test caught it.
          //
          // The container owns announcement; this element is presentation.
          role="presentation"
          data-testid="execution-freshness-stopped"
        >
          <Text size="sm">{message}</Text>
        </Alert>
      ) : (
        <Text
          size="sm"
          c={view.state === "stale" ? "orange" : "dimmed"}
          data-testid="execution-freshness-message"
        >
          {message}
        </Text>
      )}

      <Button
        variant="subtle"
        size="compact-sm"
        onClick={handleRefresh}
        disabled={refreshDisabled || isFetching}
        data-testid="execution-freshness-refresh"
      >
        Refresh now
      </Button>
    </Group>
  );
}
