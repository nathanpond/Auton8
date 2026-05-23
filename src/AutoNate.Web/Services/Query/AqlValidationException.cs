namespace AutoNate.Web.Services.Query;

public sealed class AqlValidationException : Exception
{
    public IReadOnlyList<string> Errors { get; }

    public AqlValidationException(string message)
        : base(message)
    {
        Errors = new[] { message };
    }

    public AqlValidationException(IReadOnlyList<string> errors)
        : base(errors.Count == 1 ? errors[0] : $"AQL query has {errors.Count} errors.")
    {
        Errors = errors;
    }
}
