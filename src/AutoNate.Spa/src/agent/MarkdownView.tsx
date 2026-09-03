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
          // `children` is destructured and rendered explicitly rather than
          // arriving inside `...rest`. Behaviourally identical, but it lets
          // both a reader and jsx-a11y/anchor-has-content see that the anchor
          // has content — spread props are opaque to the rule, which is why it
          // fired here.
          a: ({ node, children, ...rest }) => (
            <a {...rest} target="_blank" rel="noopener noreferrer">
              {children}
            </a>
          )
        }}
      >
        {source}
      </ReactMarkdown>
    </div>
  );
}
