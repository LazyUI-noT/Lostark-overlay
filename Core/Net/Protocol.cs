using System.Collections.Immutable;
using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Net;

/// <summary>Packet type byte (the first header byte, authenticated as AAD). 5 was Gauge (protocol 1).</summary>
public enum MessageType : byte { Hello = 1, Welcome, Refuse, State, Heartbeat = 6, Bye, Roster, Look, LookAck }

/// <summary>Party message bodies (plaintext inside <see cref="SessionCrypto"/> packets). Little-endian, compact.
/// State starts with the 8-byte id of the member it describes. The host relays a peer's State by re-encoding
/// what it decoded on its own clock, so every receiver subtracts only its own half round trip to the host.
/// Times cross PCs only as remaining ms and are rebuilt on the receiver's clock (PLAN §7 item 8).</summary>
public static class Protocol
{
    /// <summary>2: the 아덴 게이지 travels inside State (protocol 1 had a separate Gauge message). 3: a stack skill's
    /// use left travels in the phase byte (0x40), which a version 2 reader would reject as an unknown phase.
    /// 4: the class number follows the gauge. 5: per-member keys (the Hello carries the joiner's public key, the
    /// Welcome the host's, and the token pins the host's). 6: the Welcome adds a per-join cookie to the pair key, and
    /// the gold frame (0x20) and whether the slot is a stack skill (0x10) travel in the phase byte. 7: Colors and
    /// ColorsAck (a member's own box colours, for 파티원 설정 따르기). 8: they became Look and LookAck, which add the box
    /// size and free layout (파티원 크기 받기, 파티원 배치 받기). 9: the Hello ends with the joiner's app version (major,
    /// minor, patch), and the host refuses any other (<see cref="Membership.AppRefusal"/>).</summary>
    public const byte Version = 9;
    const int Estimated = 0x80, Stacked = 0x40, Gold = 0x20, StackSkill = 0x10, PhaseBits = 0x0F;
    const int MaxName = 16;

    // ── Hello / Welcome / Refuse / Roster / Heartbeat ───────────────────────────────────

    public static byte[] Hello(string name, byte[] publicKey, System.Version app) => Write(w =>
    {
        w.Write(Version); w.Write(Clip(name)); w.Write(publicKey);
        w.Write((byte)app.Major); w.Write((byte)app.Minor); w.Write((byte)Math.Max(app.Build, 0));
    });

    /// <summary>True once the version and name are read, so another version can still be refused by name;
    /// <paramref name="publicKey"/> and <paramref name="app"/> (the joiner's app version, 0.0.0 if missing) are whatever
    /// follows (the caller checks them).</summary>
    public static bool TryReadHello(byte[] body, out byte version, out string name, out byte[] publicKey, out System.Version app)
    {
        (byte v, string n, byte[] k, System.Version a) r = (0, "", [], new(0, 0, 0));
        bool ok = Read(body, b =>
        {
            (r.v, r.n, r.k) = (b.ReadByte(), Clip(b.ReadString()), b.ReadBytes(SessionCrypto.PublicKeySize));
            if (b.BaseStream.Length - b.BaseStream.Position >= 3) r.a = new(b.ReadByte(), b.ReadByte(), b.ReadByte());
        });
        (version, name, publicKey, app) = r;
        return ok;
    }

    /// <summary>Host → joiner: the host's public key and this join's cookie (<see cref="SessionCrypto.Pair"/>).</summary>
    public static byte[] Welcome(byte[] publicKey, byte[] cookie) => [.. publicKey, .. cookie];

    public static bool TryReadWelcome(byte[] body, out byte[] publicKey, out byte[] cookie)
    {
        bool ok = body.Length == SessionCrypto.PublicKeySize + SessionCrypto.CookieSize;
        (publicKey, cookie) = ok ? (body[..SessionCrypto.PublicKeySize], body[SessionCrypto.PublicKeySize..]) : ([], []);
        return ok;
    }

    public static byte[] Refuse(string reason) => Write(w => w.Write(reason));

    public static string ReadRefuse(byte[] body)
    {
        string reason = "";
        Read(body, b => reason = b.ReadString());
        return reason;
    }

