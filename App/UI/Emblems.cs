using System.Diagnostics;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;

namespace GaugeOverlay.App.UI;

/// <summary>Classes and their emblems by class number (ClassInfo.Id), from Profiles/classes.json and
/// Profiles/emblems/&lt;클래스&gt;.png (made by tools/cut-emblems.ps1). Loaded once; a class without a readable file has
/// no emblem, and its number is drawn instead.</summary>
static class Emblems
{
    static readonly Dictionary<int, ClassInfo> Classes = LoadClasses(CharacterService.DefaultProfilesDir);
    static readonly Dictionary<int, ImageSource> ById = Load(CharacterService.DefaultProfilesDir);

    public static ImageSource? For(int classId) => ById.GetValueOrDefault(classId);

    /// <summary>The class numbered <paramref name="classId"/>, or null (0 = not set, or a number this version doesn't know).</summary>
    public static ClassInfo? Class(int classId) => Classes.GetValueOrDefault(classId);

    static Dictionary<int, ClassInfo> LoadClasses(string profilesDir)
    {
        try { return ProfileLoader.LoadClasses(profilesDir).ToDictionary(c => c.Id); }
        // InvalidDataException: an unknown adenSlots entry.
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException
            or System.Text.Json.JsonException or NullReferenceException)
        {
            Trace.WriteLine($"[ui] class list unreadable, no emblems: {e.Message}");
            return [];
        }
    }

    static Dictionary<int, ImageSource> Load(string profilesDir)
    {
        var emblems = new Dictionary<int, ImageSource>();
        foreach (var c in Classes.Values)
        {
            string path = Path.Combine(profilesDir, "emblems", c.Name + ".png");
            if (!File.Exists(path)) continue;
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;   // read now; the file isn't kept open
                bmp.EndInit();
                bmp.Freeze();
                emblems[c.Id] = bmp;
            }
            catch (Exception e) when (e is IOException or NotSupportedException or FileFormatException)
            {
                Trace.WriteLine($"[ui] emblem {path} unreadable: {e.Message}");
            }
        }
        return emblems;
    }
}
