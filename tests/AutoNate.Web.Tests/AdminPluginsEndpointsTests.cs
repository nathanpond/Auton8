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

    /// <summary>
    /// Any unloadable assembly is a 400, not a 500 — whatever shape it is (#585).
    /// </summary>
    /// <remarks>
    /// <para>Surfaced as a flaky test: the same upload usually failed at
    /// <c>LoadFromAssemblyPath</c>, inside a try that returned a result, but
    /// sometimes failed earlier — resolving the entry path, constructing the load
    /// context — where the outer try had only a <c>finally</c> and the exception
    /// escaped to the endpoint as a 500.</para>
    ///
    /// <para><b>Two distinct corrupt shapes, deliberately.</b> A single byte
    /// sequence would let the mapping be tuned to one loader exception, which is
    /// how this shipped: the loader's exception type for corrupt input is not
    /// contractual, so a catch list tuned to what was seen is a catch list that
    /// will be wrong again.</para>
    /// </remarks>
    [Theory]
    [InlineData("fake assembly bytes")]
    [InlineData("MZ\u0000\u0000truncated-pe-header")]
    public async Task Enable_OnAnyUnloadableAssembly_Returns400NotAServerError(string entryBytes)
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var zip = BuildZip(arch =>
        {
            AddText(arch, "plugin.json",
                """{"name":"Corrupt","version":"1.0.0","entryAssembly":"Corrupt.dll"}""");
            AddText(arch, "Corrupt.dll", entryBytes);
        });

        var uploadResponse = await UploadAsync(client, zip, "corrupt.zip");
        uploadResponse.EnsureSuccessStatusCode();
        var created = await uploadResponse.Content.ReadFromJsonAsync<PluginListItem>();

        var enableResponse = await client.PostAsync($"/api/admin/plugins/{created!.Id}/enable", content: null);

        Assert.Equal(HttpStatusCode.BadRequest, enableResponse.StatusCode);
        Assert.NotEqual(HttpStatusCode.InternalServerError, enableResponse.StatusCode);

        var listed = await client.GetFromJsonAsync<List<PluginListItem>>("/api/admin/plugins");
        var row = listed!.Single(p => p.Id == created.Id);
        Assert.Equal(PluginStatusDto.Disabled, (PluginStatusDto)(int)row.Status);
        Assert.NotNull(row.LastError);
    }

    /// <summary>
    /// The broadened catch does not swallow a genuine success (#585).
    /// </summary>
    /// <remarks>
    /// The complement. Widening a catch until nothing escapes is trivially
    /// satisfiable by never succeeding, and a plugin host that reports every
    /// enable as a failure would pass every assertion above.
    /// </remarks>
    [Fact]
    public async Task A_loadable_plugin_still_enables()
    {
        await using var factory = await AutoNateWebApplicationFactory.CreateAsync();
        var client = factory.CreateClient();
        await PrimeAuthAsync(client);

        var sample = SamplePluginZip();
        var uploadResponse = await UploadAsync(client, sample, "sample.zip");
        uploadResponse.EnsureSuccessStatusCode();
        var created = await uploadResponse.Content.ReadFromJsonAsync<PluginListItem>();

        var enableResponse = await client.PostAsync($"/api/admin/plugins/{created!.Id}/enable", content: null);

        Assert.True(
            enableResponse.IsSuccessStatusCode,
            $"A loadable plugin must still enable; got {enableResponse.StatusCode}: "
            + await enableResponse.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// The built sample plugin, as an uploadable zip.
    /// </summary>
    /// <remarks>
    /// <b>A missing sample FAILS rather than skipping.</b> The first version of
    /// this helper looked next to the test assembly, found nothing, and returned
    /// null — so the complement above returned early and passed without asserting
    /// anything. The plugin is copied to <c>test-plugins/SamplePlugin/</c>; if it
    /// ever is not, this test has stopped checking and should say so rather than
    /// go quietly green.
    /// </remarks>
    private static byte[] SamplePluginZip()
    {
        var dll = Path.Combine(
            AppContext.BaseDirectory, "test-plugins", "SamplePlugin", "AutoNate.Web.Tests.SamplePlugin.dll");
        Assert.True(
            File.Exists(dll),
            $"Expected the sample plugin at {dll}. Without it this test asserts nothing.");

        return BuildZip(arch =>
        {
            AddText(arch, "plugin.json",
                """{"name":"Sample","version":"1.0.0","entryAssembly":"AutoNate.Web.Tests.SamplePlugin.dll"}""");
            var entry = arch.CreateEntry("AutoNate.Web.Tests.SamplePlugin.dll");
            using var target = entry.Open();
            using var source = File.OpenRead(dll);
            source.CopyTo(target);
        });
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
