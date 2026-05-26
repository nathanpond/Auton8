using AutoNate.Web.Services.Query;
using Xunit;

namespace AutoNate.Web.Tests.Query;

public sealed class AqlLexerTests
{
    [Fact]
    public void Keywords_Are_Upper_Cased()
    {
        var tokens = AqlLexer.Tokenize("from Records where Name = \"x\"");
        Assert.Equal(TokenKind.Keyword, tokens[0].Kind);
        Assert.Equal("FROM", tokens[0].Text);
        Assert.Equal(TokenKind.Keyword, tokens[2].Kind);
        Assert.Equal("WHERE", tokens[2].Text);
    }

    [Fact]
    public void Identifiers_Preserve_Original_Case()
    {
        var tokens = AqlLexer.Tokenize("FROM Records");
        var ident = tokens[1];
        Assert.Equal(TokenKind.Identifier, ident.Kind);
        Assert.Equal("Records", ident.Text);
    }

    [Fact]
    public void Relative_Date_Tokenizes_With_Negative_Sign()
    {
        var tokens = AqlLexer.Tokenize("CreatedDate > -2w");
        // CreatedDate, >, -2w, EOF
        Assert.Equal(TokenKind.Operator, tokens[1].Kind);
        Assert.Equal(">", tokens[1].Text);
        Assert.Equal(TokenKind.RelativeDate, tokens[2].Kind);
        Assert.Equal("-2w", tokens[2].Text);
    }

    [Fact]
    public void Relative_Date_Without_Sign_Tokenizes_When_Followed_By_Suffix()
    {
        var tokens = AqlLexer.Tokenize("CreatedDate > 4d");
        Assert.Equal(TokenKind.RelativeDate, tokens[2].Kind);
        Assert.Equal("4d", tokens[2].Text);
    }

    [Fact]
    public void Bare_Number_Stays_A_Number_When_No_Date_Suffix()
    {
        var tokens = AqlLexer.Tokenize("KeyNumber > 12");
        Assert.Equal(TokenKind.Number, tokens[2].Kind);
        Assert.Equal("12", tokens[2].Text);
    }

    [Fact]
    public void String_Escape_Handles_Quoted_Char()
    {
        var tokens = AqlLexer.Tokenize("Name = \"a\\\"b\"");
        var str = tokens[2];
        Assert.Equal(TokenKind.String, str.Kind);
        Assert.Equal("a\"b", str.Text);
    }

    [Fact]
    public void Operators_Are_Distinct_Tokens()
    {
        var tokens = AqlLexer.Tokenize("a != b <= c >= d ~ e");
        Assert.Equal("!=", tokens[1].Text);
        Assert.Equal("<=", tokens[3].Text);
        Assert.Equal(">=", tokens[5].Text);
        Assert.Equal("~", tokens[7].Text);
    }

    [Fact]
    public void Booleans_And_Null_Are_Their_Own_Token_Kinds()
    {
        var tokens = AqlLexer.Tokenize("Published = True AND Other = NULL");
        var trueTok = tokens[2];
        var nullTok = tokens[6];
        Assert.Equal(TokenKind.Bool, trueTok.Kind);
        Assert.Equal(TokenKind.Null, nullTok.Kind);
    }

    [Fact]
    public void Unterminated_String_Throws()
    {
        var ex = Assert.Throws<AqlValidationException>(() => AqlLexer.Tokenize("Name = \"oops"));
        Assert.Contains("Unterminated", ex.Message);
    }

    [Theory]
    [InlineData("now")]
    [InlineData("NOW")]
    [InlineData("Now")]
    public void Now_Lexes_As_RelativeDate_Preserving_Case(string source)
    {
        var tokens = AqlLexer.Tokenize($"StartDate < {source}");
        // StartDate, <, NOW, EOF
        Assert.Equal(TokenKind.RelativeDate, tokens[2].Kind);
        Assert.Equal("NOW", tokens[2].Text);
    }
}
