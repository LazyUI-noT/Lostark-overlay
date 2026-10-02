using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using GaugeOverlay.App.Platform;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.App.Services;

/// <summary>Runs a <see cref="PartySession"/> (the logic, in Core) on a UDP socket and a 50 ms timer, one call at a time
/// under a lock, and raises its changes as events.
/// <para>Events are raised on background threads and may arrive out of order across threads: when handling one,
/// read <see cref="Status"/> / <see cref="Peers"/> (always the latest) rather than trusting the argument.</para></summary>
public sealed class PartyService : IDisposable
{
    static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);

    readonly Lock _lock = new();
    readonly Timer _timer;

    readonly DeviceIdentity _device;   // signs this PC's Hellos (참가 요청 승인 recognises it); owned by the caller

    UdpTransport? _udp;
    PartySession? _session;
    bool _starting;
    // 참가 요청 승인 and 신뢰한 기기, for a session hosted from here (SetAdmission).
    bool _approveJoins;
    IReadOnlyList<string> _trusted = [];
    // The last Join's token and name, kept in memory only, to join again by itself if the host is lost (RejoinFor).
    string? _rejoinToken;
    string _rejoinName = "";
    readonly List<AdmissionChanges> _admission = [];
    TaskCompletionSource<bool>? _joining;
    (Snapshot Mine, string Name, IReadOnlySet<SlotGroup> Send, bool Gauge)? _published;
    Look? _look;

    // Raised after the lock is released.
    bool _peersDirty, _statusDirty;
    string? _notice;
    volatile IReadOnlyList<Player> _peers = [];
    volatile string? _status, _token;

    /// <summary>Other members changed; the argument is <see cref="Peers"/> at raise time. Background thread.</summary>
    public event Action<IReadOnlyList<Player>>? PeersChanged;
    /// <summary><see cref="Status"/> changed; the argument is its value at raise time. Background thread.</summary>
    public event Action<string?>? StatusChanged;
    /// <summary>A one-off message for the user (refused, host lost, IP not found). Background thread.</summary>
    public event Action<string>? Notice;
    /// <summary>Host: joiners to ask about or forget, devices to trust or untrust, refused joins. Background thread.</summary>
    public event Action<AdmissionChanges>? Admission;

    /// <summary>Host only: the token to give to peers; null otherwise. Never logged or saved.</summary>
    public string? Token => _token;

    /// <summary>"호스트 중 (3/4)", "참가 중 (2/4)", "연결 중…", or null outside a session. Thread-safe, always the latest.</summary>
    public string? Status => _status;

    /// <summary>Other members as the overlay draws them. Thread-safe, always the latest.</summary>
    public IReadOnlyList<Player> Peers => _peers;

    public PartyService(DeviceIdentity device)
    {
        _device = device;
        _timer = new Timer(_ => Locked(() => _session?.Tick(Now)));
    }

    static TimeSpan Now => Clock.Now();

    // ── Public actions ───────────────────────────────────────────────────────────────

    /// <summary>Starts a new session with a fresh secret on UDP <paramref name="port"/>. The token carries
    /// <paramref name="publicIp"/>, or the address from <see cref="FetchPublicIp"/> when it is null or empty.
    /// False (with a <see cref="Notice"/>) while another Host/Join is starting.</summary>
    public async Task<bool> Host(int port, string name, string? publicIp = null)
    {
        if (!BeginStart()) return false;
        try
        {
            var ip = string.IsNullOrWhiteSpace(publicIp) ? await FetchPublicIp()
                : IPAddress.TryParse(publicIp.Trim(), out var typed) && typed.AddressFamily == AddressFamily.InterNetwork ? typed : null;
            if (ip is null) return Fail("공인 IP(IPv4)를 확인하지 못했습니다. 인터넷 연결을 확인해 주세요.");
            UdpTransport udp;
            try { udp = new UdpTransport(port); }
            catch (SocketException) { return Fail($"UDP 포트 {port}을(를) 열 수 없습니다. 다른 프로그램이 쓰고 있는지 확인해 주세요."); }

            Listen(udp);
            Locked(() =>
            {
                _rejoinToken = null;
                Start(udp, PartySession.Host(new IPEndPoint(ip, port), name, Now, udp.Send, Updater.Current, _approveJoins, _trusted));
            });
            return true;
        }
        finally { lock (_lock) _starting = false; }
    }

    /// <summary>Joins the session in <paramref name="token"/>. Completes true on Welcome; false when refused,
    /// the token is invalid, another Host/Join is starting, or the host doesn't answer within 3 s, or 30 s once it said
    /// it is asking (<see cref="Notice"/> says why). Losing the host later joins again by itself for up to 60 s.</summary>
    public Task<bool> Join(string token, string name)
    {
        if (!BeginStart()) return Task.FromResult(false);
        try
        {
            UdpTransport udp;
            try { udp = new UdpTransport(0); }
            catch (SocketException) { return Task.FromResult(Fail("UDP 소켓을 열 수 없습니다.")); }
            Listen(udp);
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            bool valid = false;
            Locked(() =>
            {
                if (PartySession.Join(token, name, Now, udp.Send, Updater.Current, _device) is not { } session) return;
                valid = true;
                (_rejoinToken, _rejoinName) = (token, name);
                Start(udp, session);
                _joining = done;
            });
            if (valid) return done.Task;
            udp.Dispose();
            return Task.FromResult(Fail("토큰이 올바르지 않습니다. 호스트가 준 토큰을 그대로 붙여 넣고, 호스트와 같은 버전인지 확인해 주세요."));
        }
        finally { lock (_lock) _starting = false; }
    }

    /// <summary>Ends the session (a host's session ends for everyone), with no rejoin. Safe to call when not in one.</summary>
    public void Leave() => Locked(() =>
    {
        _rejoinToken = null;
        _session?.Leave();
    });

    /// <summary>참가 요청 승인 and the trusted devices (fingerprints) for sessions hosted from here, the current one too.</summary>
    public void SetAdmission(bool approveJoins, IEnumerable<string> trusted) => Locked(() =>
    {
        (_approveJoins, _trusted) = (approveJoins, [.. trusted]);
        if (_session is { IsHost: true } s) s.SetAdmission(_approveJoins, _trusted);
    });

    /// <summary>Host: 허용 / 거절 for a joiner from <see cref="Admission"/>. False if it is no longer waiting.</summary>
    public bool Approve(ulong id) => Answer(s => s.Approve(id));
    public bool Decline(ulong id) => Answer(s => s.Decline(id));

    /// <summary>Host: 다시 허용 for a device kicked this session.</summary>
    public void Unblock(string device) => Locked(() => _session?.Unblock(device));

    /// <summary>Host: devices kicked this session, with their names; empty otherwise.</summary>
    public IReadOnlyList<(string Device, string Name)> Blocked { get { lock (_lock) return _session is { IsHost: true, Ended: false } s ? s.Blocked : []; } }

    bool Answer(Func<PartySession, bool> answer)
    {
        bool done = false;
        Locked(() => done = _session is { IsHost: true } s && answer(s));
        return done;
    }

    /// <summary>The local snapshot as the engine publishes it, and what of it to send: pass a set the caller won't
    /// change afterwards (it is read on the network threads). Sent at most 20 Hz, only on change.</summary>
    public void Publish(Snapshot mine, string name, IReadOnlySet<SlotGroup> send, bool sendGauge) => Locked(() =>
    {
        _published = (mine, name, send, sendGauge);
        _session?.Publish(mine, name, send, sendGauge, Now);
    });

    /// <summary>Your box colours, size and layout, for members who receive them: sent to the current session once
    /// (resent until acknowledged), and kept for the next one. Call when joining or hosting, on 업데이트, and when the
    /// character changes; not on every change.</summary>
    public void PublishLook(Look look) => Locked(() =>
    {
        _look = look with
        {
            Colors = new Dictionary<SlotGroup, string>(look.Colors),
            Layout = look.Layout is null ? null : new Dictionary<Part, Cell>(look.Layout),
        };
        _session?.PublishLook(_look, Now);
    });

    /// <summary>True while hosting a session.</summary>
    public bool IsHost { get { lock (_lock) return _session is { Ended: false, IsHost: true }; } }

    /// <summary>내보내기 (host only): ends <paramref name="name"/>'s membership. They may join again with the token.</summary>
    public bool Kick(string name)
    {
        bool kicked = false;
        Locked(() => kicked = _session?.Kick(name) == true);
        return kicked;
    }

    /// <summary>The host's public IPv4 address from a what's-my-IP endpoint (the request carries nothing but itself).
    /// Null on any failure; nothing else (UPnP, hole punching) is tried.</summary>
    public static async Task<IPAddress?> FetchPublicIp()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var text = await http.GetStringAsync("https://api.ipify.org");
            return IPAddress.TryParse(text.Trim(), out var ip) && ip.AddressFamily == AddressFamily.InterNetwork ? ip : null;
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException) { return null; }
    }

    public void Dispose()
    {
        Leave();
        _timer.Dispose();
    }

    // ── Session lifetime ─────────────────────────────────────────────────────────────

    /// <summary>Claims the single start slot (Host/Join), then leaves any current session.</summary>
    bool BeginStart()
    {
        bool busy;
        lock (_lock) (busy, _starting) = (_starting, true);
        if (busy) return Fail("이미 연결 중입니다.");
        Leave();
        return true;
    }

    void Start(UdpTransport udp, PartySession session)
    {
        _session?.Leave();   // never drop a live session without ending it
        Sync();
        (_udp, _session) = (udp, session);
        if (_published is { } p) session.Publish(p.Mine, p.Name, p.Send, p.Gauge, Now);
        if (_look is { } look) session.PublishLook(look, Now);
        _timer.Change(Tick, Tick);
    }

    /// <summary>Subscribes before the session exists: Join sends its Hello while creating the session (under the lock),
    /// so a fast Welcome waits on the lock here instead of arriving with no one listening.</summary>
    void Listen(UdpTransport udp) =>
        udp.Received += (data, from) => Locked(() => { if (_udp == udp) _session?.Receive(data, from, Now); });

    /// <summary>Under the lock, after every session call: collects its changes, and once it has ended closes the socket.</summary>
    void Sync()
    {
        if (_session is not { } s) return;
        var changes = s.TakeChanges();
        (_peersDirty, _statusDirty) = (_peersDirty | changes.Peers, _statusDirty | changes.Status);
        if (changes.Admission.Any) _admission.Add(changes.Admission);
        if (s.Welcomed) { _joining?.TrySetResult(true); _joining = null; }
        _token = s.Token;
        if (s.Ended && s.CanRejoin && Rejoin()) return;   // the "host lost" notice waits: shown only if the rejoin fails
        _notice = changes.Notice ?? _notice;
        if (!s.Ended) return;
        _rejoinToken = null;
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
        _udp?.Dispose();
        (_udp, _session) = (null, null);
        _joining?.TrySetResult(false);
        _joining = null;
    }

    /// <summary>The host went silent: joins again with the same token on the same socket ("다시 연결 중…").</summary>
    bool Rejoin()
    {
        if (_rejoinToken is not { } token || _udp is not { } udp
            || PartySession.Join(token, _rejoinName, Now, udp.Send, Updater.Current, _device, rejoin: true) is not { } again) return false;
        _session = again;
        if (_published is { } p) again.Publish(p.Mine, p.Name, p.Send, p.Gauge, Now);
        if (_look is { } look) again.PublishLook(look, Now);
        _peersDirty = _statusDirty = true;
        return true;
    }

    bool Fail(string notice)
    {
        lock (_lock) _notice = notice;
        Raise();
        return false;
    }

    // ── Events ───────────────────────────────────────────────────────────────────────

    void Locked(Action action)
    {
        try { lock (_lock) { action(); Sync(); } }
        catch (Exception e) when (e is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine($"[net] {e.GetType().Name}: {e.Message}"); }
        // A throwing handler (e.g. Dispatcher.BeginInvoke during shutdown) must not escape into the UDP loop or the timer thread.
        try { Raise(); }
        catch (Exception e) when (e is not OutOfMemoryException) { System.Diagnostics.Trace.WriteLine($"[net] event handler {e.GetType().Name}: {e.Message}"); }
    }

    /// <summary>Publishes <see cref="Peers"/>/<see cref="Status"/> under the lock, then raises outside it with the
    /// values current at raise time (a racing thread may have published newer ones: handlers read the properties).</summary>
    void Raise()
    {
        bool peersDirty, statusDirty;
        string? notice;
        AdmissionChanges[] admission;
        lock (_lock)
        {
            (peersDirty, statusDirty) = (_peersDirty, _statusDirty);
            if (peersDirty) _peers = _session?.Peers(Now) ?? [];
            if (statusDirty) _status = _session?.Status;
            (notice, _notice, _peersDirty, _statusDirty) = (_notice, null, false, false);
            admission = [.. _admission];
            _admission.Clear();
        }
        if (peersDirty) PeersChanged?.Invoke(_peers);
        if (statusDirty) StatusChanged?.Invoke(_status);
        if (notice is not null) Notice?.Invoke(notice);
        foreach (var a in admission) Admission?.Invoke(a);
    }
}
