using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Vision;

/// <summary>--build-gauge's input from any screenshot of the game: the game area (letterbox bars removed) and the skill bar's fit
/// found as 스킬바 맞추기 finds them, then the shot resampled to the reference screen, 1920×1080 at HUD 110%, where gauge masks,
/// emblems and every other reference rect live (<see cref="Geometry"/>).</summary>
public static class ReferenceShot
{
    public const int Width = 1920, Height = 1080;

    /// <summary>The game area and the skill bar's fit in a screenshot of the game (its window or the whole screen), or null when
    /// no skill bar is found (skills on cooldown hide nothing, but a hidden HUD, a cutscene or a covered bar do).</summary>
    public static (Viewport Vp, Fit Fit)? Find(Image shot)
    {
        var client = new PixelRect(0, 0, shot.Width, shot.Height);
        var vp = Letterbox.Viewport(client, BuffReader.Crop(shot, Letterbox.Probe(client)));
        var band = FitSolver.Band(vp);
        return FitSolver.Solve(BuffReader.Crop(shot, band), band, vp) is { } fit ? (vp, fit) : null;
    }

    /// <summary>The shot as it would look at 1920×1080, HUD 110%: each reference pixel sampled (bilinear) where
    /// <see cref="Geometry.ToScreen"/> puts it. Reference pixels outside the shot stay black.</summary>
    public static Image ToReference(Image shot, Viewport vp, Fit fit)
    {
        double k = Geometry.Scale(vp, fit), cx = vp.X + vp.Width / 2.0, bottom = vp.Y + vp.Height;
        var img = new Image(Width, Height);
        for (int y = 0; y < Height; y++)
        {
            double sy = bottom + (y + 0.5 - Geometry.ReferenceHeight + fit.NudgeY) * k - 0.5;
            for (int x = 0; x < Width; x++)
            {
                double sx = cx + (x + 0.5 - Width / 2.0 + fit.NudgeX) * k - 0.5;
                Sample(shot, sx, sy, img.Bgra.AsSpan(y * img.Stride + x * 4, 4));
            }
        }
        return img;
    }

    static void Sample(Image src, double sx, double sy, Span<byte> to)
    {
        if (sx < 0 || sy < 0 || sx > src.Width - 1 || sy > src.Height - 1) return;
        int x0 = (int)sx, y0 = (int)sy, x1 = Math.Min(x0 + 1, src.Width - 1), y1 = Math.Min(y0 + 1, src.Height - 1);
        double fx = sx - x0, fy = sy - y0;
        for (int c = 0; c < 3; c++)
        {
            double top = src.Bgra[y0 * src.Stride + x0 * 4 + c] * (1 - fx) + src.Bgra[y0 * src.Stride + x1 * 4 + c] * fx;
            double low = src.Bgra[y1 * src.Stride + x0 * 4 + c] * (1 - fx) + src.Bgra[y1 * src.Stride + x1 * 4 + c] * fx;
            to[c] = (byte)Math.Round(top * (1 - fy) + low * fy);
        }
        to[3] = 255;
    }

    /// <summary>The class emblem (<see cref="Geometry.RefEmblem"/>) of a reference shot: Profiles/aden/*/emblem_empty.png and
    /// emblem_full.png, which tell another gauge class on screen.</summary>
    public static Image Emblem(Image reference) => BuffReader.Crop(reference, Geometry.RefEmblem);

    /// <summary>The empty and full shots on the reference screen, with a note of what was found for the user. Both use one fit
    /// when they're the same size (the same session: their gauge pixels then line up exactly), the empty shot's if it has a
    /// skill bar, else the full one's. Throws <see cref="InvalidDataException"/> when neither shows the bar, or only one does and
    /// they differ in size.</summary>
    public static (Image Empty, Image Full, string Note) Normalize(Image empty, Image full)
    {
        var (foundEmpty, foundFull) = (Find(empty), Find(full));
        bool sameSize = (empty.Width, empty.Height) == (full.Width, full.Height);
        if (foundEmpty is null && foundFull is null)
            throw new InvalidDataException("두 스크린샷 모두에서 스킬바를 찾지 못했습니다. 수련장에서 모든 스킬이 준비된 상태로, 화면 전체를 다시 찍어 주세요.");
        if ((foundEmpty is null || foundFull is null) && !sameSize)
            throw new InvalidDataException("한 장에서만 스킬바를 찾았는데 두 스크린샷의 크기가 달라, 같은 자리로 맞출 수 없습니다. 같은 설정으로 두 장을 다시 찍어 주세요.");

        var (vpE, fitE) = sameSize ? (foundEmpty ?? foundFull)!.Value : foundEmpty!.Value;
        var (vpF, fitF) = sameSize ? (vpE, fitE) : foundFull!.Value;
        string note = $"게임 화면 {vpE.Width}×{vpE.Height}, HUD {fitE.Hud:0}%" + (foundEmpty is null || foundFull is null ? " (스킬바가 보인 한 장의 위치를 두 장에 씀)"
            : sameSize && Math.Abs(foundEmpty.Value.Fit.Hud - foundFull.Value.Fit.Hud) > 1 ? $" (두 장의 HUD가 달라 보여({foundFull.Value.Fit.Hud:0}%) 빈 화면 기준으로 맞춤)" : "");
        return (ToReference(empty, vpE, fitE), ToReference(full, vpF, fitF), note);
    }
}
