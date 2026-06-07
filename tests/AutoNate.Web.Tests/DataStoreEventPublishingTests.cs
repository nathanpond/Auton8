using System.Net.Http.Headers;
using System.Net.Http.Json;
using AutoNate.Web.Endpoints;
using AutoNate.Web.Services.DataStores;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class DataStoreEventPublishingTests
{
    // End-to-end smoke test asserting that a file upload publishes a
    // datastore.file.uploaded event with the resource shape the catalog
    // promises. If this regresses, the catalog and runtime have drifted
    // and the SPA Events admin page will show a stale entry.
    [Fact]
    public async Task PostUploadFile_publishes_file_uploaded()
    {
        var datastoreRoot = Path.Combine(
            Path.GetTempPath(),
            "autonate-datastore-event-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(datastoreRoot);

        await using var factory = await AutoNateWebApplicationFactory.CreateAsync(
            new Dictionary<string, string?>
            {
                ["Data:Root"] = datastoreRoot
            });
        var client = factory.CreateClient();

        // Prime the dev auto-login cookie via a GET. The auto-login skips
        // POSTs, so any test that uploads needs an authenticated cookie
        // already on the client.
        (await client.GetAsync("/api/datastores")).EnsureSuccessStatusCode();
        factory.RecordedAuditEvents.Clear();

        // Create a file-type store.
        var createResp = await client.PostAsJsonAsync(
            "/api/datastores",
            new CreateDataStoreRequest(
                Name: "test-store-" + Guid.NewGuid().ToString("N").Substring(0, 8),
                Description: "audit-event test fixture",
                Kind: "FileType"));
        createResp.EnsureSuccessStatusCode();
        var store = await createResp.Content.ReadFromJsonAsync<DataStoreSummary>();
        Assert.NotNull(store);

        // Upload a file.
        using var multipart = new MultipartFormDataContent();
        multipart.Add(new StringContent("/"), "folder");
        var bytes = System.Text.Encoding.UTF8.GetBytes("audit-event smoke test contents");
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        multipart.Add(fileContent, "file", "hello.txt");
        var uploadResp = await client.PostAsync($"/api/datastores/{store!.Id}/files", multipart);
        uploadResp.EnsureSuccessStatusCode();

        // Assert the upload event landed on the bus with the expected shape.
        var uploaded = Assert.Single(
            factory.RecordedAuditEvents.Events,
            e => e.EventType == DataStoreEventTypes.FileUploaded);
        Assert.Equal(DataStoreEventTopic.TopicName, uploaded.Topic);
        Assert.Equal(DataStoreResourceKinds.File, uploaded.ResourceKind);

        var resource = uploaded.Resource!;
        Assert.Equal(store.Id, GetProp<Guid>(resource, "datastoreId"));
        Assert.Equal("/", GetProp<string>(resource, "folderPath"));
        Assert.Equal("hello.txt", GetProp<string>(resource, "filename"));
        Assert.NotEqual(Guid.Empty, GetProp<Guid>(resource, "id"));

        var details = uploaded.Details!;
        Assert.Equal((long)bytes.Length, GetProp<long>(details, "sizeBytes"));

        // The create-store event from the setup step should also be
        // recorded — confirm it fires too so a regression that loses
        // both events is caught even if the upload assertion happens
        // to keep passing.
        Assert.Contains(
            factory.RecordedAuditEvents.Events,
            e => e.EventType == DataStoreEventTypes.Created);
    }

    private static T GetProp<T>(object obj, string name)
    {
        var prop = obj.GetType().GetProperty(name)
            ?? throw new InvalidOperationException(
                $"Resource/details payload missing property '{name}'. Found: " +
                string.Join(", ", obj.GetType().GetProperties().Select(p => p.Name)));
        var value = prop.GetValue(obj);
        Assert.NotNull(value);
        return (T)value!;
    }

    // Mirror of DataStore so the test doesn't depend on the EF entity type.
    // Only Id is used here; widen if more fields become relevant.
    private sealed record DataStoreSummary(Guid Id);
}
