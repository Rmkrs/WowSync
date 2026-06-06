namespace WowSync.Core.Profiles;

public enum PatchMode
{
    // Only update existing leaf keys. Never add missing keys.
    UpdateOnly,

    // Allow adding missing leaf keys ONLY if the parent branch exists.
    AddIfParentExists,
}