using AutoNate.E2E.Tests.Support;
using Microsoft.Playwright;
using Xunit;

namespace AutoNate.E2E.Tests;

/// <summary>
/// The jobs surface, driven the way an operator drives it (#172).
/// </summary>
/// <remarks>
/// <para>
/// Untraited, so it runs on every merge. Nothing here needs the engine: the
/// interesting states are an execution with no jobs and an execution whose jobs
/// could not be read, and both are reachable by controlling what the endpoint
/// answers.
/// </para>
/// <para>
/// The <b>unreachable-versus-empty</b> distinction is the one the AC calls out,
/// and it is the one a test is most likely to skip: both render "no rows", and
/// only the words tell them apart. An operator who reads "nothing is stuck" when
/// the truth is "nobody could ask" has been told the opposite of what is known.
/// </para>
/// </remarks>
[Collection(AutoNateE2ECollection.Name)]
public sealed class JobsPanelAccessibilityTests : E2ETestBase
{
    public JobsPanelAccessibilityTests(AutoNateE2EFixture fixture) : base(fixture) { }

    private const string InstanceId = "e2e-jobs-panel-instance";

    [Fact]
    public async Task An_execution_with_no_jobs_says_so_and_an_unreadable_one_says_something_else()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        // ── Empty: the engine answered, and there is nothing.
        await page.RouteAsync($"**/api/executions/{InstanceId}/jobs", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = "[]"
            }));

        await OpenJobsTabAsync(page);

        var panel = page.GetByRole(AriaRole.Region, new() { Name = "Jobs and timers" });
        await Assertions.Expect(panel.GetByText("Nothing scheduled or stuck"))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });

        // It is NOT an alert. An empty execution is a normal state, and wearing
        // role="alert" would interrupt a screen-reader user to tell them
        // everything is fine -- the defect #597 shipped on the freshness
        // indicator, which is why this is asserted rather than assumed.
        //
        // Scoped to the panel: this page carries several Alerts of its own, and
        // a page-wide role check would be asserting about all of them.
        await Assertions.Expect(panel.GetByRole(AriaRole.Alert)).Not.ToBeVisibleAsync();

        // ── Unreadable: the engine did not answer.
        await page.UnrouteAsync($"**/api/executions/{InstanceId}/jobs");
        await page.RouteAsync($"**/api/executions/{InstanceId}/jobs", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 503,
                ContentType = "application/json",
                Body = "{\"error\":\"engine unavailable\"}"
            }));

        await page.ReloadAsync();
        await OpenJobsTabAsync(page);

        // The words, which are the whole distinction.
        await Assertions.Expect(page.GetByText("did not answer", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 15_000 });
        await Assertions.Expect(page.GetByText("is not the same as having none", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // And this one IS assertive: it is a failure the operator must not read
        // past, and the empty state above proves the role is not simply always
        // set -- without that half, this assertion passes against a panel that
        // shouts at everything.
        await Assertions.Expect(
                page.GetByRole(AriaRole.Region, new() { Name = "Jobs and timers" })
                    .GetByRole(AriaRole.Alert))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });
    }

    [Fact]
    public async Task The_failure_detail_expands_by_keyboard_alone()
    {
        await using var session = await NewSignedInAsAdminAsync();
        var page = session.Page;

        await page.RouteAsync($"**/api/executions/{InstanceId}/jobs", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = """
                    [{"id":"job-1","queue":"DeadLetter","processInstanceId":"e2e-jobs-panel-instance",
                      "processDefinitionId":"p:1:1","elementId":"boom","elementName":"Charge card",
                      "retries":0,"exceptionMessage":"Payment gateway refused the connection",
                      "dueAtUtc":null,"createdAtUtc":null}]
                    """
            }));
        await page.RouteAsync($"**/api/executions/{InstanceId}/jobs/job-1/exception**", route =>
            route.FulfillAsync(new RouteFulfillOptions
            {
                Status = 200,
                ContentType = "application/json",
                Body = "{\"jobId\":\"job-1\",\"stack\":\"java.net.ConnectException: refused\\n  at Gateway.charge\"}"
            }));

        await OpenJobsTabAsync(page);

        var expander = page.GetByRole(AriaRole.Button,
            new() { Name = "Show the failure detail for Charge card" });
        await Assertions.Expect(expander).ToBeVisibleAsync(new() { Timeout = 15_000 });

        // Focused by TABBING to it, not by .FocusAsync(). A control reachable
        // only by a direct focus call is not keyboard-operable -- that is the
        // failure this walk exists to catch, and it is invisible to a click test.
        var reached = false;
        for (var i = 0; i < 60 && !reached; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            reached = await expander.EvaluateAsync<bool>("el => el === document.activeElement");
        }

        Assert.True(reached, "The failure-detail expander could not be reached with Tab alone.");

        await page.Keyboard.PressAsync("Enter");

        await Assertions.Expect(page.GetByText("java.net.ConnectException", new() { Exact = false }))
            .ToBeVisibleAsync(new() { Timeout = 10_000 });

        // The state is announced, not just drawn: a screen-reader user needs to
        // know the row expanded, and the arrow rotating tells them nothing.
        await Assertions.Expect(
                page.GetByRole(AriaRole.Button, new() { Name = "Hide the failure detail for Charge card" }))
            .ToHaveAttributeAsync("aria-expanded", "true", new() { Timeout = 10_000 });
    }

    private static async Task OpenJobsTabAsync(IPage page)
    {
        await page.GotoAsync($"/executions/{InstanceId}");
        var tab = page.GetByRole(AriaRole.Tab, new() { Name = "Jobs & Timers" });
        await Assertions.Expect(tab).ToBeVisibleAsync(new() { Timeout = 20_000 });
        await tab.ClickAsync();
    }
}
