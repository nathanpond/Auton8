using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EntityTypes;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

public sealed class EntityRegistryTests
{
    [Fact]
    public void All_RegistersEveryCoreKind()
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var kinds = registry.All.Select(t => t.Kind).ToHashSet();

        Assert.Equal(19, kinds.Count);
        Assert.Contains(EntityKinds.User, kinds);
        Assert.Contains(EntityKinds.Group, kinds);
        Assert.Contains(EntityKinds.Role, kinds);
        Assert.Contains(EntityKinds.RecordType, kinds);
        Assert.Contains(EntityKinds.Record, kinds);
        Assert.Contains(EntityKinds.WorkflowModel, kinds);
        Assert.Contains(EntityKinds.WorkflowExecution, kinds);
        Assert.Contains(EntityKinds.WorkflowTask, kinds);
        Assert.Contains(EntityKinds.Plugin, kinds);
        Assert.Contains(EntityKinds.Form, kinds);
        Assert.Contains(EntityKinds.ExternalConnection, kinds);
        Assert.Contains(EntityKinds.SystemIssue, kinds);
        Assert.Contains(EntityKinds.SiteConfig, kinds);
        Assert.Contains(EntityKinds.Project, kinds);
        Assert.Contains(EntityKinds.Cabinet, kinds);
        // Document and Folder joined the registry with archived-25 — they were enforced
        // on 22 routes and honoured by ContentAuthorizer's selectors, but the
        // Grants admin picker could not offer them because /api/admin/registry
        // is built from this list.
        Assert.Contains(EntityKinds.Document, kinds);
        Assert.Contains(EntityKinds.Folder, kinds);
        Assert.Contains(EntityKinds.Notebook, kinds);
        Assert.Contains(EntityKinds.Page, kinds);
    }

    [Fact]
    public void Get_KnownKind_ReturnsType()
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var record = registry.Get(EntityKinds.Record);
        Assert.Equal(EntityKinds.Record, record.Kind);
    }

    [Fact]
    public void Get_UnknownKind_Throws()
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        Assert.Throws<KeyNotFoundException>(() => registry.Get("does-not-exist"));
    }

    [Fact]
    public void TryGet_UnknownKind_ReturnsFalse()
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        Assert.False(registry.TryGet("does-not-exist", out var type));
        Assert.Null(type);
    }

    [Fact]
    public void DuplicateKind_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            new EntityRegistry(new[] { CoreEntityTypes.Record, CoreEntityTypes.Record }));
    }

    [Theory]
    [InlineData(EntityKinds.Record, "view")]
    [InlineData(EntityKinds.Record, "edit")]
    [InlineData(EntityKinds.Record, "assign")]
    [InlineData(EntityKinds.Record, "create")]
    [InlineData(EntityKinds.WorkflowModel, "publish")]
    [InlineData(EntityKinds.WorkflowTask, "complete")]
    [InlineData(EntityKinds.WorkflowExecution, "override")]
    [InlineData(EntityKinds.User, "unlock")]
    [InlineData(EntityKinds.User, "create")]
    [InlineData(EntityKinds.User, "delete")]
    [InlineData(EntityKinds.SystemIssue, "view")]
    [InlineData(EntityKinds.SystemIssue, "acknowledge")]
    [InlineData(EntityKinds.SystemIssue, "resolve")]
    [InlineData(EntityKinds.SystemIssue, "remediate")]
    [InlineData(EntityKinds.SiteConfig, "view")]
    [InlineData(EntityKinds.SiteConfig, "edit")]
    public void Actions_PerKind_AreDocumented(string kind, string action)
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var entityType = registry.Get(kind);
        Assert.Contains(action, entityType.Actions);
    }

    [Theory]
    [InlineData(EntityKinds.Record, "recordtype")]
    [InlineData(EntityKinds.Record, "assignee")]
    [InlineData(EntityKinds.Record, "creator")]
    [InlineData(EntityKinds.Record, "status")]
    [InlineData(EntityKinds.WorkflowExecution, "startedby")]
    [InlineData(EntityKinds.Form, "shortcode")]
    [InlineData(EntityKinds.Form, "published")]
    public void Tags_PerKind_AreDocumented(string kind, string tag)
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var entityType = registry.Get(kind);
        Assert.Contains(tag, entityType.Tags);
    }

    [Fact]
    public void ParseId_GuidKind_ParsesCanonicalForm()
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var record = registry.Get(EntityKinds.Record);
        var id = Guid.NewGuid();
        Assert.Equal(id.ToString(), record.ParseId(id.ToString()));
    }

    [Fact]
    public void ParseId_StringKind_ReturnsAsIs()
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var execution = registry.Get(EntityKinds.WorkflowExecution);
        Assert.Equal("flowable-process-instance-42", execution.ParseId("flowable-process-instance-42"));
    }
}
