using System.IO;
using System.Text;
using System.Windows;
using GaugeOverlay.App.Platform;
using GaugeOverlay.App.Services;
using GaugeOverlay.Core.Data;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Vision;

namespace GaugeOverlay.App;

/// <summary>Class intake tools (CLASS_ADDING.md, .claude/skills/add-class): <c>--check-submission</c> and <c>--trace-gauge</c>.
/// Both write a report file and, with <c>--no-ui</c>, print it instead of showing a message box, so a script can run them.</summary>
public partial class App
{
    static readonly byte[] PngMagic = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>A message box, or with --no-ui the console (redirect it to read it).</summary>
    static void Report(string[] args, string title, string text)
    {
        if (args.Contains("--no-ui")) Console.Out.Write(text);
        else MessageBox.Show(text, title);
    }

    static string? Option(string[] args, string name) => args.FirstOrDefault(a => a.StartsWith($"--{name}="))?[(name.Length + 3)..];

    /// <summary>The arguments after <paramref name="flag"/> that aren't options.</summary>
    static string[] Operands(string[] args, string flag) => [.. args.SkipWhile(a => a != flag).Skip(1).Where(a => !a.StartsWith("--"))];

    /// <summary>--check-submission &lt;reference/classes/클래스&gt;: each 직업각인 folder in it against its _NEED.md
    /// (<see cref="Submission"/>), names against Profiles; the result in &lt;folder&gt;/_CHECK.md.</summary>
    static void CheckSubmission(string[] args)
    {
        string text;
        try
        {
            if (Operands(args, "--check-submission") is not [var dir] || !Directory.Exists(dir))
                throw new ArgumentException("사용법: --check-submission <reference/classes/클래스 폴더> [--profiles=<폴더>] [--no-ui]");
            text = SubmissionReport(dir, ProfileLoader.LoadClasses(Option(args, "profiles") ?? CharacterService.DefaultProfilesDir));
            File.WriteAllText(Path.Combine(dir, "_CHECK.md"), text);
        }
        catch (Exception ex)   // a dev tool run by a script: any failure is reported, never a blocking crash dialog
        {
            text = $"{ex.GetType().Name}: {ex.Message}";
        }
        Report(args, "GaugeOverlay --check-submission", text);
    }

    static string SubmissionReport(string dir, IReadOnlyList<ClassInfo> classes)
    {
        var folder = Path.GetFileName(Path.TrimEndingDirectorySeparator(dir));
        var className = Submission.Canonical(folder, classes.Select(c => c.Name));
        var cls = classes.FirstOrDefault(c => c.Name == className);
        var sb = new StringBuilder($"# 제출 확인: {folder}\n\n");
        int problems = 0, checkedFolders = 0;
        if (cls is null) { sb.AppendLine("✗ 클래스 이름이 classes.json에 없습니다.\n"); problems++; }
        foreach (var sub in Directory.GetDirectories(dir).Where(d => File.Exists(Path.Combine(d, "_NEED.md"))).Order())
        {
            checkedFolders++;
            var subFolder = Path.GetFileName(sub);
            var name = cls is null ? null : Submission.Canonical(subFolder, cls.Subclasses.Keys);
            sb.AppendLine($"## {subFolder}{(name is null ? "  ✗ 직업각인 이름이 classes.json에 없습니다" : name == subFolder ? "" : $"  (classes.json: {name})")}\n");
            if (name is null) problems++;
            var files = Directory.GetFiles(sub).Where(f => !Path.GetFileName(f).StartsWith('_')).Select(Load).ToList();
            var notes = Submission.Check(files, Submission.ParseNeed(File.ReadAllText(Path.Combine(sub, "_NEED.md"))));
            foreach (var n in notes) sb.AppendLine($"- {(n.Ok ? "✓" : "✗")} `{n.File}`: {n.Text}");
            var asks = notes.Where(n => n.FollowUp is not null).Select(n => n.FollowUp!).Distinct().ToList();
            if (asks.Count > 0) sb.AppendLine("\n보낼 말:\n" + string.Concat(asks.Select(a => $"- {a}\n")));
            problems += notes.Count(n => !n.Ok);
            sb.AppendLine();
        }
        if (checkedFolders == 0) { sb.AppendLine("✗ _NEED.md가 있는 직업각인 폴더가 없습니다. reference/classes/<클래스> 폴더를 주세요.\n"); problems++; }
        sb.AppendLine(problems == 0 ? "결과: 문제 없음" : $"결과: 문제 {problems}개");
        return sb.ToString();
    }

