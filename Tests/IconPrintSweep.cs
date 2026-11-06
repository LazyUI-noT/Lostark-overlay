using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;
using Xunit.Abstractions;

namespace GaugeOverlay.Tests;

/// <summary>Dev check, not run by default: each saved skill-bar icon (<c>GAUGE_ICON_SAVED</c>, folders of IconStore .bin files,
/// ';'-separated) against a reference library (<c>GAUGE_ICON_LIB</c>, .bin files searched recursively): the top three matches
/// and the gap to the second, to measure <see cref="IconPrint.MinScore"/> and <see cref="IconPrint.MinGap"/>.</summary>
public class IconPrintSweep(ITestOutputHelper output)
{
    [Fact]
    public void Saved_icons_against_a_library_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_ICON_SAVED") is not { Length: > 0 } saved
            || Environment.GetEnvironmentVariable("GAUGE_ICON_LIB") is not { Length: > 0 } lib) return;
        var library = Directory.GetFiles(lib, "*.bin", SearchOption.AllDirectories)
            .SelectMany(f => IconPrint.References(Load(f)).Select(p => (Id: Path.GetRelativePath(lib, f), Print: p))).ToList();
        foreach (var dir in saved.Split(';'))
            foreach (var file in Directory.GetFiles(dir, "*.bin").Order())
            {
                var framings = IconPrint.Framings(Load(file));
                var top = library.GroupBy(l => l.Id).Select(g => (Id: g.Key, Score: g.Max(l => framings.Max(f => f.Similarity(l.Print))))).OrderByDescending(t => t.Score).Take(3).ToList();
                output.WriteLine($"{Path.GetFileName(dir)}/{Path.GetFileNameWithoutExtension(file)}: "
                    + string.Join("  ", top.Select(t => $"{t.Id} {t.Score:0.000}")) + $"  gap {top[0].Score - top[1].Score:0.000}"
                    + $"  -> {IconPrint.Best(framings, library) ?? "unknown"}");
            }
    }

    /// <summary>Dev: each saved set (<c>GAUGE_ICON_SAVED</c>, folders named "클래스_직업각인") against the shipped library
    /// (Profiles/skills), per category: the skill identified in its own class, the best other class's score, and the class vote.</summary>
    [Fact]
    public void Saved_icons_against_the_shipped_library_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_ICON_SAVED") is not { Length: > 0 } saved
            || Environment.GetEnvironmentVariable("GAUGE_ICON_VOTE") != "1") return;
        var lib = Core.Data.SkillLibrary.Load(Path.Combine(Helpers.RepoRoot(), "Profiles", "skills"));
        double worstOther = 0;
        foreach (var dir in saved.Split(';'))
        {
            string cls = Path.GetFileName(dir).Split('_')[0];
            var slots = new List<(SlotGroup, IReadOnlyList<IconPrint>)>();
            foreach (var file in Directory.GetFiles(dir, "*.bin").Order())
            {
                string slot = Path.GetFileNameWithoutExtension(file);
                SlotGroup? kind = slot.StartsWith("일반") ? SlotGroup.일반스킬 : slot == "초각스" ? SlotGroup.초각스 : slot == "각성기" ? SlotGroup.각성기 : null;
                if (kind is not { } k) continue;
                var framings = IconPrint.Framings(Load(file));
                slots.Add((k, framings));
                var own = SkillMatch.Identify(framings, lib.Pool(cls, k));
                var other = lib.Classes.Where(c => c != cls).Select(c => (c, SkillMatch.Identify(framings, lib.Pool(c, k)).Score)).MaxBy(t => t.Score);
                worstOther = Math.Max(worstOther, other.Score);
                output.WriteLine($"{Path.GetFileName(dir)}/{slot}: {own.Skill?.Name ?? "unknown"} {own.Score:0.000} (top {own.Top.FirstOrDefault()?.Name})"
                    + $"  best other class {other.c} {other.Score:0.000}");
            }
            var votes = SkillMatch.Votes(slots, lib);
            output.WriteLine($"  vote: {string.Join(", ", votes.OrderByDescending(v => v.Value).Select(v => $"{v.Key} {v.Value}"))} -> {SkillMatch.ClassVote(slots, lib) ?? "none"}");
        }
        output.WriteLine($"highest score in another class: {worstOther:0.000}");
    }

    /// <summary>Dev (class-detect.plan.md): GAUGE_CLASS_FIND=1 over the saved sets (GAUGE_ICON_SAVED): the 초각스 by the last class and
    /// by a full search, in colour and greyed; another slot passed as a 초각스 must find nothing; the 일반 slots' verdict; then how
    /// near each class's 초각스 come to another class's, and the real greyed fixtures.</summary>
    [Fact]
    public void Class_find_when_requested()
    {
        if (Environment.GetEnvironmentVariable("GAUGE_CLASS_FIND") != "1") return;
        var lib = Core.Data.SkillLibrary.Load(Path.Combine(Helpers.RepoRoot(), "Profiles", "skills"));
        string F(SkillClassFinder.Found f) => $"{f.Class ?? "?"}/{f.Subclass ?? "-"} {(f.Fast ? "fast" : "full")} {f.Score:0.000} lead {f.Lead:0.000}";
        foreach (var dir in (Environment.GetEnvironmentVariable("GAUGE_ICON_SAVED") ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            string cls = Path.GetFileName(dir).Split('_')[0], wrong = cls == "소서리스" ? "바드" : "소서리스";
            var hyper = Load(Path.Combine(dir, "초각스.bin"));
            var colour = IconPrint.Framings(hyper);
            var grey = IconPrint.Framings(Grey(hyper));
            output.WriteLine($"{Path.GetFileName(dir)}: last {F(SkillClassFinder.Find(colour, lib, cls))} | none {F(SkillClassFinder.Find(colour, lib))} | wrong last {F(SkillClassFinder.Find(colour, lib, wrong))}");
            output.WriteLine($"  greyed: last {F(SkillClassFinder.Find(grey, lib, cls, greyed: true))} | none {F(SkillClassFinder.Find(grey, lib, greyed: true))}");
            var ordinary = Directory.GetFiles(dir, "일반*.bin").Order().Select(f => IconPrint.Framings(Load(f))).ToList();
            output.WriteLine($"  confirm own {SkillClassFinder.Confirm(ordinary, lib, cls)}, wrong {SkillClassFinder.Confirm(ordinary, lib, wrong)}");
            foreach (var other in Directory.GetFiles(dir, "*.bin").Where(f => !f.EndsWith("초각스.bin")).Order())
            {
                var asHyper = SkillClassFinder.Find(IconPrint.Framings(Load(other)), lib);
                var asGrey = SkillClassFinder.Find(IconPrint.Framings(Grey(Load(other))), lib, greyed: true);
                output.WriteLine($"  as 초각스 {Path.GetFileNameWithoutExtension(other)}: {F(asHyper)} | greyed {F(asGrey)}");
            }
        }
        double worst = 0, worstGrey = 0;
        var all = lib.Classes.SelectMany(c => lib.Pool(c, SlotGroup.초각스)).ToList();
        foreach (var a in all)
            foreach (var b in all.Where(b => b.Class != a.Class && b.Icon != a.Icon))
                foreach (var pa in a.Prints)
                    foreach (var pb in b.Prints)
                    {
                        if (pa.Similarity(pb) is var s && s > worst) { worst = s; output.WriteLine($"colour {a.Class} {a.Name} ~ {b.Class} {b.Name}: {s:0.000}"); }
                        if (pa.BrightnessSimilarity(pb) is var g && g > worstGrey) { worstGrey = g; output.WriteLine($"grey {a.Class} {a.Name} ~ {b.Class} {b.Name}: {g:0.000}"); }
                    }
        output.WriteLine($"nearest other class: colour {worst:0.000}, grey {worstGrey:0.000}");
        foreach (var name in new[] { "greyed_초각스", "Nocd_초각스", "cd_초각스", "down_22s_초각스", "lt2_11_초각스", "lt2_19_초각스", "lt2_31_초각스", "lt_3_초각스", "lt_6_초각스", "nomana_초각스", "silenced_초각스" })
        {
            var crop = Fixture.Load(name).Frames[0];
            output.WriteLine($"{name}: colour {F(SkillClassFinder.Find(IconPrint.Framings(crop), lib))} | greyed {F(SkillClassFinder.Find(IconPrint.Framings(Grey(crop)), lib, greyed: true))}");
        }
    }

    static Image Grey(Image img)
    {
        var grey = new Image(img.Width, img.Height);
        for (int i = 0; i + 3 < img.Bgra.Length; i += 4)
        {
            byte l = (byte)Math.Round(0.114 * img.Bgra[i] + 0.587 * img.Bgra[i + 1] + 0.299 * img.Bgra[i + 2]);
            (grey.Bgra[i], grey.Bgra[i + 1], grey.Bgra[i + 2], grey.Bgra[i + 3]) = (l, l, l, 255);
        }
        return grey;
    }

    static Image Load(string file) => Image.FromBytes(File.ReadAllBytes(file)) ?? throw new InvalidDataException(file);
}
