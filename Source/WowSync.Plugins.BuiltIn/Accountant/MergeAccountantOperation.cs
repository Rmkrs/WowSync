// ReSharper disable IdentifierTypo
// ReSharper disable CommentTypo
// ReSharper disable StringLiteralTypo
namespace WowSync.Plugins.BuiltIn.Accountant;

using System.Globalization;
using WowSync.Core.Lua;
using WowSync.Plugins.Abstractions.Operations;

public sealed class MergeAccountantOperation : IOperation
{
    private const string LogTag = "Accountant";
    private const string FileName = "Accountant.lua";
    private const string SaveDataVar = "Accountant_SaveData";

    private readonly IReadOnlyList<string> altSavedVariablesPaths;
    private readonly string mainLuaPath;

    public MergeAccountantOperation(
        string mainAccountSavedVariablesPath,
        IReadOnlyList<string> altAccountSavedVariablesPaths)
    {
        var mainSavedVariablesPath = mainAccountSavedVariablesPath;
        this.altSavedVariablesPaths = altAccountSavedVariablesPaths;

        this.mainLuaPath = Path.Combine(mainSavedVariablesPath, FileName);

        this.Description = $"Merge Accountant (Alts -> Main) + Distribute (Main -> Alts): {altAccountSavedVariablesPaths.Count} alt(s)";
        this.TouchedPaths =
        [
            this.mainLuaPath,
            ..altAccountSavedVariablesPaths.Select(p => Path.Combine(p, FileName)),
        ];
    }

    public string Description { get; }
    public IReadOnlyList<string> TouchedPaths { get; }

