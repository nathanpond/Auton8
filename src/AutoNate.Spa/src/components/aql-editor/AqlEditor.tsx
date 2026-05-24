import { useCallback, useMemo, useRef, useState } from "react";
import CodeMirror, { type ReactCodeMirrorRef } from "@uiw/react-codemirror";
import { EditorView, keymap } from "@codemirror/view";
import { EditorState, Prec } from "@codemirror/state";
import { acceptCompletion, autocompletion, completionKeymap } from "@codemirror/autocomplete";
import { useQuery } from "@tanstack/react-query";
import {
  fetchAqlEntityContext,
  fetchAqlSchema,
  type AqlEntityContext
} from "@/api/aqlSchema";
import { buildAqlCompletionSource, type CompletionDeps } from "./aqlCompletions";

export type AqlEditorProps = {
  value: string;
  onChange: (next: string) => void;
  onExecute: () => void;
  readOnly?: boolean;
  minHeight?: string;
  maxHeight?: string;
  placeholder?: string;
};

// AQL query editor — CodeMirror 6 with a custom completion source.
// Replaces the plain Mantine Textarea that QueryPage used previously.
// Ctrl/Cmd+Enter executes; Ctrl+Space (and typing) opens the dropdown.
export default function AqlEditor({
  value,
  onChange,
  onExecute,
  readOnly = false,
  minHeight = "6em",
  maxHeight = "20em",
  placeholder = "FROM Records"
}: AqlEditorProps) {
  const editorRef = useRef<ReactCodeMirrorRef | null>(null);

  // Static schema — long staleTime, the catalog rarely changes.
  const schemaQuery = useQuery({
    queryKey: ["aql", "schema"],
    queryFn: ({ signal }) => fetchAqlSchema(signal),
    staleTime: 5 * 60_000,
    gcTime: 30 * 60_000
  });

  // The entity context the completion source has asked about. We track
  // a `(entity, recordType)` pair separately from the editor's text so
  // detecting a new entity in the middle of typing triggers a fresh
  // fetch without re-running expensive logic.
  const [activeCtxKey, setActiveCtxKey] = useState<{
    entity: string;
    recordType: string | null;
  }>({ entity: "Records", recordType: null });

  const entityCtxQuery = useQuery<AqlEntityContext>({
    queryKey: ["aql", "entity-context", activeCtxKey.entity, activeCtxKey.recordType ?? "_all"],
    queryFn: ({ signal }) =>
      fetchAqlEntityContext(activeCtxKey.entity, activeCtxKey.recordType, signal),
    staleTime: 60_000,
    gcTime: 5 * 60_000,
    enabled: Boolean(activeCtxKey.entity)
  });

  // The completion source closes over a getter so it always sees the
  // latest snapshot from react-query rather than the snapshot at mount.
  const depsRef = useRef<CompletionDeps>({
    schema: null,
    entityContext: null,
    requestEntityContext: () => undefined
  });
  depsRef.current = {
    schema: schemaQuery.data ?? null,
    entityContext: entityCtxQuery.data ?? null,
    requestEntityContext: (entity, recordType) => {
      // setState is a no-op when the key already matches; this also
      // primes the react-query cache via queryFn.
      setActiveCtxKey((cur) => {
        if (cur.entity.toLowerCase() === entity.toLowerCase()
            && (cur.recordType ?? null) === (recordType ?? null)) {
          return cur;
        }
        return { entity, recordType };
      });
    }
  };

  const completionSource = useMemo(
    () => buildAqlCompletionSource(() => depsRef.current),
    []
  );

  // Run callback wired to Ctrl/Cmd+Enter. Re-bound on every render so
  // it picks up the latest `onExecute` closure (which captures `running`).
  const executeRef = useRef(onExecute);
  executeRef.current = onExecute;

  const extensions = useMemo(() => {
    return [
      // Highest precedence so Mod-Enter wins over any future extension
      // that might claim it (e.g. autocomplete's Enter binding doesn't
      // claim Mod-Enter today, but the Prec.highest guards against drift).
      // Tab → accept the active completion; when no dropdown is open
      // acceptCompletion returns false and the next handler takes over
      // (indent / default behavior), so this doesn't break normal Tab.
      Prec.highest(
        keymap.of([
          {
            key: "Mod-Enter",
            run: () => {
              executeRef.current();
              return true;
            },
            preventDefault: true
          },
          {
            key: "Tab",
            run: acceptCompletion
          }
        ])
      ),
      EditorView.lineWrapping,
      autocompletion({
        override: [completionSource],
        activateOnTyping: true,
        closeOnBlur: true,
        defaultKeymap: true,
        maxRenderedOptions: 30
      }),
      keymap.of(completionKeymap),
      // Mantine-aligned theme overrides. The default CodeMirror styling
      // looks like a standalone editor; these tweaks make it merge into
      // the Paper container the QueryPage wraps it in.
      EditorView.theme({
        "&": {
          fontSize: "var(--mantine-font-size-sm)"
        },
        ".cm-scroller": {
          fontFamily: "var(--mantine-font-family-monospace, monospace)",
          minHeight,
          maxHeight,
          overflow: "auto"
        },
        ".cm-content": {
          padding: "var(--mantine-spacing-xs) 0",
          caretColor: "var(--mantine-color-text)"
        },
        ".cm-editor": {
          border: "1px solid var(--mantine-color-default-border)",
          borderRadius: "var(--mantine-radius-sm)",
          backgroundColor: "var(--mantine-color-body)"
        },
        "&.cm-focused": {
          outline: "2px solid var(--mantine-primary-color-filled)",
          outlineOffset: "-2px"
        },
        ".cm-gutters": {
          display: "none"
        }
      })
    ];
  }, [completionSource, minHeight, maxHeight]);

  const handleChange = useCallback(
    (next: string) => {
      onChange(next);
    },
    [onChange]
  );

  return (
    <div style={{ opacity: readOnly ? 0.6 : 1 }}>
      <CodeMirror
        ref={editorRef}
        value={value}
        onChange={handleChange}
        extensions={extensions}
        placeholder={placeholder}
        editable={!readOnly}
        readOnly={readOnly}
        basicSetup={{
          lineNumbers: false,
          foldGutter: false,
          highlightActiveLine: false,
          highlightActiveLineGutter: false,
          dropCursor: true,
          allowMultipleSelections: false,
          indentOnInput: false,
          syntaxHighlighting: false,
          bracketMatching: false,
          closeBrackets: false,
          autocompletion: false, // we provide our own
          rectangularSelection: false,
          crosshairCursor: false,
          highlightSelectionMatches: false,
          closeBracketsKeymap: false,
          defaultKeymap: true,
          searchKeymap: false,
          historyKeymap: true,
          foldKeymap: false,
          completionKeymap: false, // wired explicitly above
          lintKeymap: false
        }}
      />
      {(schemaQuery.isError || entityCtxQuery.isError) && (
        <div style={{
          fontSize: "var(--mantine-font-size-xs)",
          color: "var(--mantine-color-dimmed)",
          marginTop: 4
        }}>
          Autocomplete schema unavailable; query execution still works.
        </div>
      )}
    </div>
  );
}
