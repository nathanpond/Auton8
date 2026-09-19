using System.Security;
using System.Text;
using AutoNate.Web.Models;

namespace AutoNate.Web.Services.Decisions;

/// <summary>
/// Turns an authored table into the DMN the engine deploys (#110).
/// </summary>
/// <remarks>
/// <para>
/// Generated at publish rather than stored, so the author never hand-edits XML and
/// every cell stays validatable. The published version keeps a copy of what was
/// generated (<c>decision_table_versions.dmn_xml</c>) — regenerating on demand
/// would let a later change to this file silently change what a bound process
/// decides, which is what versioning is here to prevent.
/// </para>
/// <para>
/// <b>The namespace and element names were measured, not recalled</b> (#106): a
/// table deployed under <c>https://www.omg.org/spec/DMN/20191111/MODEL/</c> with
/// this exact shape round-tripped through a live Flowable 8.0.0 and evaluated
/// every rule. <c>DecisionTableGeneratedDmnTests</c> deploys the generator's own
/// output and evaluates it, so a change here that produces plausible-looking XML
/// the engine will not run fails rather than ships.
/// </para>
/// </remarks>
public static class DecisionTableDmn
{
    /// <summary>The namespace Flowable 8.0.0 reads. Verified by deployment.</summary>
    private const string DmnNamespace = "https://www.omg.org/spec/DMN/20191111/MODEL/";

    public static string Generate(DecisionTableModel table)
    {
        var builder = new StringBuilder();
        var key = table.DecisionKey;

        builder.AppendLine("""<?xml version="1.0" encoding="UTF-8"?>""");
        builder.AppendLine(
            $"""<definitions xmlns="{DmnNamespace}" id="defs_{Esc(key)}" name="{Esc(table.Name)}" namespace="http://autonate.dev/decisions">""");
        builder.AppendLine($"""  <decision id="{Esc(key)}" name="{Esc(table.Name)}">""");
        builder.AppendLine($"""    <decisionTable id="dt_{Esc(key)}" hitPolicy="{Esc(table.HitPolicy)}">""");

        foreach (var input in table.Inputs)
        {
            builder.AppendLine($"""      <input id="{Esc(input.Id)}" label="{Esc(input.Label)}">""");
            builder.AppendLine(
                $"""        <inputExpression id="ie_{Esc(input.Id)}" typeRef="{Esc(input.TypeRef)}">""");
            // The expression text is the VARIABLE NAME the engine binds. Not the
            // label: a label is for people and may contain anything.
            builder.AppendLine($"          <text>{Esc(input.Name)}</text>");
            builder.AppendLine("        </inputExpression>");
            builder.AppendLine("      </input>");
        }

        foreach (var output in table.Outputs)
        {
            builder.AppendLine(
                $"""      <output id="{Esc(output.Id)}" label="{Esc(output.Label)}" name="{Esc(output.Name)}" typeRef="{Esc(output.TypeRef)}" />""");
        }

        foreach (var rule in table.Rules)
        {
            builder.AppendLine($"""      <rule id="{Esc(rule.Id)}">""");
            for (var i = 0; i < rule.InputEntries.Count; i++)
            {
                // An EMPTY inputEntry means "any", and the element must still be
                // present -- DMN is positional, so omitting it shifts every later
                // cell onto the wrong column. Measured the hard way is not
                // required here; the schema requires one entry per input.
                builder.AppendLine(
                    $"""        <inputEntry id="{Esc(rule.Id)}_i{i}"><text>{Esc(rule.InputEntries[i])}</text></inputEntry>""");
            }

            for (var i = 0; i < rule.OutputEntries.Count; i++)
            {
                builder.AppendLine(
                    $"""        <outputEntry id="{Esc(rule.Id)}_o{i}"><text>{Esc(rule.OutputEntries[i])}</text></outputEntry>""");
            }

            builder.AppendLine("      </rule>");
        }

        builder.AppendLine("    </decisionTable>");
        builder.AppendLine("  </decision>");
        builder.AppendLine("</definitions>");

        return builder.ToString();
    }

    /// <summary>
    /// XML-escapes a value for either an attribute or element text.
    /// </summary>
    /// <remarks>
    /// <c>SecurityElement.Escape</c> covers <c>&amp; &lt; &gt; " '</c>, which is
    /// every character that can break out of either position. A cell legitimately
    /// contains <c>&gt;</c> and <c>&lt;</c> — they are comparison operators — so
    /// this is load-bearing rather than defensive: without it every range and every
    /// comparison would produce malformed XML.
    /// </remarks>
    private static string Esc(string? value) =>
        SecurityElement.Escape(value ?? string.Empty) ?? string.Empty;
}
