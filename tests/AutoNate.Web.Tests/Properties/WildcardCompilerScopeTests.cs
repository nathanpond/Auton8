using System.Reflection;
using AutoNate.Web.Authorization.Selectors;
using Xunit;

namespace AutoNate.Web.Tests.Properties;

/// <summary>
/// Exactly two selector compilers handle the wildcard (#574).
/// </summary>
/// <remarks>
/// <para>
/// #574's last acceptance criterion asked for confirmation that the other
/// compilers carry no <see cref="WildcardValue"/> branch. Delivered as a test
/// rather than a sentence in a PR, because prose confirms the count on the day
/// it is written and a test confirms it on the day the next compiler is added
/// — which is the only day it matters.
/// </para>
/// <para>
/// <b>The number, since the AC asked for it: nine.</b> Nine concrete types
/// implement <c>ISelectorCompiler&lt;&gt;</c> — Form, Group, PathOnly, Record,
/// RecordType, Role, WorkflowModel, and the two workflow cache compilers. Two
/// handle the wildcard, so <b>seven</b> carry no branch, not the eight the
/// acceptance criterion guessed. The AC's figure was written before anyone
/// counted; this is the count.
/// </para>
/// <para>
/// The two that do handle it — the workflow task and execution cache compilers —
/// each branch on the wildcard <b>before</b> resolving a value. A third compiler
/// appearing in this list is not automatically wrong; it means somebody wrote
/// wildcard handling and should have read what `add-projection` now says about
/// it. That is the conversation this failure starts.
/// </para>
/// </remarks>
public sealed class WildcardCompilerScopeTests
{
    [Fact]
    public void Only_the_two_workflow_cache_compilers_handle_the_wildcard()
    {
        var compilers = typeof(ISelectorCompiler<>).Assembly
            .GetTypes()
            .Where(t => t is { IsAbstract: false, IsInterface: false })
            .Where(t => t.GetInterfaces().Any(i =>
                i.IsGenericType && i.GetGenericTypeDefinition() == typeof(ISelectorCompiler<>)))
            .ToList();

        // The suite is worth nothing if reflection found no compilers at all —
        // a silently empty set would make every assertion below vacuous.
        Assert.True(
            compilers.Count >= 8,
            $"Expected the selector compiler family to be discoverable; found {compilers.Count}. "
            + "If compilers moved assembly or stopped implementing ISelectorCompiler<>, this "
            + "guard is looking in the wrong place and is no longer checking anything.");

        var handlingWildcard = compilers
            .Where(ReferencesWildcard)
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Equal<IEnumerable<string>>(
            new[]
            {
                nameof(WorkflowExecutionCacheSelectorCompiler),
                nameof(WorkflowTaskCacheSelectorCompiler)
            },
            handlingWildcard);
    }

    // Reads the type's IL for a reference to WildcardValue. Cruder than parsing
    // source, and it is what survives a file being renamed or the branch moving
    // between methods.
    private static bool ReferencesWildcard(Type compiler)
    {
        var wildcard = typeof(WildcardValue);

        foreach (var method in compiler.GetMethods(
                     BindingFlags.Public | BindingFlags.NonPublic
                     | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
        {
            var body = method.GetMethodBody();
            if (body is null) continue;

            var il = body.GetILAsByteArray();
            if (il is null) continue;

            // Any metadata token in the body that resolves to WildcardValue.
            for (var i = 0; i + 4 < il.Length; i++)
            {
                var token = BitConverter.ToInt32(il, i);
                try
                {
                    var resolved = compiler.Module.ResolveType(token);
                    if (resolved == wildcard) return true;
                }
                catch
                {
                    // Not a type token at this offset — expected constantly,
                    // since this walks every byte rather than decoding opcodes.
                }
            }
        }

        return false;
    }
}
