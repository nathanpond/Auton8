using AutoNate.Web.Services.Query;
using AutoNate.Web.Tests.Properties.Generators;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;

namespace AutoNate.Web.Tests.Properties;

/// <summary>
/// Properties for <see cref="AqlParameterBinder"/>.
/// </summary>
/// <remarks>
/// #69's acceptance criterion asks that the binder's output "never interpolates
/// a raw parameter value into SQL text". That wording assumes a shape the code
/// does not have: <c>Bind</c> returns an <see cref="AqlQuery"/>, not SQL. It
/// substitutes <c>:name</c> placeholders <em>in the AST</em>, and SQL
/// generation happens later, in the entity adapters.
///
/// So the property actually available here — and the one carrying the security
/// meaning the criterion was reaching for — is that a parameter value can only
/// ever land as a leaf. No value, however hostile, may add, remove or re-shape
/// a node. If that holds there is no path by which a parameter becomes syntax,
/// whatever the adapter does downstream.
/// </remarks>
public sealed class AqlBinderProperties
{
    private const int Runs = 300;

    /// <summary>Payloads that would change the query if a value were ever re-parsed.</summary>
    private static Gen<string> HostileValues() => Gen.Elements(
        "\" OR \"1\"=\"1",
        "') OR 1=1 --",
        "\"; DROP TABLE records; --",
        "1 OR TRUE",
        "AND name = \"x\"",
        ") OR (",
        "*/ UNION SELECT",
        ":anotherParam",
        "' OR ''='",
        "NULL",
        " ",
        new string('A', 5000));

    [Property(MaxTest = Runs, Replay = "(1234567890,9876543211)")]
    public Property A_bound_value_can_never_change_the_query_shape()
    {
        return Prop.ForAll(
            AqlGenerators.QueryArb(),
            Arb.From(HostileValues()),
            (query, payload) =>
            {
                // Every string leaf becomes a placeholder, so the binder gets
                // as many substitution points as the query allows.
                var parameterised = query with
                {
                    Where = query.Where is null ? null : Placeholderise(query.Where),
                };

                AqlQuery bound;
                try
                {
                    bound = AqlParameterBinder.Bind(
                        parameterised,
                        new Dictionary<string, string> { ["p"] = payload });
                }
                catch (AqlParameterBindingException)
                {
                    // A refusal is a safe outcome. The property is about what
                    // happens when binding succeeds.
                    return true;
                }

                var before = Shape(parameterised.Where);
                var after = Shape(bound.Where);

                if (before != after)
                {
                    throw new Xunit.Sdk.XunitException(
                        "A parameter value changed the query's structure.\n"
                        + $"  payload : {payload}\n  before  : {before}\n  after   : {after}");
                }

                return true;
            });
    }

    private static AqlWhere Placeholderise(AqlWhere where) => where switch
    {
        AqlBinary b => b with { Left = Placeholderise(b.Left), Right = Placeholderise(b.Right) },
        AqlCompare c => c with { Value = new AqlString(":p") },
        AqlIn i => i with { Values = i.Values.Select(_ => (AqlValue)new AqlString(":p")).ToList() },
        AqlBetween b => b with { Lo = new AqlString(":p"), Hi = new AqlString(":p") },
        AqlFunctionCompare f => f with { Value = new AqlString(":p") },
        AqlFunctionCall f => f with { Args = f.Args.Select(_ => (AqlValue)new AqlString(":p")).ToList() },
        _ => where,
    };

    /// <summary>The tree with every leaf value erased.</summary>
    /// <remarks>
    /// Values are expected to change — that is what binding does. Erasing them
    /// leaves exactly what must not change: the structure. A payload that
    /// became syntax would appear here as extra or different nodes.
    /// </remarks>
    private static string Shape(AqlWhere? where) => where switch
    {
        null => "-",
        AqlBinary b => $"({Shape(b.Left)} {b.Op} {Shape(b.Right)})",
        AqlCompare c => $"cmp[{c.Field} {c.Op} _]",
        AqlIn i => $"in[{i.Field} x{i.Values.Count}]",
        AqlBetween b => $"between[{b.Field} _ _]",
        AqlContains c => $"contains[{c.Field} _]",
        AqlFunctionCall f => $"fn[{f.Name} x{f.Args.Count}]",
        AqlFunctionCompare f => $"fncmp[{f.FnName} x{f.Args.Count} {f.Op} _]",
        _ => $"?{where.GetType().Name}",
    };
}
