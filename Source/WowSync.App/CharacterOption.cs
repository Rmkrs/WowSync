namespace WowSync.App;

public sealed record CharacterOption(string Realm, string Name)
{
    public string Display => $"{this.Realm} - {this.Name}";
}