    /// <summary>Host → peers: every member including the host, in join order.</summary>
    public static byte[] Roster(IReadOnlyList<(ulong Id, string Name)> members) => Write(w =>
    {
        w.Write((byte)members.Count);
        foreach (var (id, name) in members) { w.Write(id); w.Write(Clip(name)); }
    });

    public static bool TryReadRoster(byte[] body, out List<(ulong Id, string Name)> members)
    {
        var list = new List<(ulong, string)>();
        members = list;
        return Read(body, b =>
        {
            int n = b.ReadByte();
            for (int i = 0; i < n; i++) list.Add((b.ReadUInt64(), Clip(b.ReadString())));
        });
    }

    /// <summary>A heartbeat carries the sender's clock; the receiver echoes it back (reply) so the sender measures RTT.</summary>
    public static byte[] Heartbeat(TimeSpan stamp, bool reply) => Write(w => { w.Write(stamp.Ticks); w.Write(reply); });

    public static bool TryReadHeartbeat(byte[] body, out TimeSpan stamp, out bool reply)
    {
        (long t, bool r) v = default;
        bool ok = Read(body, b => v = (b.ReadInt64(), b.ReadBoolean()));
        (stamp, reply) = (TimeSpan.FromTicks(v.t), v.r);
        return ok;
    }

    // ── State ────────────────────────────────────────────────────────────────────────

    /// <summary>Encodes the slots of the groups in <paramref name="send"/> (and the 각성기 변화 flag with 각성기)
    /// as remaining/total ms at <paramref name="now"/>, plus the 아덴 게이지 when <paramref name="sendGauge"/>.
    /// Layout: <c>[8B origin][1B group mask][1B 아덴 slot mask][1B 변화][2B gauge, 0xFFFF = none][1B class][1B n]
    /// + n × [1B slot][1B phase | 0x80 estimated | 0x40 stacked | 0x20 gold][4B remaining ms][4B total ms][1B minutes]</c>. Other slots are not sent.</summary>
    public static byte[] State(ulong origin, Snapshot s, IReadOnlySet<SlotGroup> send, bool sendGauge, TimeSpan now) => Write(w =>
    {
        w.Write(origin);
        var groups = Enum.GetValues<SlotGroup>().Where(g => send.Contains(g) && s.Groups.Contains(g)).ToList();
        w.Write((byte)groups.Aggregate(0, (m, g) => m | 1 << (int)g));
        w.Write((byte)((s.AdenSlots.Contains(SlotId.아덴1) ? 1 : 0) | (s.AdenSlots.Contains(SlotId.아덴2) ? 2 : 0)));
        w.Write(s.AwakeningChanged && groups.Contains(SlotGroup.각성기));
        w.Write(sendGauge && s.Gauge is { } g ? (ushort)Math.Round(Math.Clamp(g, 0, 1) * 65534) : ushort.MaxValue);
        w.Write((byte)Math.Clamp(s.ClassId, 0, 255));
        var slots = s.Slots.Where(st => groups.Contains(SlotInfo.GroupOf(st.Slot, s.AdenSlots))).ToList();
        w.Write((byte)slots.Count);
        foreach (var st in slots)
        {
            w.Write((byte)st.Slot);
            // Unavailable goes as a 0-minute LongCooldown: version 8 peers drop a state with a phase they don't know.
            var phase = st.Phase == SlotPhase.Unavailable ? SlotPhase.LongCooldown : st.Phase;
            w.Write((byte)((int)phase |(st.Estimated ? Estimated : 0) | (st.Stacked ? Stacked : 0) | (st.Gold ? Gold : 0) | (st.StackSkill ? StackSkill : 0)));
            w.Write(Ms(st.Remaining(now)));
            w.Write(Ms(st.Phase == SlotPhase.Cooldown ? st.Total : null));
            w.Write((byte)Math.Clamp(st.Minutes, 0, 255));
        }
    });

