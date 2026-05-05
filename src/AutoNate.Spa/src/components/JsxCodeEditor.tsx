import CodeMirror from "@uiw/react-codemirror";
import { javascript } from "@codemirror/lang-javascript";
import { html } from "@codemirror/lang-html";
import { json } from "@codemirror/lang-json";

export type JsxCodeEditorLanguage = "jsx" | "html" | "json";

export type JsxCodeEditorProps = {
  value: string;
  onChange: (next: string) => void;
  language?: JsxCodeEditorLanguage;
  placeholder?: string;
  height?: string;
  autoFocus?: boolean;
};

// Shared CodeMirror harness. Originally inline in MenuItemEditModal — extracted
// so the new Forms editor reuses the same configuration without diverging.
export default function JsxCodeEditor({
  value,
  onChange,
  language = "jsx",
  placeholder,
  height = "100%",
  autoFocus = false
}: JsxCodeEditorProps) {
  return (
    <CodeMirror
      value={value}
      onChange={onChange}
      height={height}
      style={{ height }}
      autoFocus={autoFocus}
      placeholder={placeholder}
      extensions={[pickLanguageExtension(language)]}
      basicSetup={{
        lineNumbers: true,
        highlightActiveLineGutter: true,
        highlightSpecialChars: true,
        history: true,
        foldGutter: true,
        drawSelection: true,
        dropCursor: true,
        allowMultipleSelections: true,
        indentOnInput: true,
        syntaxHighlighting: true,
        bracketMatching: true,
        closeBrackets: true,
        autocompletion: true,
        rectangularSelection: true,
        crosshairCursor: true,
        highlightActiveLine: true,
        highlightSelectionMatches: true,
        closeBracketsKeymap: true,
        defaultKeymap: true,
        searchKeymap: true,
        historyKeymap: true,
        foldKeymap: true,
        completionKeymap: true,
        lintKeymap: true
      }}
    />
  );
}

function pickLanguageExtension(language: JsxCodeEditorLanguage) {
  switch (language) {
    case "jsx":
      return javascript({ jsx: true, typescript: true });
    case "html":
      return html();
    case "json":
      return json();
  }
}
