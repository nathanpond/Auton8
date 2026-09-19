using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// Completing a Page-mode task refreshes My Tasks (#268).
/// </summary>
/// <remarks>
/// <para>
/// #259 fixed this staleness for the modal path by invalidating the panel's
/// keys inside <c>MyTasksPanel.completeFromModal</c>. That is one of two call
/// sites, and the other is the one this panel itself navigates to: a user task
/// configured <c>userFormMode="page"</c> opens <c>TaskFormPage</c>, whose submit
/// went straight to the mutation and then back to <c>/home</c> -- with a stale
/// panel, because the mutation invalidated <c>["tasks",…]</c> and the panel
/// reads <c>["home",…]</c>.
/// </para>
/// <para>
/// A test of the modal path cannot see this, which is the whole point of #268:
/// the fix and the test both sat on the one call site that had been reported.
/// The invalidation now lives in <c>useCompleteTask</c>, so this asserts the
/// path that had no coverage rather than re-asserting the one that did.
/// </para>
/// <para>
/// The refresh must happen WITHOUT a reload. <c>useInvalidateOnChannels</c>
/// would eventually invalidate the right keys when the push arrives, so a test
/// that tolerates a reload -- or waits long enough -- passes on the broken code.
/// Navigating back to <c>/home</c> is a client-side route change, which keeps
/// the query cache, which is exactly the state the bug lived in.
/// </para>
/// </remarks>
[Trait("RequiresService", "Flowable")]
public sealed class PageModeTaskRefreshTests : E2ETestBase
{
    public PageModeTaskRefreshTests(AutoNateE2EFixture fixture) : base(fixture) { }

    /// <summary>
    /// The server's default form code renders a heading and nothing else, so a
    /// test that has to submit brings its own. One button, no fields: the form's
    /// contents are not what is under test.
    /// </summary>
    private const string SubmitOnlyFormCode = """
        function Page({ onSubmit }) {
          return (
            <div>
              <h3>Review</h3>
              <button type="button" onClick={() => onSubmit({ approved: true })}>
                Submit review
              </button>
            </div>
          );
        }
        """;

    [Fact]
    public async Task CompletingAPageModeTask_RefreshesMyTasks_WithoutAReload()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;
        var seeder = new ApiSeeder(page.APIRequest);

        var meResponse = await page.APIRequest.GetAsync("/api/auth/me");
        var me = await meResponse.JsonAsync()
            ?? throw new InvalidOperationException("Empty response from /api/auth/me.");
        var adminUserId = me.GetProperty("userId").GetString()
            ?? throw new InvalidOperationException("/api/auth/me did not return userId.");

        var shortCode = $"e2e{TestNames.ShortSlug()}";
        var form = await seeder.CreateFormAsync(
            TestNames.Prefixed("page-form"), shortCode, siteAvailable: false, formCode: SubmitOnlyFormCode);
        await seeder.PublishFormAsync(form.Id);

        var processKey = $"e2e_{TestNames.ShortSlug()}";
        var workflowName = TestNames.Prefixed("wf-page-task");
        await seeder.CreateAndPublishWorkflowAsync(
            processKey,
            workflowName,
            assignee: adminUserId,
            userFormMode: "page",
            userFormShortCode: form.ShortCode);
        await seeder.StartExecutionAsync(processKey, instanceName: TestNames.Prefixed("page-complete"));

        await page.GotoAsync("/home");

        // Row-scoped, as #259 established: the bare workflow name also matches
        // the recent-executions list, where it correctly survives completion.
        var taskRow = page.GetByRole(AriaRole.Row).Filter(new() { HasText = workflowName });
        await Assertions.Expect(taskRow).ToBeVisibleAsync(new() { Timeout = 15_000 });

        await taskRow.GetByRole(AriaRole.Button, new() { Name = "Open", Exact = true }).ClickAsync();

        // Page mode routes rather than opening a modal -- and if it did open a
        // modal, the rest of this test would be asserting the path #259 already
        // covers, so the URL is checked rather than assumed.
        await Assertions.Expect(page).ToHaveURLAsync(
            new System.Text.RegularExpressions.Regex(@"/workflow-tasks/.+/form"),
            new() { Timeout = 15_000 });

        await Assertions.Expect(page.GetByRole(AriaRole.Button, new() { Name = "Submit review" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await page.GetByRole(AriaRole.Button, new() { Name = "Submit review" }).ClickAsync();

        // TaskFormPage navigates to "/" on success. A client-side route change,
        // so the query cache survives it -- which is why the panel could come
        // back stale.
        await Assertions.Expect(page.GetByRole(AriaRole.Heading, new() { Name = "My Tasks" }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        await Assertions.Expect(taskRow).ToHaveCountAsync(0, new() { Timeout = 20_000 });
    }
}
