// ReSharper disable IdentifierTypo
// ReSharper disable StringLiteralTypo
namespace WowSync.Plugins.BuiltIn.Altoholic.DataStore;

using System.Globalization;
using WowSync.Core.Lua;
using WowSync.Plugins.Abstractions.Operations;

public sealed class MergeDataStoreAltAccountOperation : IOperation
{
    private sealed record ModuleSpec(string LogTag, string FileName, string CharactersVar);

    private static readonly ModuleSpec[] modules =
    [
        new("DataStore_Inventory",     "DataStore_Inventory.lua",     "DataStore_Inventory_Characters"),
        new("DataStore_Containers",    "DataStore_Containers.lua",    "DataStore_Containers_Characters"),

        new("DataStore_Achievements",  "DataStore_Achievements.lua",  "DataStore_Achievements_Characters"),
        new("DataStore_Quests",        "DataStore_Quests.lua",        "DataStore_Quests_Characters"),
        new("DataStore_Reputations",   "DataStore_Reputations.lua",   "DataStore_Reputations_Characters"),
        new("DataStore_Stats",         "DataStore_Stats.lua",         "DataStore_Stats_Characters"),

        new("DataStore_Auctions",      "DataStore_Auctions.lua",      "DataStore_Auctions_Characters"),
        new("DataStore_Crafts",        "DataStore_Crafts.lua",        "DataStore_Crafts_Characters"),
        new("DataStore_Currencies",    "DataStore_Currencies.lua",    "DataStore_Currencies_Characters"),

        new("DataStore_Agenda",        "DataStore_Agenda.lua",        "DataStore_Agenda_Characters"),
        new("DataStore_Mails",         "DataStore_Mails.lua",         "DataStore_Mails_Characters"),

        new("DataStore_Pets",          "DataStore_Pets.lua",          "DataStore_Pets_Characters"),
        new("DataStore_Spells",        "DataStore_Spells.lua",        "DataStore_Spells_Characters"),
        new("DataStore_Talents",       "DataStore_Talents.lua",       "DataStore_Talents_Characters"),

        new("DataStore_Garrisons",     "DataStore_Garrisons.lua",     "DataStore_Garrisons_Characters"),

        new("DataStore_Characters",    "DataStore_Characters.lua",    "DataStore_Characters_Info"),
    ];

    private readonly string mainSavedVariablesPath;
    private readonly IReadOnlyList<string> altSavedVariablesPaths;

    private readonly string mainDataStoreLuaPath;

    public MergeDataStoreAltAccountOperation(
        string mainAccountSavedVariablesPath,
        IReadOnlyList<string> altAccountSavedVariablesPaths)
    {
        this.mainSavedVariablesPath = mainAccountSavedVariablesPath;
        this.altSavedVariablesPaths = altAccountSavedVariablesPaths;

        this.mainDataStoreLuaPath = Path.Combine(this.mainSavedVariablesPath, "DataStore.lua");

        this.Description = $"Merge Altoholic DataStore (All alts -> Main): {altAccountSavedVariablesPaths.Count} alt(s)";

        var touched = new List<string> { this.mainDataStoreLuaPath };
        touched.AddRange(modules.Select(m => Path.Combine(this.mainSavedVariablesPath, m.FileName)));
        this.TouchedPaths = touched;
    }

    public string Description { get; }
    public IReadOnlyList<string> TouchedPaths { get; }

