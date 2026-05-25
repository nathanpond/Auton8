using System.Text;
using System.Text.Json;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Helpers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace AutoNate.Web.Services.Notes;

// Converts CommonMark/GFM markdown into a BlockNote v0.51 block-array JSON
// document. Designed for the agent's "summarize this conversation and save it
// as a note here" path: the LLM emits markdown, the converter rewrites it as
// the JSON shape BlockNote's editor renders losslessly.
//
// What's mapped (block level):
//   paragraph, heading (1-3, levels 4+ collapsed to 3), bullet list item,
//   numbered list item, quote, code block (language preserved), divider.
//
// What's mapped (inline):
//   text, bold, italic, code, strikethrough, link.
//
// Known limitations (documented and stable — extend in later phases):
//   - Tables, images, footnotes, definition lists, HTML blocks ignored.
//   - List nesting collapses one level: nested items are flattened to top-
//     level items with a leading "  " indent prefix in their text content
//     so the user can still read them; bullet trees can be a future skill.
//   - Underline / textColor / backgroundColor / checked-list-item are not
//     emitted (no markdown source maps to them).
//
// The output is a JsonElement representing an array of blocks the way
// BlockNote serializes them. Stable, pure, no I/O.
public interface IMarkdownToBlockNoteConverter
{
    JsonElement Convert(string markdown);
}

public sealed class MarkdownToBlockNoteConverter : IMarkdownToBlockNoteConverter
{
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder()
            .UseAutoLinks()
            .UseEmphasisExtras()
            .UsePipeTables()
            .UseGridTables()
            .Build();

    public JsonElement Convert(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            // BlockNote needs at least one paragraph or it renders nothing.
            return SerializeBlocks(new[] { MakeBlock("paragraph", Array.Empty<object>()) });
        }