    /// <summary>A file of a submission: really a PNG (its first bytes), and its picture if it is a still that a decoder reads.</summary>
    static SubmissionFile Load(string path)
    {
        var head = new byte[PngMagic.Length];
        using (var fs = File.OpenRead(path)) _ = fs.Read(head, 0, head.Length);
        Image? img = null;
        if (Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg")
            try { img = Png.Load(path); }
            catch (Exception ex) when (ex is NotSupportedException or FileFormatException or IOException or ArgumentException) { }
        return new(Path.GetFileName(path), head.AsSpan().SequenceEqual(PngMagic), img);
    }

    /// <summary>--trace-gauge &lt;frames&gt; &lt;gauge folder&gt; [--fps=10]: PNG frames of a clip (ffmpeg -vf fps=10, as the engine
    /// reads every 100 ms), the skill bar fitted on the first frame that shows it, the gauge read on each through
    /// <see cref="GaugeTrace"/>; trace.csv and _TRACE.md in the frames folder.</summary>
    static void TraceGauge(string[] args)
    {
        string text;
        try
        {
            if (Operands(args, "--trace-gauge") is not [var framesDir, var gaugeDir])
                throw new ArgumentException("사용법: --trace-gauge <프레임 폴더> <게이지 폴더> [--fps=10] [--no-ui]");
            double fps = double.Parse(Option(args, "fps") ?? "10", System.Globalization.CultureInfo.InvariantCulture);
            var def = GaugeLoader.Parse(File.ReadAllText(Path.Combine(gaugeDir, GaugeLoader.JsonFile)), Png.Load(Path.Combine(gaugeDir, GaugeLoader.MaskFile)));
            var frames = Directory.GetFiles(framesDir, "*.png").Order().ToList();
            // ponytail: one fit for the whole clip (a recording doesn't change size); a cutscene in it reads as noise, so trim it out.
            var found = frames.Select(f => ReferenceShot.Find(Png.Load(f))).FirstOrDefault(f => f is not null)
                ?? throw new InvalidDataException("어느 프레임에서도 스킬창을 찾지 못했습니다.");
            var points = GaugeTrace.Run([.. frames.Select((f, i) => (TimeSpan.FromSeconds(i / fps), def.ReadOn(Png.Load(f), found.Vp, found.Fit)))]);
            File.WriteAllLines(Path.Combine(framesDir, "trace.csv"),
                ["frame,seconds,raw,shown", .. points.Select((p, i) => FormattableString.Invariant($"{i},{p.At.TotalSeconds:0.0},{p.Raw:0.000},{p.Shown:0.000}"))]);
            text = TraceReport(points, fps, found.Vp, found.Fit);
            File.WriteAllText(Path.Combine(framesDir, "_TRACE.md"), text);
        }
        catch (Exception ex)   // as in CheckSubmission
        {
            text = $"{ex.GetType().Name}: {ex.Message}";
        }
        Report(args, "GaugeOverlay --trace-gauge", text);
    }

    static string TraceReport(IReadOnlyList<TracePoint> points, double fps, Viewport vp, Fit fit)
    {
        var raw = GaugeTrace.Flags([.. points.Select(p => p.Raw)], GaugeTrace.Jump);
        var shown = GaugeTrace.Flags([.. points.Select(p => p.Shown)], GaugeTrace.Jump);
        string At(IReadOnlyList<int> f) => f.Count == 0 ? "없음"
            : string.Join(", ", f.Take(30).Select(i => FormattableString.Invariant($"{points[i].At.TotalSeconds:0.0}s"))) + (f.Count > 30 ? $" … ({f.Count}개)" : "");
        return $"# 게이지 효과 확인\n\n프레임 {points.Count}개 ({fps} fps), 게임 화면 {vp.Width}×{vp.Height}, HUD {fit.Hud:0}\n\n" +
            $"- 원본 읽기의 튐 ({raw.Count}개): {At(raw)}\n- 필터 뒤에도 남은 튐 ({shown.Count}개): {At(shown)}\n\n" +
            (shown.Count == 0 ? "결과: 효과가 있더라도 필터가 흡수합니다." : "결과: 필터가 흡수하지 못한 효과가 있습니다. 그 시점 프레임을 확인하세요.") + "\n";
    }
}
