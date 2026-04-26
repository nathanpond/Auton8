using AutoNate.Web.Authorization;
using AutoNate.Web.Authorization.EntityTypes;
using Xunit;

namespace AutoNate.Web.Tests.Authorization;

public sealed class EntityRegistryTests
{
    [Fact]
    public void All_RegistersEightCoreKinds()
    {
        var registry = new EntityRegistry(CoreEntityTypes.All);
        var kinds = registry.All.Select(t => t.Kind).ToHashSet();

        Assert.Equal(8, kinds.Count);
        Assert.Contains(EntityKinds.User, kinds);
        Assert.Contains(EntityKinds.Group, kinds);
        Assert.Contains(EntityKinds.Role, kinds);
        Assert.Contains(EntityKinds.RecordType, kinds);
        Assert.Contains(EntityKinds.Record, kinds);
        Assert.Contains(EntityKinds.WorkflowModel, kinds);
        Assert.Contains(EntityKinds.WorkflowExecution, kinds);
        Assert.Contains(EntityKinds.WorkflowTask, kinds);
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
    [InlineData(EntityKinds.WorkflowModel, "publish")]
    [InlineData(EntityKinds.WorkflowTask, "complete")]
    [InlineData(EntityKinds.WorkflowExecution, "override")]
    [InlineData(EntityKinds.User, "deactivate")]
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
    [InlineData(EntityKinds.User, "supervisor")]
    [InlineData(EntityKinds.WorkflowExecution, "startedby")]
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
