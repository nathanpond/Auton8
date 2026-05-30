namespace AutoNate.Web.Services.DataConnectors;

// Well-known built-in connector kinds. Plugin-contributed kinds use their
// own opaque key strings — host code never compares against a closed set.
public static class DataConnectorKinds
{
    public const string Rest = "rest";
    public const string Smb = "smb";
}
