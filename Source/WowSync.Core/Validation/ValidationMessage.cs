// ReSharper disable NotAccessedPositionalProperty.Global
namespace WowSync.Core.Validation;

public sealed record ValidationMessage(
    ValidationSeverity Severity,
    string Code,
    string Message,
    string? FixHint = null);
