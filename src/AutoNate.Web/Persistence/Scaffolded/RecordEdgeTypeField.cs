using System;

namespace AutoNate.Web.Persistence.Scaffolded;

public partial class RecordEdgeTypeField
{
    public Guid Id { get; set; }

    public Guid EdgeTypeId { get; set; }

    public string FieldKey { get; set; } = null!;

    public string DisplayName { get; set; } = null!;

    public string DataType { get; set; } = null!;

    public string Config { get; set; } = "{}";

    public bool IsRequired { get; set; }

    public int SortOrder { get; set; }
}
