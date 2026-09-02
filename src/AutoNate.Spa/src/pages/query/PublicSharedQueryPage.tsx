import { FormEvent, useCallback, useMemo, useState } from "react";
import { useParams, useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  Alert,
  Badge,
  Box,
  Button,
  Container,
  Group,
  Loader,
  Paper,
  Stack,
  Text,
  TextInput,
  Title
} from "@mantine/core";
import { DataTable, type DataTableColumn } from "@/components/data-table/DataTable";
import {
  extractMissingParamName,
  redeemSharedQuery,
  shareNotFound
} from "@/api/publicQueries";
import { useDocumentTitle } from "@/hooks/useDocumentTitle";

// Public share recipient surface (audit fix archived-9). Mounted OUTSIDE the
// AppShell so anonymous recipients land on a clean page with no nav
// chrome — the URL is bookmarkable and pasteable into Slack / email
// without auth. The token comes from the path; declared `:param` values
// flow through the URL's query string so the link is the entire
// shareable artifact.
//
// Missing-parameter behavior: when the AQL references a `:name` the
// recipient didn't supply, the backend returns a 400 with a stable
// message we parse to render a fill-in form. Submitting the form
// writes the value into useSearchParams (so a refresh / re-paste
// retains it) and the query refetches automatically because the
// search-params string is part of the React Query key.
type IndexedRow = Record<string, unknown> & { __rowId: string };

export default function PublicSharedQueryPage() {
  const { token = "" } = useParams<{ token: string }>();
  const [searchParams, setSearchParams] = useSearchParams();

  // Collect all non-empty query-string entries as `name → value`. The
  // backend strips a leading `:` either way; we leave the key form to
  // whatever the issuer pasted into the URL.
  const params = useMemo(() => {
    const out: Record<string, string> = {};
    for (const [k, v] of searchParams.entries()) {
      if (k.length === 0) continue;
      out[k] = v;
    }
    return out;
  }, [searchParams]);

  useDocumentTitle("Shared query");

  const shareQuery = useQuery({
    queryKey: ["public-shared-query", token, params],
    queryFn: ({ signal }) => redeemSharedQuery(token, params, signal),
    enabled: token !== "",
    retry: false,
    refetchOnWindowFocus: false
  });

  const missingParam = useMemo(
    () => (shareQuery.error ? extractMissingParamName(shareQuery.error) : null),
    [shareQuery.error]
  );
  const notFound = useMemo(
    () => (shareQuery.error ? shareNotFound(shareQuery.error) : false),
    [shareQuery.error]
  );

  const indexedRows = useMemo<IndexedRow[]>(() => {
    if (!shareQuery.data) return [];
    return shareQuery.data.rows.map((row, idx) => ({
      __rowId: String(idx),
      ...row
    }));
  }, [shareQuery.data]);

  const columns = useMemo<DataTableColumn<IndexedRow>[]>(() => {
    if (!shareQuery.data) return [];
    return shareQuery.data.columns.map((col) => ({
      id: col.name,
      accessorFn: (row) => row[col.name],
      header: col.name,
      enableSorting: true,
      cell: ({ row }) => renderCell(row.original[col.name])
    }));
  }, [shareQuery.data]);

  // Even-distribution column widths matching the in-app QueryPage's
  // sizing logic — DataTable requires the prop and even percentages
  // are the right default for an arbitrary AQL projection.
  const columnWidths = useMemo(() => {
    if (!shareQuery.data) return [] as string[];
    const n = shareQuery.data.columns.length;
    if (n === 0) return [] as string[];
    const pct = Math.max(8, Math.floor(100 / n));
    return Array<string>(n).fill(`${pct}%`);
  }, [shareQuery.data]);

  const loadAll = useCallback(async () => indexedRows, [indexedRows]);

  return (
    <Container size="lg" py="xl">
      <Stack gap="md">
        <Title order={1}>Shared query</Title>

        {shareQuery.isLoading ? (
          <Group justify="center" py="xl">
            <Loader />
            <Text c="dimmed">Running query…</Text>
          </Group>
        ) : null}

        {notFound ? (
          <Alert color="red" title="Link not available">
            This share link is invalid, expired, revoked, or has reached its use cap.
            Ask the person who sent it for a fresh link.
          </Alert>
        ) : null}

        {missingParam ? (
          <MissingParamForm
            name={missingParam}
            currentValue={params[missingParam] ?? ""}
            onSubmit={(value) => {
              const next = new URLSearchParams(searchParams);
              if (value === "") next.delete(missingParam);
              else next.set(missingParam, value);
              setSearchParams(next, { replace: true });
            }}
          />
        ) : null}

        {shareQuery.error && !notFound && !missingParam ? (
          <Alert color="red" title="Couldn't load the query">
            {describeError(shareQuery.error)}
          </Alert>
        ) : null}

        {shareQuery.data ? (
          <Paper p="md" withBorder>
            <Stack gap="sm">
              <Group justify="space-between" align="center">
                <Group gap="xs">
                  <Badge variant="light">{shareQuery.data.rows.length} rows</Badge>
                  <Badge variant="light" color="gray">
                    {shareQuery.data.columns.length} columns
                  </Badge>
                  <Text size="xs" c="dimmed">
                    Executed in {shareQuery.data.durationMs} ms
                  </Text>
                </Group>
              </Group>

              {shareQuery.data.truncated ? (
                <Alert color="yellow" title="Results were truncated">
                  Showing the first {shareQuery.data.rows.length} rows. The full result
                  set is larger than the public-share cap.
                </Alert>
              ) : null}

              {shareQuery.data.columns.length === 0 ? (
                <Text c="dimmed" size="sm">
                  The query returned no columns.
                </Text>
              ) : (
                <DataTable<IndexedRow>
                  mode="client"
                  queryKey={["public-shared-query-rows", token, params]}
                  loadAll={loadAll}
                  columns={columns}
                  columnWidths={columnWidths}
                  rowKey={(row) => row.__rowId}
                  emptyMessage="Query returned no rows."
                />
              )}
            </Stack>
          </Paper>
        ) : null}
      </Stack>
    </Container>
  );
}

