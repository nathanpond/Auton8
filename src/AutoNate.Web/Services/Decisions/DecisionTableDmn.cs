using System.Security;
using System.Text;
using System.Xml.Linq;
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
    /// The engine key a published VERSION is deployed under, so a process can bind
    /// to that version and not to whatever is latest (#111).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Flowable resolves a DMN service task's <c>decisionTableReferenceKey</c> to
    /// the LATEST version of that key at run time, and nothing in the element's
    /// field extensions selects a version. Measured, not assumed: the two escapes
    /// Flowable offers for exactly this — a <c>.dmn</c> riding in the process
    /// deployment, and a <c>parentDeploymentId</c> on the DMN deployment — are
    /// both unreachable here. The process engine in the pinned image has no DMN
    /// deployer (a <c>.dmn</c> in a BPMN deployment becomes a resource and no
    /// decision), and the DMN REST API ignores a <c>parentDeploymentId</c> form
    /// field, stamping the deployment's own id instead. Both were probed against
    /// the running engine before this existed.
    /// </para>
    /// <para>
    /// So the version goes in the key. A second copy of the version's own DMN is
    /// deployed under this name the first time a process binds to it, and the
    /// deployed BPMN points here; the authored diagram keeps the author's key, and
    /// the table's own key still means "latest", which is what the try-it panel
    /// and every existing reader rely on.
    /// </para>
    /// <para>
    /// <b>A hyphen, and that is the load-bearing part.</b> A DMN id is an NCName,
    /// which allows one; <see cref="DecisionTableValidator"/>'s key pattern does
    /// not — an author's key is a letter followed by letters, digits and
    /// underscores. So no author can write a key that collides with another
    /// table's pinned name. The first draft used <c>__v</c>, which they can:
    /// a table genuinely called <c>foo__v1</c> would occupy the pinned key of
    /// <c>foo</c> version 1, and <see cref="IFlowableDecisionClient.EnsureDecisionAsync"/>
    /// would find it already deployed and bind the process to the wrong table
    /// without deploying anything or saying a word.
    /// </para>
    /// </remarks>
    public static string PinnedKey(string decisionKey, int versionNumber) =>
        $"{decisionKey}-v{versionNumber}";

    /// <summary>
    /// Re-keys a published version's stored DMN so it can be deployed under its
    /// pinned key (#111).
    /// </summary>
    /// <remarks>
    /// The stored bytes are re-keyed rather than the table regenerated. A table's
    /// current draft is not its published version, and regenerating from the model
    /// would bind a process to rules nobody published — which is the failure
    /// versioning exists to prevent, arriving by the back door.
    /// </remarks>
    public static string Rekey(string dmnXml, string pinnedKey)
    {
        var document = XDocument.Parse(dmnXml);
        var root = document.Root
            ?? throw new InvalidOperationException("The stored DMN has no root element.");

        root.SetAttributeValue("id", $"defs_{pinnedKey}");

        // Flowable takes the decision KEY from the <decision> id, so this single
        // attribute is what the whole mechanism turns on. `GetLatestDecisionAsync`
        // after the deploy is what proves it landed rather than being accepted and
        // quietly not parsed (#106's finding about the file extension).
        foreach (var decision in root.Elements(root.Name.Namespace + "decision"))
        {
            decision.SetAttributeValue("id", pinnedKey);

            foreach (var table in decision.Elements(root.Name.Namespace + "decisionTable"))
            {
                table.SetAttributeValue("id", $"dt_{pinnedKey}");
            }
        }

        var declaration = document.Declaration is null
            ? "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n"
            : $"{document.Declaration}\n";

        return declaration + document.ToString(SaveOptions.DisableFormatting);
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
