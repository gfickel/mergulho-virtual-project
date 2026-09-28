namespace MergulhoVirtual.UI
{
    /// <summary>
    /// Manual beach override (the "Automático (GPS)" dropdown behavior):
    /// setting a beach bypasses GPS resolution for spawning/conditions until cleared.
    /// </summary>
    public interface IBeachOverride
    {
        void SetOverride(string beachName);
        void ClearOverride();
    }
}
