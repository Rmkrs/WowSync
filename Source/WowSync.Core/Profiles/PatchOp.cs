// ReSharper disable NotAccessedPositionalProperty.Global
namespace WowSync.Core.Profiles;

using WowSync.Core.Lua;

public sealed record PatchOp(string SourcePath, string TargetPath, LuaValue Value);