namespace AutoNate.Web.Authorization;

public readonly record struct EntityRef(string Kind, string Id)
{
    public override string ToString() => $"{Kind}/{Id}";
}