    /// <summary>Rebuilds a State on the receiver's clock: <c>EndsAt = now + remaining − rttHalf</c>.
    /// Slots that were not sent are Unknown and their groups are absent from <see cref="Snapshot.Groups"/>.</summary>
    public static bool TryReadState(byte[] body, TimeSpan now, TimeSpan rttHalf, out ulong origin, out Snapshot snapshot)
    {
        ulong o = 0;
        Snapshot? snap = null;
        bool ok = Read(body, b =>
        {
            o = b.ReadUInt64();
            int groupMask = b.ReadByte(), adenMask = b.ReadByte();
            bool changed = b.ReadBoolean();
            ushort gauge = b.ReadUInt16();
            int classId = b.ReadByte();
            List<SlotId> aden = [];
            if ((adenMask & 1) != 0) aden.Add(SlotId.아덴1);
            if ((adenMask & 2) != 0) aden.Add(SlotId.아덴2);
            var slots = SlotInfo.All.Select(SlotState.Unknown).ToArray();
            int n = b.ReadByte();
            for (int i = 0; i < n; i++)
            {
                int id = b.ReadByte(), ph = b.ReadByte();
                uint rem = b.ReadUInt32(), total = b.ReadUInt32();
                int minutes = b.ReadByte();
                var phase = (SlotPhase)(ph & PhaseBits);
                if (id >= slots.Length || !Enum.IsDefined(phase)) throw new EndOfStreamException();
                bool cd = phase == SlotPhase.Cooldown;
                slots[id] = new SlotState((SlotId)id, phase,
                    cd ? now + TimeSpan.FromMilliseconds(rem) - rttHalf : null,
                    cd ? TimeSpan.FromMilliseconds(total) : null,
                    (ph & Estimated) != 0, now, minutes, Stacked: cd && (ph & Stacked) != 0, Gold: (ph & Gold) != 0, StackSkill: (ph & StackSkill) != 0);
            }
            var groups = Enum.GetValues<SlotGroup>().Where(g => (groupMask & 1 << (int)g) != 0).ToImmutableHashSet();
            snap = new Snapshot(now, [.. slots], changed, gauge == ushort.MaxValue ? null : gauge / 65534.0, groups, [.. aden], classId);
        });
        (origin, snapshot) = (o, snap ?? Snapshot.Empty(now));
        return ok;
    }

    // ── Look / LookAck ───────────────────────────────────────────────────────────────

    const double ScaleUnit = 1000, CellUnit = 4;   // box size in thousandths, cells in quarter DIPs

    /// <summary>Member <paramref name="origin"/>'s own boxes: colours, box size and free layout. Layout: <c>[8B origin]
    /// [1B version][1B n] + n × [1B group][R][G][B], [2B width][2B height] (thousandths), [1B m] + m × [1B part][2B x][2B y]
    /// [2B w][2B h] (signed quarter DIPs)</c>. A colour that isn't "#RRGGBB" is left out; m = 0 is the fixed grid.</summary>
    public static byte[] Look(ulong origin, byte version, Look look) => Write(w =>
    {
        var colours = look.Colors.Where(c => Enum.IsDefined(c.Key) && TryRgb(c.Value, out _)).ToList();
        w.Write(origin);
        w.Write(version);
        w.Write((byte)colours.Count);
        foreach (var (g, hex) in colours)
        {
            TryRgb(hex, out var rgb);
            w.Write((byte)g);
            w.Write(rgb);
        }
        w.Write(Scale(look.Width));
        w.Write(Scale(look.Height));
        var cells = (look.Layout ?? new Dictionary<Part, Cell>()).Where(c => Enum.IsDefined(c.Key)).ToList();
        w.Write((byte)cells.Count);
        foreach (var (part, c) in cells)
        {
            w.Write((byte)part);
            foreach (double v in new[] { c.X, c.Y }) w.Write(Quarter(v));
            foreach (double v in new[] { c.W, c.H }) w.Write(Math.Max((short)1, Quarter(v)));
        }
    });

    /// <summary>A received cell's farthest spot or largest size, DIP at size 1.0: well past any layout made by dragging,
    /// short of one that would stretch a member's window across the screen.</summary>
    public const double MaxCell = 1000;

