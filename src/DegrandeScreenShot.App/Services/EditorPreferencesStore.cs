using System.IO;
using System.Text.Json;

namespace DegrandeScreenShot.App.Services;

internal sealed class EditorPreferencesStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
    };

    private static readonly object SaveLock = new();
    private static EditorPreferences? _pendingPreferences;
    private static bool _writerRunning;

    private static readonly string PreferencesPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "DegrandeScreenShot",
        "editor-preferences.json");

    internal static event EventHandler? PreferencesChanged;

    internal EditorPreferences Load()
    {
        lock (SaveLock)
        {
            if (_pendingPreferences is { } pending)
            {
                return pending;
            }
        }

        try
        {
            if (!File.Exists(PreferencesPath))
            {
                return EditorPreferences.Default;
            }

            var json = File.ReadAllText(PreferencesPath);
            return JsonSerializer.Deserialize<EditorPreferences>(json, SerializerOptions) ?? EditorPreferences.Default;
        }
        catch
        {
            return EditorPreferences.Default;
        }
    }

    internal void Save(EditorPreferences preferences)
    {
        lock (SaveLock)
        {
            _pendingPreferences = preferences;
            if (_writerRunning)
            {
                return;
            }
            _writerRunning = true;
        }

        _ = App.TrackOutputAsync(WritePendingPreferencesAsync());
    }

    private static async Task WritePendingPreferencesAsync()
    {
        while (true)
        {
            // Coalesce slider changes from all editor windows into a single ordered writer.
            await Task.Delay(200).ConfigureAwait(false);
            EditorPreferences snapshot;
            lock (SaveLock)
            {
                snapshot = _pendingPreferences!;
            }

            bool saved = false;
            string temporaryPath = $"{PreferencesPath}.{Guid.NewGuid():N}.tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencesPath)!);
                File.WriteAllText(temporaryPath, JsonSerializer.Serialize(snapshot, SerializerOptions));
                File.Move(temporaryPath, PreferencesPath, overwrite: true);
                saved = true;
            }
            catch
            {
                try { File.Delete(temporaryPath); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }

            bool finished;
            lock (SaveLock)
            {
                finished = ReferenceEquals(snapshot, _pendingPreferences);
                if (finished)
                {
                    _pendingPreferences = null;
                    _writerRunning = false;
                }
            }
            if (saved)
            {
                try
                {
                    PreferencesChanged?.Invoke(null, EventArgs.Empty);
                }
                catch
                {
                    // A listener must not prevent subsequent preference updates.
                }
            }
            if (finished)
            {
                return;
            }
        }
    }
}

internal sealed record EditorPreferences(
    string ThemePreference,
    string ShapeColor,
    double? ShapeBackgroundOpacity,
    double? FrameCornerRadius,
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
    double? TextCornerRadius,
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
        ShapeBackgroundOpacity: EditorWindow.DefaultShapeBackgroundOpacity,
        FrameCornerRadius: EditorWindow.DefaultFrameCornerRadius,
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
        TextCornerRadius: EditorWindow.DefaultTextCornerRadius,
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
