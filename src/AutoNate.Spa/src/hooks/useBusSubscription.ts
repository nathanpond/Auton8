import { useEffect, useRef } from "react";
import {
  SubscriptionEvent,
  SubscriptionHandler,
  getSubscriptionClient,
} from "@/lib/ws/subscription";

export type UseBusSubscriptionOptions = {
  enabled?: boolean;
};

// Subscribes to one or more channels on the shared bus connection for the
// lifetime of the component. The handler ref is captured per-render so the
// channel set drives subscribe/unsubscribe activity, not handler-identity
// changes. The `channels` array is shallow-compared via join — pass a stable
// order so re-renders with the same channels don't cause churn.
export function useBusSubscription(
  channels: string[],
  handler: (event: SubscriptionEvent) => void,
  options: UseBusSubscriptionOptions = {},
): void {
  const { enabled = true } = options;
  const handlerRef = useRef(handler);
  handlerRef.current = handler;
  const channelsKey = channels.join("|");

  useEffect(() => {
    if (!enabled || channels.length === 0) return;
    const stableHandler: SubscriptionHandler = (event) => {
      handlerRef.current(event);
    };
    const dispose = getSubscriptionClient().subscribe(channels, stableHandler);
    return dispose;
    // eslint-disable-next-line react-hooks/exhaustive-deps -- channelsKey is the change signal
  }, [channelsKey, enabled]);
}
