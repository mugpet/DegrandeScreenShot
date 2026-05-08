using System.IO;
using System.Text.Json;

namespace DegrandeScreenShot.App.Services;

internal sealed class EditorPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _preferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DegrandeScreenShot",
        "editor-preferences.json");

    internal EditorPreferences Load()
    {
        try
        {
            if (!File.Exists(_preferencesPath))
            {
                return EditorPreferences.Default;
            }

            var json = File.ReadAllText(_preferencesPath);
            return JsonSerializer.Deserialize<EditorPreferences>(json, SerializerOptions) ?? EditorPreferences.Default;
        }
        catch
        {
            return EditorPreferences.Default;
        }
    }

    internal void Save(EditorPreferences preferences)
    {
        try
        {
            var directory = Path.GetDirectoryName(_preferencesPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(preferences, SerializerOptions);
            File.WriteAllText(_preferencesPath, json);
        }
        catch
        {
            // Ignore persistence errors and keep the editor usable.
        }
    }
}

internal sealed record EditorPreferences(
    string ThemePreference,
    string ShapeColor,
    string ArrowColor,
    string ArrowStyle,
    List<ArrowShapePresetPreference>? ArrowShapePresets,
    string? SelectedArrowPresetId,
    string TextColor,
    double TextFontSize,
    double TextBackgroundOpacity,
    double TextBackgroundStrength,
    string ObscureColor,
    string ObscureMode,
    double? ObscureColorStrength,
    double ObscureBlurLevel,
    double ObscurePixelationLevel)
{
    internal static EditorPreferences Default => new(
        ThemePreference: "System",
        ShapeColor: "#FF125B50",
        ArrowColor: "#FFF2A23A",
        ArrowStyle: "BrushStroke",
        ArrowShapePresets: [],
        SelectedArrowPresetId: null,
        TextColor: "#FF6C4B16",
        TextFontSize: 26,
        TextBackgroundOpacity: 0.80,
        TextBackgroundStrength: 0.30,
        ObscureColor: "#FFD94841",
        ObscureMode: "Blur",
        ObscureColorStrength: 0.10,
        ObscureBlurLevel: 0.00,
        ObscurePixelationLevel: 0.25);
}

internal sealed record ArrowShapePresetPreference(
    string Id,
    double TailScale,
    double BodyScale,
    double FrontScale,
    double HeadScale,
    double ShadowStrength,
    double BorderWidth,
    List<ArrowPresetPointPreference>? BendPoints);

internal sealed record ArrowPresetPointPreference(double U, double V);