    public async Task ExecuteAsync(IOperationContext context, CancellationToken ct)
    {
        if (!context.FileSystem.FileExists(this.mainDataStoreLuaPath))
        {
            context.Logger.Err($"DataStore: Main identity file missing. '{this.mainDataStoreLuaPath}'");
            return;
        }

        // Parse main identity once
        var mainDsOriginalText = await context.FileSystem.ReadAllTextAsync(this.mainDataStoreLuaPath, ct).ConfigureAwait(false);
        var mainDsText = mainDsOriginalText;

        LuaDocument mainDsDoc;
        try
        {
            mainDsDoc = new LuaParser(mainDsText).ParseDocument();
        }
        catch (Exception ex)
        {
            throw new FormatException($"Failed to parse main identity file '{this.mainDataStoreLuaPath}'. {ex.Message}", ex);
        }

        var mainIndex = DataStoreMergeHelpers.BuildCharacterIndex(mainDsDoc);

        // Mutable lookup over time as we add identities
        var mainByGuid = mainIndex.ToDictionary(x => x.CharacterGuid, StringComparer.Ordinal);

        var identityAdds = 0;

        // Parse all main modules once, keep them in memory
        var mainModuleDocs = new Dictionary<string, LuaDocument>(StringComparer.Ordinal);
        var mainModuleDirty = new HashSet<string>(StringComparer.Ordinal);
        var mainModuleOriginalText = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var m in modules)
        {
            var mainModulePath = Path.Combine(this.mainSavedVariablesPath, m.FileName);

            if (!context.FileSystem.FileExists(mainModulePath))
            {
                context.Logger.Err($"{m.LogTag}: Main module missing. '{mainModulePath}'");
                continue;
            }

            var mainModuleText = await context.FileSystem.ReadAllTextAsync(mainModulePath, ct).ConfigureAwait(false);
            mainModuleOriginalText[m.FileName] = mainModuleText;

            try
            {
                mainModuleDocs[m.FileName] = new LuaParser(mainModuleText).ParseDocument();
            }
            catch (Exception ex)
            {
                throw new FormatException($"{m.LogTag}: Failed to parse main module '{mainModulePath}'. {ex.Message}", ex);
            }

            var mainDoc = mainModuleDocs[m.FileName];

            if (!LuaNavigator.TryGetValueAtPath(mainDoc, m.CharactersVar, out var mainCharsValue) ||
                mainCharsValue is not LuaValue.LuaTable)
            {
                context.Logger.Err($"{m.LogTag}: Missing or invalid '{m.CharactersVar}' in main. Skipping this module entirely.");
                mainModuleDocs.Remove(m.FileName);
            }
        }

        var moduleAdds = 0;
        var moduleReplaces = 0;

