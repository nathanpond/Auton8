namespace AutoNate.Web.Services.Records.Fields;

public readonly record struct FieldValidationError(string Code, string Message);

public sealed record class FieldValidationResult(IReadOnlyList<FieldValidationError> Errors)
{
    public static readonly FieldValidationResult Success = new(Array.Empty<FieldValidationError>());

    public bool IsValid => Errors.Count == 0;

    public static FieldValidationResult Fail(string code, string message) =>
        new(new[] { new FieldValidationError(code, message) });

    public static FieldValidationResult Fail(params FieldValidationError[] errors) =>
        new(errors);
}