        var ast = Markdown.Parse(markdown, Pipeline);
        var blocks = new List<object>();
        foreach (var node in ast)
        {
            ConvertBlock(node, blocks, listDepth: 0);
        }
        if (blocks.Count == 0)
        {
            blocks.Add(MakeBlock("paragraph", Array.Empty<object>()));
        }
        return SerializeBlocks(blocks);
    }

    private static void ConvertBlock(Block node, List<object> output, int listDepth)
    {
        switch (node)
        {
            case ParagraphBlock para:
                output.Add(MakeBlock("paragraph", InlineContent(para.Inline)));
                break;
            case HeadingBlock heading:
                {
                    var level = Math.Clamp(heading.Level, 1, 3);
                    var props = new Dictionary<string, object?>
                    {
                        ["level"] = level
                    };
                    output.Add(MakeBlock("heading", InlineContent(heading.Inline), props));
                    break;
                }
            case QuoteBlock quote:
                {
                    // BlockNote v0.51 has a `quote` block that takes inline content
                    // directly. Quote children that are paragraphs flatten into a
                    // single quote block; nested quotes degrade to paragraphs (rare).
                    var inlines = new List<object>();
                    foreach (var child in quote)
                    {
                        if (child is ParagraphBlock p)
                        {
                            if (inlines.Count > 0)
                            {
                                inlines.Add(MakeText("\n", styles: null));
                            }
                            inlines.AddRange(InlineContent(p.Inline));
                        }
                        else
                        {
                            // Unsupported child kind inside a quote → render as a
                            // sibling paragraph below the quote so content isn't lost.
                            ConvertBlock(child, output, listDepth);
                        }
                    }
                    output.Add(MakeBlock("quote", inlines));
                    break;
                }
            case ListBlock list:
                {
                    var blockType = list.IsOrdered ? "numberedListItem" : "bulletListItem";
                    foreach (var item in list)
                    {
                        if (item is not ListItemBlock li) continue;
                        // Compose all paragraph children of this list item into one
                        // line of inline content; nested lists flatten to subsequent
                        // items with a depth-prefixed indent so the user can read
                        // them. (Phase 3 v1 ships flat lists; nested rendering is a
                        // documented future enhancement.)
                        var firstParaInlines = new List<object>();
                        var indent = new string(' ', listDepth * 2);
                        if (!string.IsNullOrEmpty(indent))
                        {
                            firstParaInlines.Add(MakeText(indent, styles: null));
                        }
                        var trailing = new List<Block>();
                        foreach (var child in li)
                        {
                            switch (child)
                            {
                                case ParagraphBlock p:
                                    if (firstParaInlines.Count > (string.IsNullOrEmpty(indent) ? 0 : 1))
                                    {
                                        firstParaInlines.Add(MakeText(" ", styles: null));
                                    }
                                    firstParaInlines.AddRange(InlineContent(p.Inline));
                                    break;
                                case ListBlock nested:
                                    trailing.Add(nested);
                                    break;
                                default:
                                    trailing.Add(child);
                                    break;
                            }
                        }
                        output.Add(MakeBlock(blockType, firstParaInlines));
                        foreach (var t in trailing)
                        {
                            ConvertBlock(t, output, listDepth + 1);
                        }
                    }
                    break;
                }
            case FencedCodeBlock code:
                {
                    var language = string.IsNullOrWhiteSpace(code.Info) ? null : code.Info;
                    var text = JoinLines(code.Lines.Lines, code.Lines.Count);
                    var props = new Dictionary<string, object?>();
                    if (language is not null) props["language"] = language;
                    output.Add(MakeBlock("codeBlock",
                        new[] { MakeText(text, styles: null) },
                        props));
                    break;
                }
            case CodeBlock indented when indented is not FencedCodeBlock:
                {
                    var text = JoinLines(indented.Lines.Lines, indented.Lines.Count);
                    output.Add(MakeBlock("codeBlock",
                        new[] { MakeText(text, styles: null) }));
                    break;
                }
            case ThematicBreakBlock:
                // BlockNote v0.51 doesn't have a first-class divider in core; emit
                // a paragraph with em-dashes so the visual break survives.
                output.Add(MakeBlock("paragraph", new[] { MakeText("———", styles: null) }));
                break;
            case Table table:
                {
                    // Tables degrade to a paragraph with the cell text joined by
                    // pipes — keeps the data visible while a proper table block
                    // mapping waits for a future phase. (BlockNote tables have a
                    // very different content model.)
                    foreach (var row in table)
                    {
                        if (row is not TableRow tr) continue;
                        var cells = new List<string>();
                        foreach (var cell in tr)
                        {
                            if (cell is TableCell tc)
                            {
                                cells.Add(ExtractPlainText(tc));
                            }
                        }
                        var line = string.Join(" | ", cells);
                        output.Add(MakeBlock("paragraph", new[] { MakeText(line, styles: null) }));
                    }
                    break;
                }
            default:
                // Unmapped block kinds (HTML, footnote definitions, etc.) become
                // empty paragraphs rather than disappearing silently.
                output.Add(MakeBlock("paragraph", Array.Empty<object>()));
                break;
        }
    }

    private static IReadOnlyList<object> InlineContent(ContainerInline? container)
    {
        if (container is null) return Array.Empty<object>();
        var styles = new Styles();
        var result = new List<object>();
        foreach (var inline in container)
        {
            EmitInline(inline, styles, result);
        }
        return result;
    }

    private static void EmitInline(Inline inline, Styles styles, List<object> output)
    {
        switch (inline)
        {
            case LiteralInline lit:
                AppendText(output, lit.Content.ToString(), styles);
                break;
            case LineBreakInline:
                AppendText(output, "\n", styles);
                break;
            case EmphasisInline emph:
                {
                    // Markdig encodes bold via DelimiterChar='*'|'_' and IsDouble=true,
                    // italic via the same delimiters with IsDouble=false, and strike
                    // via DelimiterChar='~' with IsDouble=true (GFM via EmphasisExtras).
                    var newStyles = styles.Clone();
                    if (emph.DelimiterChar == '~' && emph.DelimiterCount == 2)
                    {
                        newStyles.Strike = true;
                    }
                    else if (emph.DelimiterCount == 2)
                    {
                        newStyles.Bold = true;
                    }
                    else
                    {
                        newStyles.Italic = true;
                    }
                    foreach (var child in emph)
                    {
                        EmitInline(child, newStyles, output);
                    }
                    break;
                }
            case CodeInline code:
                {
                    var newStyles = styles.Clone();
                    newStyles.Code = true;
                    AppendText(output, code.Content, newStyles);
                    break;
                }
            case LinkInline link:
                {
                    // BlockNote represents links as a wrapper inline:
                    //   { type: "link", href, content: [ { type: "text", ... } ] }
                    var linkContent = new List<object>();
                    foreach (var child in link)
                    {
                        EmitInline(child, styles, linkContent);
                    }
                    if (linkContent.Count == 0)
                    {
                        linkContent.Add(MakeText(link.Url ?? string.Empty, styles));
                    }
                    output.Add(new Dictionary<string, object?>
                    {
                        ["type"] = "link",
                        ["href"] = link.Url ?? string.Empty,
                        ["content"] = linkContent
                    });
                    break;
                }
            case AutolinkInline autolink:
                output.Add(new Dictionary<string, object?>
                {
                    ["type"] = "link",
                    ["href"] = autolink.Url,
                    ["content"] = new[] { MakeText(autolink.Url, styles) }
                });
                break;
            case HtmlInline htmlInline:
                // Surface the raw tag as plain text rather than risk emitting
                // unsanitized HTML through the editor's content model.
                AppendText(output, htmlInline.Tag, styles);
                break;
            case ContainerInline container:
                foreach (var child in container)
                {
                    EmitInline(child, styles, output);
                }
                break;
            default:
                // Unknown inline kinds (footnotes, emoji shortcodes, etc.) drop
                // through as empty rather than throwing.
                break;
        }
    }

    private static void AppendText(List<object> output, string text, Styles styles)
    {
        if (text.Length == 0) return;
        // Merge with the previous run if styles match exactly — keeps the JSON
        // compact and matches what BlockNote produces when re-serializing.
        if (output.Count > 0
            && output[^1] is Dictionary<string, object?> prev
            && prev.TryGetValue("type", out var t) && (string?)t == "text"
            && StylesEqual(prev.GetValueOrDefault("styles") as Dictionary<string, object?>, styles))
        {
            prev["text"] = ((string?)prev["text"] ?? string.Empty) + text;
            return;
        }
        output.Add(MakeText(text, styles));
    }

    private static bool StylesEqual(Dictionary<string, object?>? a, Styles b)
    {
        var bDict = b.ToDictionary();
        if (a is null) return bDict.Count == 0;
        if (a.Count != bDict.Count) return false;
        foreach (var (key, value) in a)
        {
            if (!bDict.TryGetValue(key, out var bVal) || !Equals(value, bVal)) return false;
        }
        return true;
    }

    private static Dictionary<string, object?> MakeText(string text, Styles? styles)
    {
        var stylesDict = styles is null ? new Dictionary<string, object?>() : styles.ToDictionary();
        return new Dictionary<string, object?>
        {
            ["type"] = "text",
            ["text"] = text,
            ["styles"] = stylesDict
        };
    }

    private static Dictionary<string, object?> MakeBlock(
        string type,
        IEnumerable<object> content,
        IDictionary<string, object?>? props = null)
    {
        return new Dictionary<string, object?>
        {
            ["id"] = Guid.NewGuid().ToString("N"),
            ["type"] = type,
            ["props"] = props ?? new Dictionary<string, object?>(),
            ["content"] = content.ToList(),
            ["children"] = Array.Empty<object>()
        };
    }

    private static JsonElement SerializeBlocks(IEnumerable<object> blocks)
    {
        var json = JsonSerializer.Serialize(blocks);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    private static string JoinLines(StringLine[] lines, int count)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < count; i++)
        {
            if (i > 0) sb.Append('\n');
            sb.Append(lines[i].ToString());
        }
        return sb.ToString();
    }

    private static string ExtractPlainText(ContainerBlock container)
    {
        var sb = new StringBuilder();
        foreach (var block in container)
        {
            if (block is LeafBlock leaf && leaf.Inline is not null)
            {
                foreach (var inline in leaf.Inline)
                {
                    AppendInlineText(inline, sb);
                }
            }
            else if (block is ContainerBlock c)
            {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(ExtractPlainText(c));
            }
        }
        return sb.ToString();
    }

    private static void AppendInlineText(Inline inline, StringBuilder sb)
    {
        switch (inline)
        {
            case LiteralInline lit:
                sb.Append(lit.Content.ToString());
                break;
            case CodeInline code:
                sb.Append(code.Content);
                break;
            case AutolinkInline auto:
                sb.Append(auto.Url);
                break;
            case ContainerInline container:
                foreach (var child in container) AppendInlineText(child, sb);
                break;
        }
    }

    // Tracks the active inline-style stack as we walk emphasis nodes. Cloned
    // before mutation so siblings see the same baseline as parents.
    private sealed class Styles
    {
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public bool Code { get; set; }
        public bool Strike { get; set; }

        public Styles Clone() => new()
        {
            Bold = Bold,
            Italic = Italic,
            Code = Code,
            Strike = Strike
        };

        public Dictionary<string, object?> ToDictionary()
        {
            var d = new Dictionary<string, object?>();
            if (Bold) d["bold"] = true;
            if (Italic) d["italic"] = true;
            if (Code) d["code"] = true;
            if (Strike) d["strike"] = true;
            return d;
        }
    }
}
