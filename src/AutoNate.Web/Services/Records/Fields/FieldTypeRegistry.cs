namespace AutoNate.Web.Services.Records.Fields;

public sealed class FieldTypeRegistry : IFieldTypeRegistry
{
    private readonly IReadOnlyDictionary<string, IFieldType> _byDataType;

    public FieldTypeRegistry(IEnumerable<IFieldType> fieldTypes)
    {
        var map = new Dictionary<string, IFieldType>(StringComparer.Ordinal);
        foreach (var fieldType in fieldTypes)
        {
            if (map.ContainsKey(fieldType.DataType))
            {
                throw new InvalidOperationException(
                    $"Field type '{fieldType.DataType}' is registered more than once.");
            }

            map.Add(fieldType.DataType, fieldType);
        }

        _byDataType = map;
    }

    public IReadOnlyCollection<IFieldType> All => _byDataType.Values.ToList();

    public IFieldType Get(string dataType)
    {
        if (!_byDataType.TryGetValue(dataType, out var fieldType))
        {
            throw new UnknownFieldTypeException(dataType);
        }

        return fieldType;
    }

    public bool TryGet(string dataType, out IFieldType fieldType)
    {
        if (_byDataType.TryGetValue(dataType, out var found))
        {
            fieldType = found;
            return true;
        }

        fieldType = null!;
        return false;
    }
}