    public async Task ExecuteAsync(IOperationContext context, CancellationToken ct)
    {
        if (!context.FileSystem.FileExists(this.mainLuaPath))
        {
            context.Logger.Err($"{LogTag}: Main file missing. '{this.mainLuaPath}'. Skipping operation.");
            return;
        }

        // ---- Parse main
        var mainOriginalText = await context.FileSystem.ReadAllTextAsync(this.mainLuaPath, ct).ConfigureAwait(false);

        LuaDocument mainDoc;
        try
        {
            mainDoc = new LuaParser(mainOriginalText).ParseDocument();
        }
        catch (Exception ex)
        {
            throw new FormatException($"{LogTag}: Failed to parse main file '{this.mainLuaPath}'. {ex.Message}", ex);
        }

        if (!AccountantMergeHelpers.TryGetSaveDataTable(mainDoc, out var mainSaveData))
        {
            context.Logger.Err($"{LogTag}: Missing or invalid '{SaveDataVar}' in main. Skipping operation.");
            return;
        }

        var mainToons = mainSaveData.Entries.Count(e => e.Key is LuaKey.StringKey);
        context.Logger.Info(string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Main has {mainToons} toon(s) in SaveData."));

        // Merge stats
        var mergeAdds = 0;
        var mergeReplaces = 0;

        // Deterministic alt processing order: altSavedVariablesPaths already ordered in plugin
        var altCache = new Dictionary<string, (string Path, string Text, LuaDocument Doc, LuaValue.LuaTable SaveData)>(StringComparer.Ordinal);

        foreach (var altSv in this.altSavedVariablesPaths)
        {
            var altLuaPath = Path.Combine(altSv, FileName);

            if (!context.FileSystem.FileExists(altLuaPath))
            {
                context.Logger.Warn($"{LogTag}: Alt file missing, skipping alt. '{altLuaPath}'");
                continue;
            }

            var altText = await context.FileSystem.ReadAllTextAsync(altLuaPath, ct).ConfigureAwait(false);

            LuaDocument altDoc;
            try
            {
                altDoc = new LuaParser(altText).ParseDocument();
            }
            catch (Exception ex)
            {
                throw new FormatException($"{LogTag}: Failed to parse alt file '{altLuaPath}'. {ex.Message}", ex);
            }

            if (!AccountantMergeHelpers.TryGetSaveDataTable(altDoc, out var altSaveData))
            {
                context.Logger.Warn($"{LogTag}: Alt missing/invalid '{SaveDataVar}', skipping alt. '{altLuaPath}'");
                continue;
            }

            var altToons = altSaveData.Entries.Count(e => e.Key is LuaKey.StringKey);
            context.Logger.Info(string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Alt '{altSv}' has {altToons} toon(s) in SaveData."));

            altCache[altSv] = (altLuaPath, altText, altDoc, altSaveData);

            // Phase A: merge alts -> main (superset)
            var (updatedDoc, adds, replaces) = AccountantMergeHelpers.MergeAltIntoMain(
                mainDoc: mainDoc,
                altSaveData: altSaveData,
                log: context.Logger,
                isDryRun: context.IsDryRun);

            mainDoc = updatedDoc;
            mergeAdds += adds;
            mergeReplaces += replaces;
        }

        // If mainDoc changed in-memory, write it (only if effective output differs)
        var mainOut = LuaWriter.WriteDocument(mainDoc);
        var mainChanged = !string.Equals(mainOut, mainOriginalText, StringComparison.Ordinal);

        if (mainChanged)
        {
            context.Logger.Info(
                context.IsDryRun
                    ? string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Would write merged main file. '{this.mainLuaPath}' (adds={mergeAdds}, replaces={mergeReplaces})")
                    : string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Writing merged main file. '{this.mainLuaPath}' (adds={mergeAdds}, replaces={mergeReplaces})"));

            await context.FileSystem.WriteAllTextAtomicAsync(this.mainLuaPath, mainOut, ct).ConfigureAwait(false);
        }
        else
        {
            context.Logger.Info(string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Main file unchanged after merge (adds={mergeAdds}, replaces={mergeReplaces})."));
        }

        // Phase B: distribute main -> alts (filtered to toons present on target)
        var distributeWrites = 0;

        foreach (var altSv in this.altSavedVariablesPaths)
        {
            if (!altCache.TryGetValue(altSv, out var alt))
            {
                // Missing/invalid alt file: already logged above
                continue;
            }

            var (altLuaPath, altOriginalText, altDoc, altSaveData) = alt;

            // Build a filtered target doc: only toons that exist on THIS alt (based on its current keys)
            var (updatedAltDoc, skippedMissingInMain, toonCount) = AccountantMergeHelpers.BuildFilteredTargetDocument(
                mergedMainDoc: mainDoc,
                targetDoc: altDoc,
                targetExistingSaveData: altSaveData,
                log: context.Logger,
                isDryRun: context.IsDryRun,
                targetTag: altSv);

            var altOut = LuaWriter.WriteDocument(updatedAltDoc);

            if (string.Equals(altOut, altOriginalText, StringComparison.Ordinal))
            {
                context.Logger.Info(string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Alt unchanged, skipping write. '{altLuaPath}' (toons={toonCount}, skippedMissingInMain={skippedMissingInMain})"));
                continue;
            }

            context.Logger.Info(
                context.IsDryRun
                    ? string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Would write alt file. '{altLuaPath}' (toons={toonCount}, skippedMissingInMain={skippedMissingInMain})"
)
                    : string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Writing alt file. '{altLuaPath}' (toons={toonCount}, skippedMissingInMain={skippedMissingInMain})"));

            await context.FileSystem.WriteAllTextAtomicAsync(altLuaPath, altOut, ct).ConfigureAwait(false);
            distributeWrites++;
        }

        context.Logger.Info(
            context.IsDryRun
                ? string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Dry-run summary. Merge: adds={mergeAdds}, replaces={mergeReplaces}. Distribute writes={distributeWrites}."
)
                : string.Create(CultureInfo.InvariantCulture, $"{LogTag}: Complete. Merge: adds={mergeAdds}, replaces={mergeReplaces}. Distribute writes={distributeWrites}."));
    }
}
