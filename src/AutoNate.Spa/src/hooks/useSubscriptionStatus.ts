import { useEffect, useState } from "react";
import { SubscriptionStatus, getSubscriptionClient } from "@/lib/ws/subscription";

// Live connection status of the shared /ws/bus-watcher client. Used by
// components (BusWatcher admin, WorkflowExecutions header) that show a
// connection chip.
export function useSubscriptionStatus(): SubscriptionStatus {
  const [status, setStatus] = useState<SubscriptionStatus>(() => getSubscriptionClient().getStatus());

  useEffect(() => {
    const client = getSubscriptionClient();
    setStatus(client.getStatus());
    const unsubscribe = client.onStatusChange(setStatus);
    return unsubscribe;
  }, []);

  return status;
}
