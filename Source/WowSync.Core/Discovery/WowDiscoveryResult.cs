namespace WowSync.Core.Discovery;

using WowSync.Core.Validation;
using WowSync.Plugins.Abstractions.Runs;

public sealed record WowDiscoveryResult(
    WowContextSnapshot? Context,
    ValidationResult Validation);
