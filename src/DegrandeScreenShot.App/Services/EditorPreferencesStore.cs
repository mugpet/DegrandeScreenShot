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
    double? ArrowTailScale,
    double? ArrowBodyScale,
    double? ArrowFrontScale,
    double? ArrowHeadScale,
    double? ArrowShadowStrength,
    double? ArrowBorderWidth,
    bool? ArrowHasStartHead,
    bool? ArrowHasEndHead,
    double? ArrowTailHeadScale,
    string TextColor,
    double TextFontSize,
    double TextBackgroundOpacity,
    double TextBackgroundStrength,
    double? HighlightStrength,
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
        ArrowTailScale: 1.00,
        ArrowBodyScale: 0.20,
        ArrowFrontScale: 0.30,
        ArrowHeadScale: 0.40,
        ArrowShadowStrength: 0.00,
        ArrowBorderWidth: 0.00,
        ArrowHasStartHead: false,
        ArrowHasEndHead: true,
        ArrowTailHeadScale: 1.0,
        TextColor: "#FF6C4B16",
        TextFontSize: 26,
        TextBackgroundOpacity: 0.80,
        TextBackgroundStrength: 0.30,
        HighlightStrength: 0.55,
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
    bool? HasStartHead,
    bool? HasEndHead,
    double? TailHeadScale,
    List<ArrowPresetPointPreference>? BendPoints);

internal sealed record ArrowPresetPointPreference(double U, double V);