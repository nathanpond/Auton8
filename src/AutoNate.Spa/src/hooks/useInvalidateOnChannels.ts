import { useCallback } from "react";
import { QueryKey, useQueryClient } from "@tanstack/react-query";
import { useBusSubscription, UseBusSubscriptionOptions } from "./useBusSubscription";

// Common pattern for the migrated panels: when any event arrives on these
// channels, invalidate these react-query keys. Rejection / invalidate frames
// also trigger an invalidate so a permission revoke is reflected as the
// page's data refetches.
export function useInvalidateOnChannels(
  channels: string[],
  queryKeys: QueryKey[],
  options: UseBusSubscriptionOptions = {},
): void {
  const qc = useQueryClient();
  const handler = useCallback(
    () => {
      for (const key of queryKeys) {
        qc.invalidateQueries({ queryKey: key });
      }
    },
    // queryKeys array identity may change per render; stable invalidation
    // depends on the contents, not the wrapper.
    // eslint-disable-next-line react-hooks/exhaustive-deps
    [qc, JSON.stringify(queryKeys)],
  );
  useBusSubscription(channels, handler, options);
}
