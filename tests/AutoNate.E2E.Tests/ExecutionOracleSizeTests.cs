using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The live-engine oracle is this many cells, asserted in the slim tier (#447).
/// </summary>
/// <remarks>
/// <para>
/// <b>No trait, no fixture, no engine.</b> That is the entire point: no trait is
/// what puts a test in the <b>slim</b> tier. Its sibling
/// <see cref="ExecutionEvidenceExecutionTests"/> is
/// <c>RequiresService=Flowable</c> and therefore full-local only, so its own
/// <c>The_oracle_runs_every_declared_cell</c> cannot fail a merge. This class
/// runs in the same project under the slim filter, and calls the same
/// <c>DeclaredEffects()</c> the theory feeds from.
/// </para>
/// <para>
/// <b>Why this exists rather than a stronger regex.</b> #433 was fixed by having
/// the backend suite read the E2E <em>source</em> and reject an arm whose body was
/// the literal <c>null</c>. A guard that matches a spelling has as many holes as
/// there are ways to spell the thing, and verification found four in one sitting:
/// <c>=&gt; default,</c>, the arm split across two lines, a look-alike arm left
/// inside a block comment with the real one deleted, and an early
/// <c>return null;</c> before the switch. Each dropped the oracle from 29 cells to
/// 1 with every CI gate green.
/// </para>
/// <para>
/// This runs the switch instead of reading it. None of those four survive it,
/// because none of them is a question about text.
/// </para>
/// </remarks>
public sealed class ExecutionOracleSizeTests
{
    [Fact]
    public void The_live_engine_oracle_has_not_shrunk()
    {
        // Moves with `obliged` in ExecutionEvidenceTests, which names the same
        // set. Both are edited together or one of them fails.
        const int Cells = 49;

        var actual = ExecutionEvidenceExecutionTests.DeclaredEffects().Count;

        Assert.True(
            actual == Cells,
            $"The live-engine oracle runs {actual} cells, not {Cells}. If an element genuinely "
            + "stopped being exercised, change this number and the `obliged` list in "
            + "ExecutionEvidenceTests in the same commit — an oracle that quietly shrinks is "
            + "the defect this milestone has now chased through four hiding places (#447).");
    }
}