        // Process each alt in order
        foreach (var altSv in this.altSavedVariablesPaths)
        {
            var altDataStoreLuaPath = Path.Combine(altSv, "DataStore.lua");

            if (!context.FileSystem.FileExists(altDataStoreLuaPath))
            {
                context.Logger.Warn($"DataStore: Alt identity file missing, skipping alt. '{altDataStoreLuaPath}'");
                continue;
            }

            var altDsText = await context.FileSystem.ReadAllTextAsync(altDataStoreLuaPath, ct).ConfigureAwait(false);

            LuaDocument altDsDoc;
            try
            {
                altDsDoc = new LuaParser(altDsText).ParseDocument();
            }
            catch (Exception ex)
            {
                throw new FormatException($"Failed to parse alt identity file '{altDataStoreLuaPath}'. {ex.Message}", ex);
            }

            var altIndex = DataStoreMergeHelpers.BuildCharacterIndex(altDsDoc);

            context.Logger.Info($"DataStore: Alt='{altSv}'. Main characters={mainByGuid.Count}, Alt characters={altIndex.Count}");

            // Validate mismatches vs current main state
            var currentMainIndex = mainByGuid.Values
                .OrderBy(x => x.ZeroBasedIndex)
                .ToList();

            if (!DataStoreMergeHelpers.ValidateNoMismatchesOrAbort(altIndex, currentMainIndex, context.Logger, "DataStore"))
            {
                // Abort whole operation on mismatch (strict mode)
                return;
            }

            // Extend identity as needed
            foreach (var a in altIndex)
            {
                if (mainByGuid.ContainsKey(a.CharacterGuid))
                {
                    continue;
                }

                identityAdds++;

                var newZeroBased = mainByGuid.Count; // append position
                var newRef = a with { ZeroBasedIndex = newZeroBased };

                mainByGuid[a.CharacterGuid] = newRef;

                context.Logger.Info(
                    context.IsDryRun
                        ? $"DataStore: Would add missing char to main identity. guid='{a.CharacterGuid}', name='{a.Name}', fullId='{a.FullId}'"
                        : $"DataStore: Adding missing char to main identity. guid='{a.CharacterGuid}', name='{a.Name}', fullId='{a.FullId}'");

                mainDsDoc = DataStoreMergeHelpers.AddCharacterToMainIdentity(
                    mainDsDoc,
                    altDsDoc,
                    a,
                    context.Logger,
                    "DataStore");
            }

            // Build mapping for THIS alt after identity extensions
            var altGuidToMainLuaIndex = altIndex.ToDictionary(
                c => c.CharacterGuid,
                c => mainByGuid[c.CharacterGuid].ZeroBasedIndex + 1,
                StringComparer.Ordinal);

            // Apply each module from this alt into main module docs
            foreach (var m in modules)
            {
                if (!mainModuleDocs.TryGetValue(m.FileName, out var mainModuleDoc))
                {
                    // main module missing/failed earlier
                    continue;
                }

                var altModulePath = Path.Combine(altSv, m.FileName);
                if (!context.FileSystem.FileExists(altModulePath))
                {
                    context.Logger.Warn($"{m.LogTag}: Alt module missing, skipping. '{altModulePath}'");
                    continue;
                }

                var altModuleText = await context.FileSystem.ReadAllTextAsync(altModulePath, ct).ConfigureAwait(false);

                LuaDocument altModuleDoc;
                try
                {
                    altModuleDoc = new LuaParser(altModuleText).ParseDocument();
                }
                catch (Exception ex)
                {
                    throw new FormatException($"{m.LogTag}: Failed to parse alt module '{altModulePath}'. {ex.Message}", ex);
                }

                if (!LuaNavigator.TryGetValueAtPath(altModuleDoc, m.CharactersVar, out var altCharsValue) ||
                    altCharsValue is not LuaValue.LuaTable altCharsTable)
                {
                    context.Logger.Err($"{m.LogTag}: Missing or invalid '{m.CharactersVar}' in alt. Skipping this module for this alt.");
                    continue;
                }

                var altCharsLen = DataStoreMergeHelpers.GetArrayLength(altCharsTable);

                var updatesThisAltModule = 0;
                var updatedMainDoc = mainModuleDoc;

                foreach (var altChar in altIndex)
                {
                    var altLuaIndex = altChar.ZeroBasedIndex + 1;

                    if (altLuaIndex > altCharsLen)
                    {
                        context.Logger.Warn(
                            string.Create(CultureInfo.InvariantCulture, $"{m.LogTag}: Alt '{m.CharactersVar}' has no entry at index {altLuaIndex} ") +
                            string.Create(CultureInfo.InvariantCulture, $"(arrayLen={altCharsLen}, guid='{altChar.CharacterGuid}', name='{altChar.Name}'). Skipping this character."));
                        continue;
                    }

                    if (!LuaNavigator.TryGetValueAtPath(altModuleDoc, string.Create(CultureInfo.InvariantCulture, $"{m.CharactersVar}[{altLuaIndex}]"), out var altBranch))
                    {
                        context.Logger.Warn(
                            string.Create(CultureInfo.InvariantCulture, $"{m.LogTag}: Alt branch missing at index {altLuaIndex} ") +
                            $"(guid='{altChar.CharacterGuid}', name='{altChar.Name}'). Skipping this character.");
                        continue;
                    }

                    var mainLuaIndex = altGuidToMainLuaIndex[altChar.CharacterGuid];

                    var mainPath = string.Create(CultureInfo.InvariantCulture, $"{m.CharactersVar}[{mainLuaIndex}]");

                    // If main already has the exact same branch, do nothing: avoids dirtying + avoids rewrites.
                    if (LuaNavigator.TryGetValueAtPath(updatedMainDoc, mainPath, out var existingMainBranch) &&
                        LuaDeepComparer.AreEquivalent(existingMainBranch, altBranch))
                    {
                        // Keep this quiet:
                        // context.Logger.Info($"{m.LogTag}: No change for guid='{altChar.CharacterGuid}', name='{altChar.Name}', mainIndex={mainLuaIndex} (UNCHANGED)");
                        continue;
                    }

                    var existedBefore = LuaNavigator.TryGetValueAtPath(updatedMainDoc, mainPath, out _);
                    if (existedBefore)
                    {
                        moduleReplaces++;
                    }
                    else
                    {
                        moduleAdds++;
                    }

                    context.Logger.Info(
                        context.IsDryRun
                            ? string.Create(CultureInfo.InvariantCulture, $"{m.LogTag}: Would set guid='{altChar.CharacterGuid}', name='{altChar.Name}', mainIndex={mainLuaIndex}, altIndex={altLuaIndex} ({(existedBefore ? "REPLACE" : "ADD")})"
)
                            : string.Create(CultureInfo.InvariantCulture, $"{m.LogTag}: Setting guid='{altChar.CharacterGuid}', name='{altChar.Name}', mainIndex={mainLuaIndex}, altIndex={altLuaIndex} ({(existedBefore ? "REPLACE" : "ADD")})"));

                    updatedMainDoc = LuaNavigator.SetValueAtPath(updatedMainDoc, mainPath, altBranch);
                    updatesThisAltModule++;
                }

                if (updatesThisAltModule > 0)
                {
                    mainModuleDocs[m.FileName] = updatedMainDoc;
                    mainModuleDirty.Add(m.FileName);

                    context.Logger.Info(
                        context.IsDryRun
                            ? string.Create(CultureInfo.InvariantCulture, $"{m.LogTag}: Would update main module in-memory (updates={updatesThisAltModule}) from alt '{altSv}'."
)
                            : string.Create(CultureInfo.InvariantCulture, $"{m.LogTag}: Updated main module in-memory (updates={updatesThisAltModule}) from alt '{altSv}'."));
                }
            }
        }

