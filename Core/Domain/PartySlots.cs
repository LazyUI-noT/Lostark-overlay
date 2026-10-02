using System.Globalization;

namespace GaugeOverlay.Core.Domain;

/// <summary>파티원 순서: which party member goes in which party window (slot 0–2, shown as <see cref="Numbers"/>).
/// Decided only from who is here, the pinned names and the support slot, so the same party always gets the same answer
/// (no swapping back and forth).</summary>
public static class PartySlots
{
    /// <summary>The first supporter by join order takes <paramref name="supportSlot"/> (-1 = none); then each pinned name
    /// that is here takes its slot; everyone else fills the free slots in join order.</summary>
    public static Player?[] Assign(IReadOnlyList<Player> joined, IReadOnlyList<string?> pins, int supportSlot,
        Func<Player, bool> isSupport, int slots = 3)
    {
        var placed = new Player?[slots];
        var left = joined.ToList();
        void Put(int slot, Player p) { placed[slot] = p; left.Remove(p); }

        if (supportSlot >= 0 && supportSlot < slots && left.FirstOrDefault(isSupport) is { } support) Put(supportSlot, support);
        for (int i = 0; i < Math.Min(slots, pins.Count); i++)
            if (placed[i] is null && pins[i] is { } name && left.FirstOrDefault(p => p.Name == name) is { } pinned) Put(i, pinned);
        for (int i = 0; i < slots && left.Count > 0; i++)
            if (placed[i] is null) Put(i, left[0]);
        return placed;
    }

    /// <summary>Pins that put the members now in slots <paramref name="a"/> and <paramref name="b"/> the other way round
    /// and keep everyone else where they are.</summary>
    public static List<string?> Swap(IReadOnlyList<string?> current, int a, int b)
    {
        var pins = current.ToList();
        (pins[a], pins[b]) = (pins[b], pins[a]);
        return pins;
    }

    /// <summary>The numbers drawn beside the members in slots 0–2: 1–4 without <paramref name="mine"/> (내 번호; outside
    /// 1–4 counts as 1), in order.</summary>
    public static int[] Numbers(int mine)
    {
        if (mine is < 1 or > 4) mine = 1;
        return [.. Enumerable.Range(1, 4).Where(n => n != mine)];
    }

    /// <summary>The number drawn beside the member in <paramref name="slot"/>.</summary>
    public static string Label(int slot, int mine) => Numbers(mine)[slot].ToString(CultureInfo.InvariantCulture);

    /// <summary>The slot (0–2) that 서포터 고정 puts a supporter in, or -1: off, you are the supporter, or the number is
    /// yours.</summary>
    public static int SupportSlot(PartySettings party, bool iAmSupport) =>
        party.SupportFixed && !iAmSupport ? Array.IndexOf(Numbers(party.MyNumber), party.SupportNumber) : -1;
}
