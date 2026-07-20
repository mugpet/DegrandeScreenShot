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

    internal static event EventHandler? PreferencesChanged;

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
        var wasSaved = false;
        try
        {
            var directory = Path.GetDirectoryName(_preferencesPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(preferences, SerializerOptions);
            File.WriteAllText(_preferencesPath, json);
            wasSaved = true;
        }
        catch
        {
            // Ignore persistence errors and keep the editor usable.
        }

        if (wasSaved)
        {
            try
            {
                PreferencesChanged?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                // Preference listeners must never make editor settings unusable.
            }
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
    double? ShapeScale,
    double? ArrowScale,
    double? TextScale,
    double? HighlightScale,
    double? ObscureScale,
    double? ArrowTailScale,
    double? ArrowBodyScale,
    double? ArrowFrontScale,
    double? ArrowHeadScale,
    double? ArrowTailRoundness,
    double? ArrowHeadRoundness,
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
        ArrowColor: "#FFEC4899",
        ArrowStyle: "BrushStroke",
        ArrowShapePresets: [],
        SelectedArrowPresetId: null,
        ShapeScale: 1.0,
        ArrowScale: 1.0,
        TextScale: 1.0,
        HighlightScale: 1.0,
        ObscureScale: 1.0,
        ArrowTailScale: 1.00,
        ArrowBodyScale: 1.00,
        ArrowFrontScale: 1.00,
        ArrowHeadScale: 1.00,
        ArrowTailRoundness: 0.00,
        ArrowHeadRoundness: 0.00,
        ArrowShadowStrength: 0.00,
        ArrowBorderWidth: 2.00,
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
    double? TailRoundness,
    double? HeadRoundness,
    List<ArrowPresetPointPreference>? BendPoints,
    double? Scale = 1.0);

internal sealed record ArrowPresetPointPreference(double U, double V);
