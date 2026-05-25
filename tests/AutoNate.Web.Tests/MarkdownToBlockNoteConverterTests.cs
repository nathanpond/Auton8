using System.Text.Json;
using AutoNate.Web.Services.Notes;
using Xunit;

namespace AutoNate.Web.Tests;

public sealed class MarkdownToBlockNoteConverterTests
{
    private static readonly MarkdownToBlockNoteConverter Converter = new();

    [Fact]
    public void empty_input_returns_a_single_paragraph_so_blocknote_can_render()
    {
        var doc = Converter.Convert("");
        Assert.Equal(JsonValueKind.Array, doc.ValueKind);
        Assert.Equal(1, doc.GetArrayLength());
        var block = doc[0];
        Assert.Equal("paragraph", block.GetProperty("type").GetString());
    }

    [Fact]
    public void plain_paragraph_renders_one_paragraph_block_with_text_inline()
    {
        var doc = Converter.Convert("Hello, world.");
        Assert.Equal(1, doc.GetArrayLength());
        var block = doc[0];
        Assert.Equal("paragraph", block.GetProperty("type").GetString());
        var inlines = block.GetProperty("content");
        Assert.Equal(1, inlines.GetArrayLength());
        Assert.Equal("text", inlines[0].GetProperty("type").GetString());
        Assert.Equal("Hello, world.", inlines[0].GetProperty("text").GetString());
    }

    [Fact]
    public void heading_levels_map_to_heading_block_with_level_prop_clamped_to_3()
    {
        var doc = Converter.Convert("# H1\n\n## H2\n\n#### H4 collapses to 3");
        Assert.Equal(3, doc.GetArrayLength());
        Assert.Equal("heading", doc[0].GetProperty("type").GetString());
        Assert.Equal(1, doc[0].GetProperty("props").GetProperty("level").GetInt32());
        Assert.Equal(2, doc[1].GetProperty("props").GetProperty("level").GetInt32());
        Assert.Equal(3, doc[2].GetProperty("props").GetProperty("level").GetInt32());
    }

    [Fact]
    public void bold_italic_code_strikethrough_inline_marks_set_correct_styles()
    {
        var doc = Converter.Convert("Plain **bold** *italic* `code` ~~strike~~ end.");
        Assert.Equal(1, doc.GetArrayLength());
        var inlines = doc[0].GetProperty("content");
        // We expect at least one of each style applied to its run.
        var anyBold = false; var anyItalic = false; var anyCode = false; var anyStrike = false;
        foreach (var run in inlines.EnumerateArray())
        {
            if (run.GetProperty("type").GetString() != "text") continue;
            var styles = run.GetProperty("styles");
            if (styles.TryGetProperty("bold", out _)) anyBold = true;
            if (styles.TryGetProperty("italic", out _)) anyItalic = true;
            if (styles.TryGetProperty("code", out _)) anyCode = true;
            if (styles.TryGetProperty("strike", out _)) anyStrike = true;
        }
        Assert.True(anyBold);
        Assert.True(anyItalic);
        Assert.True(anyCode);
        Assert.True(anyStrike);
    }

    [Fact]
    public void links_emit_link_inlines_with_href_and_nested_text()
    {
        var doc = Converter.Convert("Visit [Anthropic](https://anthropic.com) for info.");
        var inlines = doc[0].GetProperty("content");
        var linkSeen = false;
        foreach (var inline in inlines.EnumerateArray())
        {
            if (inline.GetProperty("type").GetString() == "link")
            {
                linkSeen = true;
                Assert.Equal("https://anthropic.com", inline.GetProperty("href").GetString());
                var linkContent = inline.GetProperty("content");
                Assert.Equal(1, linkContent.GetArrayLength());
                Assert.Equal("Anthropic", linkContent[0].GetProperty("text").GetString());
            }
        }
        Assert.True(linkSeen, "Expected a link inline in the rendered output.");
    }

    [Fact]
    public void bullet_and_numbered_lists_emit_list_item_blocks()
    {
        var doc = Converter.Convert("""
            - one
            - two
            - three

            1. first
            2. second
            """);
        var types = new List<string>();
        foreach (var b in doc.EnumerateArray()) types.Add(b.GetProperty("type").GetString()!);
        Assert.Equal(3, types.Count(t => t == "bulletListItem"));
        Assert.Equal(2, types.Count(t => t == "numberedListItem"));
    }

    [Fact]
    public void fenced_code_block_preserves_language_prop_and_body()
    {
        var doc = Converter.Convert("""
            ```csharp
            var x = 1;
            ```
            """);
        Assert.Equal(1, doc.GetArrayLength());
        var block = doc[0];
        Assert.Equal("codeBlock", block.GetProperty("type").GetString());
        Assert.Equal("csharp", block.GetProperty("props").GetProperty("language").GetString());
        var content = block.GetProperty("content");
        Assert.Equal(1, content.GetArrayLength());
        Assert.Equal("var x = 1;", content[0].GetProperty("text").GetString());
    }

    [Fact]
    public void blockquote_emits_quote_block_with_inlines()
    {
        var doc = Converter.Convert("> Quoted.");
        Assert.Equal(1, doc.GetArrayLength());
        Assert.Equal("quote", doc[0].GetProperty("type").GetString());
        Assert.Equal("Quoted.", doc[0].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void multi_paragraph_doc_preserves_block_order()
    {
        var doc = Converter.Convert("First.\n\nSecond.\n\n# Title\n\nThird.");
        var types = doc.EnumerateArray().Select(b => b.GetProperty("type").GetString()).ToArray();
        Assert.Equal(new[] { "paragraph", "paragraph", "heading", "paragraph" }, types);
        Assert.Equal("First.", doc[0].GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("Third.", doc[3].GetProperty("content")[0].GetProperty("text").GetString());
    }

    [Fact]
    public void every_block_carries_an_id_and_an_empty_children_array()
    {
        var doc = Converter.Convert("# Title\n\nBody.");
        foreach (var block in doc.EnumerateArray())
        {
            Assert.True(block.TryGetProperty("id", out var id));
            Assert.False(string.IsNullOrWhiteSpace(id.GetString()));
            Assert.True(block.TryGetProperty("children", out var children));
            Assert.Equal(JsonValueKind.Array, children.ValueKind);
            Assert.Equal(0, children.GetArrayLength());
        }
    }

    [Fact]
    public void runs_with_identical_styles_merge_into_one_text_run()
    {
        // Markdig may split inline runs across LineBreak markers etc.; the
        // converter should collapse contiguous same-style runs so the resulting
        // JSON stays compact.
        var doc = Converter.Convert("Hello world.");
        var content = doc[0].GetProperty("content");
        // Whatever the parser does internally, a plain phrase should land as a
        // single text run.
        Assert.Equal(1, content.GetArrayLength());
    }
}