function MissingParamForm({
  name,
  currentValue,
  onSubmit
}: {
  name: string;
  currentValue: string;
  onSubmit: (value: string) => void;
}) {
  const [draft, setDraft] = useState(currentValue);

  function submit(e: FormEvent) {
    e.preventDefault();
    if (draft.trim().length === 0) return;
    onSubmit(draft.trim());
  }

  return (
    <Paper p="md" withBorder>
      <Stack gap="sm" component="form" onSubmit={submit}>
        <Text fw={500}>Fill in the missing parameter</Text>
        <Text size="sm" c="dimmed">
          This shared query expects a value for{" "}
          <Box component="code" style={{ fontFamily: "var(--mantine-font-family-monospace)" }}>
            :{name}
          </Box>
          . The page reloads with the new value appended to the URL, so the resulting
          link can be reshared.
        </Text>
        <TextInput
          label={`:${name}`}
          required
          value={draft}
          onChange={(e) => setDraft(e.currentTarget.value)}
          data-autofocus
        />
        <Group justify="flex-end">
          <Button type="submit" disabled={draft.trim().length === 0}>
            Run query
          </Button>
        </Group>
      </Stack>
    </Paper>
  );
}

// Lightweight cell renderer — public recipients don't have deep-link
// access to the host's records / records / workflows, so we render
// values as text and leave intelligent linking to the in-app
// /query page. Matches the AqlQueryResponse rows[] shape (any).
function renderCell(value: unknown): string {
  if (value === null || value === undefined) return "—";
  if (typeof value === "string") return value;
  if (typeof value === "number" || typeof value === "boolean") return String(value);
  try {
    return JSON.stringify(value);
  } catch {
    return String(value);
  }
}

function describeError(err: unknown): string {
  if (err && typeof err === "object" && "response" in err) {
    const resp = (err as { response?: { data?: { reason?: string } } }).response;
    if (typeof resp?.data?.reason === "string") return resp.data.reason;
  }
  return err instanceof Error ? err.message : "Unknown error.";
}
