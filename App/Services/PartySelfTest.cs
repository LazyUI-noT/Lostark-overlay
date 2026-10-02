using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using GaugeOverlay.Core.Domain;
using GaugeOverlay.Core.Net;

namespace GaugeOverlay.App.Services;

/// <summary><c>GaugeOverlay --party-test</c>: a host and 3 peers in-process on 127.0.0.1 with fake snapshots.
/// Writes %TEMP%\GaugeOverlay-party-test.txt and exits with 0 (all passed) or 1.</summary>
internal static class PartySelfTest
{
    const int Port = 47811;

    public static void Run(Application app) =>
        Task.Run(Check).ContinueWith(t => app.Dispatcher.Invoke(() =>
        {
            var (ok, text) = t.Exception is { } ex ? (false, "FAILED\n" + ex) : t.Result;
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "GaugeOverlay-party-test.txt"), text);
            app.Shutdown(ok ? 0 : 1);
        }));

    sealed class Node(string name)
    {
        public readonly string Name = name;
        public readonly Settings Settings = new();
        public PartyService Service = null!;
        public volatile IReadOnlyList<Player> Peers = [];
        public readonly ConcurrentQueue<string> Notices = new();
        public readonly ConcurrentQueue<AdmissionChanges> Admissions = new();
        public readonly DeviceIdentity Device = DeviceIdentity.Create();
        public string Fingerprint => DeviceIdentity.Fingerprint(Device.PublicKey);
        public TimeSpan EndsAt;   // of its fake 일반1 cooldown (one process, so one clock for every member)

        public void Attach()
        {
            Service = new PartyService(Device);
            Service.PeersChanged += p => Peers = p;
            Service.Notice += Notices.Enqueue;
            Service.Admission += Admissions.Enqueue;
        }

        public void Publish(int seconds)
        {
            var now = Clock.Now();
            EndsAt = now + TimeSpan.FromSeconds(seconds);
            var s = Snapshot.Empty(now);
            var slots = s.Slots.SetItem((int)SlotId.일반1,
                new SlotState(SlotId.일반1, SlotPhase.Cooldown, EndsAt, TimeSpan.FromSeconds(30), false, now));
            Service.Publish(s with { Slots = slots, Gauge = 0.5, AdenSlots = [SlotId.아덴1] }, Name, Settings.Party.SendGroups(), Settings.Party.SendGauge);
        }
    }

    static async Task<(bool, string)> Check()
    {
        var log = new StringBuilder();
        bool all = true;
        void Result(string what, bool ok, string detail = "")
        {
            all &= ok;
            log.AppendLine($"{(ok ? "PASS" : "FAIL")}  {what}{(detail.Length > 0 ? "  — " + detail : "")}");
        }

        var host = new Node("호스트");
        var peers = new[] { new Node("민수"), new Node("지현"), new Node("철수") };
        var everyone = peers.Prepend(host).ToArray();
        foreach (var n in everyone) n.Attach();
        peers[0].Settings.Party.Send[SlotGroup.일반스킬] = false;   // group filter: 민수 doesn't send 일반스킬
        peers[1].Settings.Party.SendGauge = false;                  // 지현 doesn't send the gauge

        bool hosted = await host.Service.Host(Port, host.Name, "127.0.0.1");
        var token = host.Service.Token;
        Result("host started", hosted && token is not null, host.Service.Status ?? "");
        if (token is null) return (false, log.ToString());

        // Each member's look (colours and box size) reaches the others once; an update (업데이트) replaces it.
        static Look LookOf(int i, int v) => new(new Dictionary<SlotGroup, string> { [SlotGroup.일반스킬] = $"#{i:X2}{v:X2}00" }, 1 + v / 10.0);
        for (int i = 0; i < everyone.Length; i++)
        {
            everyone[i].Publish(10 + i * 5);
            everyone[i].Service.PublishLook(LookOf(i, 1));
        }
        var joins = await Task.WhenAll(peers.Select(p => p.Service.Join(token, p.Name)));
        Result("3 peers joined", joins.All(j => j), string.Join(", ", peers.Select(p => p.Service.Status)));

        var sw = Stopwatch.StartNew();
        bool SeesAll(Node n) => n.Peers.Count == 3 && n.Peers.All(p => p.Snapshot[SlotId.일반1].Phase == SlotPhase.Cooldown
            || !p.Snapshot.Groups.Contains(SlotGroup.일반스킬));
        bool seen = await Wait(() => everyone.All(SeesAll), TimeSpan.FromSeconds(1));
        Result("every member sees the other three within 1 s", seen, $"{sw.ElapsedMilliseconds} ms; "
            + string.Join("; ", everyone.Select(n => $"{n.Name}: {string.Join(",", n.Peers.Select(p => p.Name))}")));

        // Timing: each receiver's EndsAt for a sender's cooldown matches the sender's within 50 ms.
        double worst = 0;
        foreach (var n in everyone)
            foreach (var p in n.Peers)
                if (p.Snapshot[SlotId.일반1].EndsAt is { } end)
                    worst = Math.Max(worst, Math.Abs((end - everyone.First(x => x.Name == p.Name).EndsAt).TotalMilliseconds));
        Result("remaining-time error across members", worst < 50, $"worst {worst:0} ms");

        bool filtered = everyone.Where(n => n != peers[0]).All(n => n.Peers.First(p => p.Name == "민수") is var m
            && !m.Snapshot.Groups.Contains(SlotGroup.일반스킬) && m.Snapshot[SlotId.일반1].Phase == SlotPhase.Unknown
            && m.Snapshot.Groups.Contains(SlotGroup.각성기) && m.Snapshot.Gauge is > 0.49 and < 0.51 && m.Snapshot.AdenSlots.Contains(SlotId.아덴1));
        Result("per-group send filter (민수 without 일반스킬) + gauge + 아덴 slots", filtered);

        bool gaugeInState = everyone.Where(n => n != peers[1]).All(n => n.Peers.First(p => p.Name == "지현").Snapshot.Gauge is null)
            && everyone.Where(n => n != host).All(n => n.Peers.First(p => p.Name == "호스트").Snapshot.Gauge is > 0.49 and < 0.51);
        Result("gauge travels inside State (지현 SendGauge off → none; host's relayed and direct)", gaugeInState);

        Look? LookSeen(Node viewer, Node about) => viewer.Peers.FirstOrDefault(p => p.Name == about.Name)?.Look;
        bool HasAll(int v) => everyone.All(n => everyone.Where(o => o != n).All(o =>
            LookSeen(n, o) is { } seen && (o == peers[0] ? LookOf(1, v) : LookOf(Array.IndexOf(everyone, o), 1)) is var sent
            && seen.Colors.GetValueOrDefault(SlotGroup.일반스킬) == sent.Colors[SlotGroup.일반스킬] && Math.Abs(seen.Width - sent.Width) < 0.01));
        bool coloured = await Wait(() => HasAll(1), TimeSpan.FromSeconds(1));
        Result("every member has the others' colours and box size", coloured);
        peers[0].Service.PublishLook(LookOf(1, 2));
        bool recoloured = await Wait(() => HasAll(2), TimeSpan.FromSeconds(2));
        Result("an update reaches everyone (at most one a second)", recoloured);

        var fifth = new Node("영희");
        fifth.Attach();
        bool fifthJoined = await fifth.Service.Join(token, fifth.Name);
        Result("5th join refused", !fifthJoined && fifth.Notices.Any(t => t.Contains("가득")), string.Join(" | ", fifth.Notices));

        peers[2].Service.Leave();
        bool updated = await Wait(() => new[] { host, peers[0], peers[1] }.All(n => n.Peers.Count == 2 && n.Peers.All(p => p.Name != "철수"))
            && host.Service.Status == "호스트 중 (3/4)" && peers[0].Service.Status == "참가 중 (3/4)", TimeSpan.FromSeconds(1));
        Result("leaving updates rosters", updated, $"{host.Service.Status}, {peers[0].Service.Status}");

        // 내보내기: the kicked member's session ends with the notice; the same token no longer lets that device in.
        bool kickedOk = host.Service.IsHost && !peers[0].Service.IsHost && host.Service.Kick("지현");
        bool kickedOut = kickedOk && await Wait(() => peers[1].Service.Status is null && host.Service.Status == "호스트 중 (2/4)"
            && host.Peers.All(p => p.Name != "지현"), TimeSpan.FromSeconds(1));
        Result("host kicks a member", kickedOut && peers[1].Notices.Any(t => t.Contains("내보냈")), string.Join(" | ", peers[1].Notices));
        // Join completes when the session ends; its notice is raised just after, on the network thread.
        bool refusedAgain = !await peers[1].Service.Join(token, peers[1].Name)
            && await Wait(() => peers[1].Notices.Any(t => t.Contains("내보낸")), TimeSpan.FromSeconds(1));
        bool listed = host.Service.Blocked.Any(b => b.Device == peers[1].Fingerprint);
        bool told = await Wait(() => host.Admissions.Any(a => a.Refused.Any(r => r.Contains("지현"))), TimeSpan.FromSeconds(1));
        Result("a kicked device is refused with the same token; the host is told", refusedAgain && listed && told,
            $"refused {refusedAgain}, listed {listed}, told {told}");
        host.Service.Unblock(peers[1].Fingerprint);
        bool rejoined = await peers[1].Service.Join(token, peers[1].Name)
            && await Wait(() => host.Service.Status == "호스트 중 (3/4)", TimeSpan.FromSeconds(1));
        Result("다시 허용 lets it back in", rejoined, host.Service.Status ?? "");

        // 참가 요청 승인: an untrusted device waits for the host; once allowed it is trusted and rejoins without asking.
        host.Service.SetAdmission(true, []);
        var asking = peers[2].Service.Join(token, peers[2].Name);
        bool asked = await Wait(() => host.Admissions.Any(a => a.Asked.Any(r => r.Name == "철수")), TimeSpan.FromSeconds(1))
            && peers[2].Service.Status == "승인 기다리는 중…";
        var request = host.Admissions.SelectMany(a => a.Asked).LastOrDefault(r => r.Name == "철수");
        bool approved = asked && request is not null && host.Service.Approve(request.Id) && await asking
            && host.Admissions.Any(a => a.Trusted.Any(t => t.Device == peers[2].Fingerprint));
        Result("approval: asked, allowed, trusted", approved, peers[2].Service.Status ?? "");
        peers[2].Service.Leave();
        await Wait(() => host.Service.Status == "호스트 중 (3/4)", TimeSpan.FromSeconds(1));
        host.Service.SetAdmission(true, [peers[2].Fingerprint]);
        int askedBefore = host.Admissions.Sum(a => a.Asked.Count);
        bool trustedBack = await peers[2].Service.Join(token, peers[2].Name) && host.Admissions.Sum(a => a.Asked.Count) == askedBefore;
        Result("a trusted device joins without asking", trustedBack, peers[2].Service.Status ?? "");
        peers[2].Service.Leave();
        var declining = fifth.Service.Join(token, fifth.Name);
        bool askedFifth = await Wait(() => host.Admissions.Any(a => a.Asked.Any(r => r.Name == "영희")), TimeSpan.FromSeconds(1));
        var fifthRequest = host.Admissions.SelectMany(a => a.Asked).LastOrDefault(r => r.Name == "영희");
        bool declined = askedFifth && fifthRequest is not null && host.Service.Decline(fifthRequest.Id) && !await declining
            && await Wait(() => fifth.Notices.Any(t => t.Contains("거절")), TimeSpan.FromSeconds(1));
        Result("approval: declined", declined, string.Join(" | ", fifth.Notices.TakeLast(1)));
        host.Service.SetAdmission(false, []);

        // The handshake and a token holder's forged, flooded or replayed packets: Tests/PartySessionTests.
        host.Service.Leave();
        bool ended = await Wait(() => peers[0].Service.Status is null && peers[1].Service.Status is null, TimeSpan.FromSeconds(1));
        Result("host leaving ends the session for everyone", ended && host.Service.Token is null,
            string.Join(" | ", peers[0].Notices));

        var late = new Node("늦은");
        late.Attach();
        bool lateJoined = await late.Service.Join(token, late.Name);
        Result("old token dead after the session ended (nobody listening)", !lateJoined, string.Join(" | ", late.Notices));

        bool rehosted = await host.Service.Host(Port, host.Name, "127.0.0.1");
        bool differs = rehosted && host.Service.Token is { } t2 && t2 != token;
        bool oldJoined = await late.Service.Join(token, late.Name);
        Result("new session on the same port has a new secret; old token refused", differs && !oldJoined, string.Join(" | ", late.Notices));
        host.Service.Leave();

        // Host without a typed IP awaits the what's-my-IP fetch; a Join meanwhile must be refused, not overwrite it.
        var hosting = late.Service.Host(Port, late.Name);
        bool joinedDuringHost = await late.Service.Join(token, late.Name);
        bool hostedAfter = await hosting;
        bool clean = !joinedDuringHost && late.Notices.Any(n => n.Contains("이미 연결 중"))
            && late.Service.Status == (hostedAfter ? "호스트 중 (1/4)" : null);
        late.Service.Leave();
        Result("concurrent Host + Join: Join refused, one clean session", clean && late.Service.Status is null,
            $"host {(hostedAfter ? "started" : "failed (no public IP)")}; {string.Join(" | ", late.Notices.TakeLast(2))}");

        foreach (var n in everyone.Append(fifth).Append(late)) { n.Service.Dispose(); n.Device.Dispose(); }
        log.Insert(0, all ? "ALL PASSED\n" : "SOME FAILED\n");
        return (all, log.ToString());
    }

    static async Task<bool> Wait(Func<bool> condition, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (!condition())
        {
            if (sw.Elapsed > timeout) return false;
            await Task.Delay(10);
        }
        return true;
    }
}