        // Write module files once
        var moduleWrites = 0;
        foreach (var m in modules)
        {
            if (!mainModuleDirty.Contains(m.FileName))
            {
                continue;
            }

            var mainModulePath = Path.Combine(this.mainSavedVariablesPath, m.FileName);

            var output = LuaWriter.WriteDocument(mainModuleDocs[m.FileName]);
            var original = mainModuleOriginalText[m.FileName];

            if (string.Equals(output, original, StringComparison.Ordinal))
            {
                context.Logger.Info($"{m.LogTag}: No effective changes, skipping write.");
                continue;
            }

            context.Logger.Info(
                context.IsDryRun
                    ? $"{m.LogTag}: Would write module file (final). '{mainModulePath}'"
                    : $"{m.LogTag}: Writing module file (final). '{mainModulePath}'");

            await context.FileSystem.WriteAllTextAtomicAsync(mainModulePath, output, ct).ConfigureAwait(false);
            moduleWrites++;
        }

        // Write identity once
        if (identityAdds > 0)
        {
            var dsOut = LuaWriter.WriteDocument(mainDsDoc);

            if (string.Equals(dsOut, mainDsOriginalText, StringComparison.Ordinal))
            {
                context.Logger.Info("DataStore: No effective identity changes, skipping write.");
            }
            else
            {
                context.Logger.Info(
                    context.IsDryRun
                        ? string.Create(CultureInfo.InvariantCulture, $"DataStore: Would write identity file (adds={identityAdds}). '{this.mainDataStoreLuaPath}'"
)
                        : string.Create(CultureInfo.InvariantCulture, $"DataStore: Writing identity file (adds={identityAdds}). '{this.mainDataStoreLuaPath}'"));

                await context.FileSystem.WriteAllTextAtomicAsync(this.mainDataStoreLuaPath, dsOut, ct).ConfigureAwait(false);
            }
        }
        else
        {
            context.Logger.Info("DataStore: No identity additions needed.");
        }

        context.Logger.Info(
            context.IsDryRun
                ? string.Create(CultureInfo.InvariantCulture, $"DataStore: Dry-run summary. Identity adds={identityAdds}, Module files to write={moduleWrites}, Adds={moduleAdds}, Replaces={moduleReplaces}."
)
                : string.Create(CultureInfo.InvariantCulture, $"DataStore: Merge complete. Identity adds={identityAdds}, Module files written={moduleWrites}, Adds={moduleAdds}, Replaces={moduleReplaces}."));
    }
}
