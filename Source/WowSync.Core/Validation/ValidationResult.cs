// ReSharper disable UnusedMember.Global
namespace WowSync.Core.Validation;

public sealed record ValidationResult(
    bool IsOk,
    IReadOnlyList<ValidationMessage> Messages)
{
    public static ValidationResult Ok()
    {
        return new(IsOk: true, Messages: []);
    }

    public static ValidationResult From(IReadOnlyList<ValidationMessage> messages)
    {
        var ok = messages.All(m => m.Severity != ValidationSeverity.Error);
        return new ValidationResult(ok, messages);
    }
}
