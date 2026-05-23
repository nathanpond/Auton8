using System.Text.Json;
using AutoNate.Web.Services.Query;
using Xunit;

namespace AutoNate.Web.Tests.Query;

// One round-trip test acts as the canary for the JSON contract that a future
// TypeScript client-side parser will target. If the discriminator or shape
// changes, this test fails and the SPA-side schema must be updated to match.
public sealed class AqlAstJsonRoundTripTests
{
    // Records' default equality is reference-based on collection properties,
    // so we compare by re-serializing the restored AST and string-matching
    // the JSON — equivalent value-equality for the contract that matters.
    private static void AssertRoundTrip(string source)
    {
        var original = AqlParser.Parse(source);
        var json = JsonSerializer.Serialize(original);
        var restored = JsonSerializer.Deserialize<AqlQuery>(json);
        Assert.NotNull(restored);
        var json2 = JsonSerializer.Serialize(restored);
        Assert.Equal(json, json2);
    }

    [Fact]
    public void Complex_Query_Round_Trips() => AssertRoundTrip(
        "FROM Records WHERE (RecordType = \"Car\" AND CreatedDate > -2w) " +
        "ORDER BY Name DESC COLUMNS(Name, Status) LIMIT 100");

    [Fact]
    public void Function_Compare_Round_Trips() => AssertRoundTrip(
        "FROM Workflows WHERE NUMNODES() > 5");
}
