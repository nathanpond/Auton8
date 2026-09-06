import { api } from "./client";

// #152: run a script task against sample variables from the editor, before
// publishing. The endpoint routes to the same sandbox production uses, so what
// the author sees here is what a real execution will do.
export type ScriptTestRunResponse = {
  ok: boolean;
  result: unknown;
  mutations: Record<string, unknown> | null;
  /** Names the script actually changed, as opposed to the inputs it was given. */
  changed: string[] | null;
  /**
   * `sandbox_refusal` is the boundary working, not a bug in the script — the
   * panel says so differently, because that distinction is what an author
   * comes here to learn.
   */
  errorKind: "script_error" | "sandbox_refusal" | "executor_unavailable" | null;
  errorMessage: string | null;
};

export async function runScriptTest(
  code: string,
  variables: Record<string, unknown>,
  signal?: AbortSignal
): Promise<ScriptTestRunResponse> {
  const { data } = await api.post<ScriptTestRunResponse>(
    "/api/workflow-script-tasks/test-run",
    { code, variables },
    { signal }
  );
  return data;
}
