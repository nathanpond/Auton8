namespace AutoNate.Web.Services.Records.Fields;

public interface IFieldTypeRegistry
{
    IFieldType Get(string dataType);

    bool TryGet(string dataType, out IFieldType fieldType);

    IReadOnlyCollection<IFieldType> All { get; }
}

public sealed class UnknownFieldTypeException(string dataType)
    : Exception($"Unknown field data type '{dataType}'.");
