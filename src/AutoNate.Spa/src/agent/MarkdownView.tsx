import ReactMarkdown from "react-markdown";
import remarkGfm from "remark-gfm";

type Props = {
  source: string;
};

// Renders LLM-produced markdown safely. react-markdown's default URL
// transformer drops javascript: schemes, and we don't enable rehype-raw, so
// HTML in the source string ends up as literal text rather than being parsed
// — no DOMPurify needed. Links open in a new tab with rel=noopener so a
// hostile target can't window.opener-attack the SPA.
export function MarkdownView({ source }: Props) {
  return (
    <div className="agent-markdown">
      <ReactMarkdown
        remarkPlugins={[remarkGfm]}
        components={{
          a: ({ node, ...rest }) => (
            <a {...rest} target="_blank" rel="noopener noreferrer" />
          )
        }}
      >
        {source}
      </ReactMarkdown>
    </div>
  );
}