    /// <summary>False for a wrong length, an unknown or repeated group or part, a cell past <see cref="MaxCell"/>, a size outside
    /// <see cref="ClassProfiles.MinScale"/>–<see cref="ClassProfiles.MaxScale"/> or a cell without width or height.</summary>
    public static bool TryReadLook(byte[] body, out ulong origin, out byte version, out Look look)
    {
        (ulong o, byte v) head = default;
        Look? read = null;
        bool ok = Read(body, b =>
        {
            head = (b.ReadUInt64(), b.ReadByte());
            int n = b.ReadByte();
            var colours = new Dictionary<SlotGroup, string>();
            for (int i = 0; i < n; i++)
            {
                var g = (SlotGroup)b.ReadByte();
                var rgb = b.ReadBytes(3);
                if (rgb.Length < 3 || !Enum.IsDefined(g) || !colours.TryAdd(g, $"#{rgb[0]:X2}{rgb[1]:X2}{rgb[2]:X2}")) throw new FormatException();
            }
            double width = b.ReadUInt16() / ScaleUnit, height = b.ReadUInt16() / ScaleUnit;
            if (width is < ClassProfiles.MinScale or > ClassProfiles.MaxScale || height is < ClassProfiles.MinScale or > ClassProfiles.MaxScale)
                throw new FormatException();
            int m = b.ReadByte();
            if (body.Length != 10 + n * 4 + 5 + m * 9) throw new FormatException();
            var cells = new Dictionary<Part, Cell>();
            for (int i = 0; i < m; i++)
            {
                var part = (Part)b.ReadByte();
                var c = new Cell(b.ReadInt16() / CellUnit, b.ReadInt16() / CellUnit, b.ReadInt16() / CellUnit, b.ReadInt16() / CellUnit);
                if (!Enum.IsDefined(part) || c.W <= 0 || c.H <= 0 || !cells.TryAdd(part, c)
                    || Math.Abs(c.X) > MaxCell || Math.Abs(c.Y) > MaxCell || c.W > MaxCell || c.H > MaxCell) throw new FormatException();
            }
            read = new Look(colours, width, height, m == 0 ? null : cells);
        });
        (origin, version, look) = ok ? (head.o, head.v, read!) : (0, (byte)0, new Look(new Dictionary<SlotGroup, string>()));
        return ok;
    }

    /// <summary>Receipt of <paramref name="origin"/>'s look at <paramref name="version"/>; the sender stops resending it.</summary>
    public static byte[] LookAck(ulong origin, byte version) => Write(w => { w.Write(origin); w.Write(version); });

    public static bool TryReadLookAck(byte[] body, out ulong origin, out byte version)
    {
        (ulong o, byte v) r = default;
        bool ok = body.Length == 9 && Read(body, b => r = (b.ReadUInt64(), b.ReadByte()));
        (origin, version) = r;
        return ok;
    }

    static ushort Scale(double v) =>
        (ushort)Math.Round(Math.Clamp(double.IsFinite(v) ? v : 1, ClassProfiles.MinScale, ClassProfiles.MaxScale) * ScaleUnit);

    static short Quarter(double v) => (short)Math.Clamp(double.IsFinite(v) ? Math.Round(v * CellUnit) : 0, short.MinValue, short.MaxValue);

    static bool TryRgb(string hex, out byte[] rgb)
    {
        rgb = [];
        if (hex is not ['#', _, _, _, _, _, _] || !uint.TryParse(hex.AsSpan(1), System.Globalization.NumberStyles.HexNumber, null, out uint v))
            return false;
        rgb = [(byte)(v >> 16), (byte)(v >> 8), (byte)v];
        return true;
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────

    static uint Ms(TimeSpan? t) => t is { } v ? (uint)Math.Clamp(Math.Round(v.TotalMilliseconds), 0, uint.MaxValue) : 0;

    static string Clip(string s) => s.Length > MaxName ? s[..MaxName] : s;

    static byte[] Write(Action<BinaryWriter> write)
    {
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms)) write(w);
        return ms.ToArray();
    }

    /// <summary>False when the body is short or malformed (it came from an authenticated member, but still).</summary>
    static bool Read(byte[] body, Action<BinaryReader> read)
    {
        try
        {
            using var r = new BinaryReader(new MemoryStream(body));
            read(r);
            return true;
        }
        catch (Exception e) when (e is EndOfStreamException or IOException or ArgumentException or FormatException) { return false; }
    }
}
