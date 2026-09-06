using System.Net;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Models;
using Xunit;

namespace AutoNate.Web.Tests;

// #153: `runAs="system"` needs a permission beyond Publish, checked on the
// server.
//
// The studio hides the option from an author who lacks the permission, but a
// hidden control is not a gate — the XML arrives over HTTP and can say
// anything. This pins the refusal.
//
// The setup matters more than it looks. A first attempt used the auto-login
// admin against a random workflow id and got a 403 — from the route's existing
// Publish filter, with an empty body, before this gate was ever reached. It
// would have passed while asserting nothing. So the actor here is granted
// Publish explicitly and nothing else: the only thing standing between them and
// a successful deploy is the elevated-script permission.
[Trait("Category", "Integration")]
public sealed class ScriptIdentityPublishGateTests
{
    private static string BpmnWith(string? runAs)
    {
        var attr = runAs is null ? "" : $" autonate:runAs=\"{runAs}\"";
        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              xmlns:autonate="http://autonate.dev/workflows"
                              id="Definitions_1" targetNamespace="http://autonate.dev/workflows">
              <bpmn:process id="gate_flow" name="Gate Flow" isExecutable="true">
                <bpmn:startEvent id="s" />
                <bpmn:userTask id="u" name="Approve" />
                <bpmn:scriptTask id="t" name="Compute" scriptFormat="javascript"{attr}>
                  <bpmn:script>variables.set("x", 1);</bpmn:script>
                </bpmn:scriptTask>
                <bpmn:sequenceFlow id="f1" sourceRef="s" targetRef="u" />
                <bpmn:sequenceFlow id="f2" sourceRef="u" targetRef="t" />
              </bpmn:process>
            </bpmn:definitions>
            """;
    }

    private sealed record UserDto(long Id, Guid UserId, string Username);
    private sealed record AntiforgeryTokenDto(string Token, string FormFieldName, string HeaderName);

    // An author who may publish workflows and nothing more.
    private static async Task<HttpClient> AuthorWithPublishOnlyAsync(
        AutoNateWebApplicationFactory factory)
    {
        var admin = factory.CreateClient();
        (await admin.GetAsync("/api/auth/me")).EnsureSuccessStatusCode();

        var username = "author-" + Guid.NewGuid().ToString("N")[..8];
        var created = await admin.PostAsJsonAsync("/api/users", new UserEndpoints.CreateUserRequest(
            Username: username, FirstName: "Ada", LastName: "Author",
            Password: "p@ssword123", Email: "ada@x.com"));
        created.EnsureSuccessStatusCode();
        var author = await created.Content.ReadFromJsonAsync<UserDto>()
            ?? throw new InvalidOperationException("User creation response was empty.");

        foreach (var action in new[] { "publish", "edit", "view" })
        {
            var grant = await admin.PostAsJsonAsync("/api/admin/grants",
                new PermissionGrantEndpoints.CreateGrantRequest(
                    PrincipalKind: "user",
                    PrincipalId: author.UserId.ToString(),
                    Action: action,
                    SelectorString: "/workflowmodel/*",
                    Effect: "allow",
                    Priority: 0));
            grant.EnsureSuccessStatusCode();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Clear();
        var tokenResp = await client.GetAsync("/api/auth/antiforgery");
        tokenResp.EnsureSuccessStatusCode();
        var tokens = await tokenResp.Content.ReadFromJsonAsync<AntiforgeryTokenDto>()
            ?? throw new InvalidOperationException("Antiforgery token response was empty.");
        var login = await client.PostAsync("/account/login", new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                [tokens.FormFieldName] = tokens.Token,
                ["username"] = username,
                ["password"] = "p@ssword123",
            }));
        login.EnsureSuccessStatusCode();
        return client;
    }

    private static Task<AutoNateWebApplicationFactory> HostAsync() =>
        // Enforcement fully on, and deliberately NOT assigning super-admin: a
        // super-admin short-circuits to Allow inside the authorizer, so this
        // gate would never be reached and the assertion would be vacuous.
        AutoNateWebApplicationFactory.CreateAsync(new Dictionary<string, string?>
        {
            ["Authorization:Enabled"] = "true",
            ["Authorization:Enforcement"] = "full",
            // The bootstrap admin needs SuperAdmin to run the create-user +
            // create-grant setup under full enforcement. It applies to users
            // that exist at startup, and the author below is created after it,
            // so the author does not inherit it — which is essential: a
            // super-admin short-circuits to Allow inside the authorizer and
            // would sail through the gate this class exists to test.
            ["Authorization:AssignSuperAdminToAllExistingUsers"] = "true",
        });

    // The workflow has to EXIST before it can be published. Publishing a
    // random id returns 403 from the route's own instance check — an empty
    // body, before this gate is reached — which is how the first two versions
    // of this class passed while asserting nothing.
    private static async Task<HttpResponseMessage> PublishAsync(HttpClient client, string? runAs)
    {
        var model = new WorkflowModel
        {
            Id = Guid.NewGuid(),
            Name = "Gate Flow " + Guid.NewGuid().ToString("N")[..6],
            ProcessKey = "gate_flow_" + Guid.NewGuid().ToString("N")[..6],
            BpmnXml = BpmnWith(runAs),
        };
        var saved = await client.PostAsJsonAsync("/api/workflows/", model);
        saved.EnsureSuccessStatusCode();
        var stored = await saved.Content.ReadFromJsonAsync<WorkflowModel>()
            ?? throw new InvalidOperationException("Save response was empty.");

        return await client.PostAsJsonAsync($"/api/workflows/{stored.Id}/publish", stored);
    }

    [Fact]
    public async Task PublishingASystemScriptWithoutThePermissionIsRefusedByTheServer()
    {
        await using var factory = await HostAsync();
        var author = await AuthorWithPublishOnlyAsync(factory);

        var response = await PublishAsync(author, "system");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // The body distinguishes this from the route's own Publish filter,
        // which returns 403 with nothing in it. Without checking the body this
        // test would pass against the wrong refusal.
        Assert.Contains("elevatescript", await response.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("workflowAuthor")]
    public async Task TheGateFiresOnlyForASystemDeclaration(string? runAs)
    {
        // Positive control. If publishing were refused for everyone the test
        // above would pass while telling us nothing — the point is that
        // ordinary workflows do not need an elevated permission.
        await using var factory = await HostAsync();
        var author = await AuthorWithPublishOnlyAsync(factory);

        var response = await PublishAsync(author, runAs);

        // Asserts the absence of ANY 403, not merely of the elevated-permission
        // one. An earlier version checked only for the message and passed
        // happily against the route's own empty-bodied 403 — which is exactly
        // the failure this control is supposed to rule out. The publish may
        // still fail further along, since there is no Flowable to deploy to.
        var body = await response.Content.ReadAsStringAsync();
        Assert.False(
            response.StatusCode == HttpStatusCode.Forbidden,
            $"runAs={runAs ?? "unset"} was forbidden, so the author cannot publish at all " +
            $"and the refusal test proves nothing: {body}");
    }
}
