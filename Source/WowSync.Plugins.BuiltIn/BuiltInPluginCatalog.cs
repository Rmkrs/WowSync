namespace WowSync.Plugins.BuiltIn;

using WowSync.Plugins.Abstractions.Contracts;
using WowSync.Plugins.BuiltIn.Accountant;
using WowSync.Plugins.BuiltIn.Altoholic;

public static class BuiltInPluginCatalog
{
    public static IReadOnlyList<IOperationPlugin> CreateAll()
    {
        return [
                new DataStoreMergeOperationPlugin(),
            new AccountantMergeOperationPlugin(),
        ];
    }
}
