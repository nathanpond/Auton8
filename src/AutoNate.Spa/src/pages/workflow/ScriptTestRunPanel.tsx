import { useCallback, useId, useState } from "react";
import { Alert, Badge, Box, Button, Code, Group, Stack, Text, Textarea } from "@mantine/core";
import { runScriptTest, type ScriptTestRunResponse } from "@/api/workflowScriptTest";

// #152: the script author's test environment.
//
// Scripts are the one place an author writes free-form logic, so they are where
// the gap between "I wrote it" and "it works" is widest. Before this, finding
// out meant publishing and running a real process.
//
// Two properties make this worth having rather than misleading:
//
//   * it runs in the SAME sandbox as production — the endpoint calls the same
//     runner the Flowable callback calls, so a test environment cannot be more
//     permissive than the real one and teach an author the wrong thing;
//   * a sandbox refusal is shown as a refusal, not as a bug. Reaching for
//     `Java.type` produces a bare "Java is not defined", which reads like a
//     missing dependency; this is where an author should learn it is a
//     boundary.
//
// Nothing here starts a process instance or persists anything.

/** Inputs are entered as JSON, which carries every type the sandbox round-trips. */
const InitialInputs = "{\n  \n}";

export function ScriptTestRunPanel({ script }: { script: string }) {
  const [inputsText, setInputsText] = useState(InitialInputs);
  const [inputsError, setInputsError] = useState<string | null>(null);
  const [running, setRunning] = useState(false);
  const [response, setResponse] = useState<ScriptTestRunResponse | null>(null);
  const [transportError, setTransportError] = useState<string | null>(null);

  const inputsId = useId();
  const outputId = useId();

  // Parsed as the author types so a malformed value is reported at entry
  // rather than surfacing later as a confusing script failure.
  const handleInputsChange = useCallback((value: string) => {
    setInputsText(value);
    if (value.trim() === "") {
      setInputsError(null);
      return;
    }
    try {
      const parsed: unknown = JSON.parse(value);
      if (parsed === null || typeof parsed !== "object" || Array.isArray(parsed)) {
        setInputsError("Input variables must be a JSON object, e.g. { \"price\": 10 }.");
        return;
      }
      setInputsError(null);
    } catch (e) {
      setInputsError(e instanceof Error ? e.message : "That is not valid JSON.");
    }
  }, []);

  const run = useCallback(async () => {
    let variables: Record<string, unknown> = {};
    if (inputsText.trim() !== "") {
      try {
        variables = JSON.parse(inputsText) as Record<string, unknown>;
      } catch {
        setInputsError("That is not valid JSON.");
        return;
      }
    }
    setRunning(true);
    setTransportError(null);
    setResponse(null);
    try {
      setResponse(await runScriptTest(script, variables));
    } catch (e) {
      setTransportError(e instanceof Error ? e.message : "The test run could not be sent.");
    } finally {
      setRunning(false);
    }
  }, [inputsText, script]);

  const changed = response?.changed ?? [];
  const mutations = response?.mutations ?? {};

  return (
    <Stack gap="sm">
      <Group justify="space-between" align="flex-end" wrap="wrap">
        <Text size="sm" fw={500} component="label" htmlFor={inputsId}>
          Test run — input variables (JSON)
        </Text>
        <Button
          size="xs"
          onClick={() => void run()}
          loading={running}
          disabled={running || inputsError !== null || script.trim() === ""}
        >
          Run script
        </Button>
      </Group>

      <Textarea
        id={inputsId}
        value={inputsText}
        onChange={(e) => handleInputsChange(e.currentTarget.value)}
        autosize
        minRows={3}
        maxRows={10}
        error={inputsError}
        spellCheck={false}
        styles={{ input: { fontFamily: "var(--mantine-font-family-monospace)" } }}
      />

      <Text size="xs" c="dimmed">
        Runs in the same sandbox as a published workflow. No process is started and nothing is
        saved.
      </Text>

      {/* Results are announced, so a screen-reader user is not left waiting
          for a change they cannot see. */}
      <Box id={outputId} aria-live="polite">
        {transportError !== null && (
          <Alert color="red" title="The test run could not be sent">
            {transportError}
          </Alert>
        )}

        {response?.ok === true && (
          <Stack gap="xs">
            <Alert color="green" title="Script ran">
              {changed.length === 0 ? (
                <Text size="sm">The script changed no variables.</Text>
              ) : (
                <Stack gap={4}>
                  <Text size="sm">Variables the script changed:</Text>
                  {changed.map((name) => (
                    <Group key={name} gap="xs" wrap="nowrap">
                      <Badge size="sm" variant="light">
                        {name}
                      </Badge>
                      <Code>{JSON.stringify(mutations[name])}</Code>
                    </Group>
                  ))}
                </Stack>
              )}
              {response.result !== null && response.result !== undefined && (
                <Text size="sm" mt="xs">
                  Returned <Code>{JSON.stringify(response.result)}</Code>
                </Text>
              )}
            </Alert>
          </Stack>
        )}

        {response?.ok === false && response.errorKind === "sandbox_refusal" && (
          // Deliberately not styled as an error: the sandbox did its job. An
          // author who reads this as a bug goes looking for one that is not
          // there.
          <Alert color="yellow" title="Blocked by the script sandbox">
            {response.errorMessage}
          </Alert>
        )}

        {response?.ok === false && response.errorKind === "script_error" && (
          <Alert color="red" title="The script failed">
            <Code block>{response.errorMessage}</Code>
          </Alert>
        )}

        {response?.ok === false && response.errorKind === "executor_unavailable" && (
          <Alert color="orange" title="The sandbox could not be reached">
            {response.errorMessage} Your script was not run, so this says nothing about whether it
            works.
          </Alert>
        )}
      </Box>
    </Stack>
  );
}
