import { useQuery } from "@tanstack/react-query";
import { fetchCurrentUser } from "@/api/auth";
import { CurrentUser } from "@/types/flowable";

export const ME_QUERY_KEY = ["auth", "me"] as const;

export function useMe() {
  return useQuery<CurrentUser>({
    queryKey: ME_QUERY_KEY,
    queryFn: ({ signal }) => fetchCurrentUser(signal),
    staleTime: 5 * 60_000
  });
}
