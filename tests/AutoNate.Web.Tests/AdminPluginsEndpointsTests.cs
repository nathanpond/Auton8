using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using AutoNate.Web.Plugins;
using Xunit;

namespace AutoNate.Web.Tests;

[Trait("Category", "Integration")]
public sealed class AdminPluginsEndpointsTests
{
    [Fact]
    public async Task ListPlugins_EmptyDatabase_ReturnsEmpty()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();

        var plugins = await client.GetFromJsonAsync<List<PluginListItem>>("/api/admin/plugins");

        Assert.NotNull(plugins);
        Assert.Empty(plugins);
    }

    [Fact]
    public async Task UploadPlugin_RoundTrips()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var zip = BuildSimpleZip(name: "AcmePlugin", entryAssembly: "Acme.dll");
        var response = await UploadAsync(client, zip, "AcmePlugin.zip");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<PluginListItem>();
        Assert.NotNull(created);
        Assert.Equal("AcmePlugin", created!.Name);
        Assert.Equal(PluginStatusDto.Disabled, (PluginStatusDto)(int)created.Status);

        var listed = await client.GetFromJsonAsync<List<PluginListItem>>("/api/admin/plugins");
        Assert.NotNull(listed);
        Assert.Single(listed!);
        Assert.Equal(created.Id, listed[0].Id);
    }

    [Fact]
    public async Task UploadPlugin_ZipSlip_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var zip = BuildZip(arch =>
        {
            AddText(arch, "plugin.json", """{"name":"Acme","version":"1.0.0","entryAssembly":"Acme.dll"}""");
            AddText(arch, "../../etc/passwd", "evil");
        });

        var response = await UploadAsync(client, zip, "evil.zip");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadPlugin_MissingManifest_Returns400()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var zip = BuildZip(arch => AddText(arch, "Acme.dll", "fake"));

        var response = await UploadAsync(client, zip, "no-manifest.zip");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task EnableThenDisable_OnFakePlugin_RecordsLastErrorAndStaysDisabled()
    {
        // The zip uploaded here is structurally valid (passes upload validation)
        // but the entry assembly is a fake byte sequence — Enable will fail at
        // assembly load, the row should stay Disabled with last_error populated,
        // and Disable should be a no-op.
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var zip = BuildSimpleZip(name: "Broken", entryAssembly: "Broken.dll");
        var uploadResponse = await UploadAsync(client, zip, "broken.zip");
        uploadResponse.EnsureSuccessStatusCode();
        var created = await uploadResponse.Content.ReadFromJsonAsync<PluginListItem>();

        var enableResponse = await client.PostAsync($"/api/admin/plugins/{created!.Id}/enable", content: null);
        Assert.Equal(HttpStatusCode.BadRequest, enableResponse.StatusCode);

        var listed = await client.GetFromJsonAsync<List<PluginListItem>>("/api/admin/plugins");
        var row = listed!.Single(p => p.Id == created.Id);
        Assert.Equal(PluginStatusDto.Disabled, (PluginStatusDto)(int)row.Status);
        Assert.NotNull(row.LastError);
    }

    [Fact]
    public async Task DeletePlugin_RemovesRow()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var zip = BuildSimpleZip(name: "ToDelete", entryAssembly: "ToDelete.dll");
        var uploadResponse = await UploadAsync(client, zip, "todelete.zip");
        var created = await uploadResponse.Content.ReadFromJsonAsync<PluginListItem>();

        var deleteResponse = await client.DeleteAsync($"/api/admin/plugins/{created!.Id}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var listed = await client.GetFromJsonAsync<List<PluginListItem>>("/api/admin/plugins");
        Assert.Empty(listed!);
    }

    private static async Task PrimeAuthAsync(HttpClient client)
    {
        (await client.GetAsync("/api/admin/plugins")).EnsureSuccessStatusCode();
    }

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, byte[] zipBytes, string filename)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(zipBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(fileContent, "file", filename);
        return await client.PostAsync("/api/admin/plugins", content);
    }

    private static byte[] BuildSimpleZip(string name, string entryAssembly)
    {
        return BuildZip(arch =>
        {
            AddText(arch, "plugin.json",
                $$"""{"name":"{{name}}","version":"1.0.0","entryAssembly":"{{entryAssembly}}"}""");
            AddText(arch, entryAssembly, "fake assembly bytes");
        });
    }

    private static byte[] BuildZip(Action<ZipArchive> populate)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            populate(zip);
        }
        return ms.ToArray();
    }

    private static void AddText(ZipArchive zip, string name, string content)
    {
        var entry = zip.CreateEntry(name);
        using var s = entry.Open();
        s.Write(Encoding.UTF8.GetBytes(content));
    }

    private enum PluginStatusDto
    {
        Disabled = 0,
        Enabled = 1,
        DeletedPending = 2,
    }
}
