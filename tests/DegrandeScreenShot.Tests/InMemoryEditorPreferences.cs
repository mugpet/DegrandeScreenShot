using DegrandeScreenShot.App.Services;

namespace DegrandeScreenShot.Tests;

internal sealed class InMemoryEditorPreferences(EditorPreferences? initial = null) : IEditorPreferencesStore
{
    private EditorPreferences _preferences = initial ?? EditorPreferences.Default;

    internal int SaveCount { get; private set; }

    public EditorPreferences Load() => _preferences;

    public void Save(EditorPreferences preferences)
    {
        SaveCount++;
        _preferences = preferences;
    }
}
