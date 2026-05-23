using AutoNate.Web.Services.Query;
using Xunit;

namespace AutoNate.Web.Tests.Query;

public sealed class AqlRelativeDateTests
{
    private static readonly DateTime FixedNow = new(2026, 5, 22, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(-2, 'w', "2026-05-08")]
    [InlineData(4, 'd', "2026-05-26")]
    [InlineData(-4, 'h', "2026-05-22T08:00:00")]
    [InlineData(2, 'y', "2028-05-22")]
    [InlineData(-3, 'm', "2026-02-22")]
    public void Resolves_All_Suffixes(int magnitude, char unit, string expectedIso)
    {
        var r = new AqlRelativeDate(magnitude, unit);
        var got = r.Resolve(FixedNow);
        Assert.Equal(DateTime.Parse(expectedIso).ToUniversalTime().Date, got.Date);
    }

    [Fact]
    public void Lexer_And_Parser_Round_Trip_Relative_Dates()
    {
        var q = AqlParser.Parse("CreatedDate > -2w");
        var cmp = Assert.IsType<AqlCompare>(q.Where);
        var rel = Assert.IsType<AqlRelativeDate>(cmp.Value);
        Assert.Equal(-2, rel.Magnitude);
        Assert.Equal('w', char.ToLowerInvariant(rel.Unit));
    }
}
