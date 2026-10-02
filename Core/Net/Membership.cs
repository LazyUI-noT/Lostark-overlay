namespace GaugeOverlay.Core.Net;

/// <summary>The host's decision on a <c>Hello</c> (PLAN §5: at most 4 members, same protocol version).</summary>
public static class Membership
{
    public const int MaxMembers = 4;
    public static readonly string Full = $"파티가 가득 찼습니다 ({MaxMembers}/{MaxMembers}).";

    /// <summary>Null to accept, else the refusal text shown to the joining user.
    /// <paramref name="members"/> are the confirmed members' sender ids, the host included (a join still in progress
    /// doesn't hold a slot; the host checks again when it confirms). <paramref name="everAdmitted"/> is every id
    /// accepted in this session, departed and unconfirmed members too (their nonce counters already ran under the
    /// group key); it defaults to <paramref name="members"/>.</summary>
    public static string? Refusal(IReadOnlyCollection<ulong> members, ulong joining, byte version,
        IReadOnlyCollection<ulong>? everAdmitted = null)
    {
        if (version != Protocol.Version) return "프로그램 버전이 호스트와 다릅니다. 같은 버전으로 맞춘 뒤 다시 참가해 주세요.";
        if (members.Count >= MaxMembers) return Full;
        if ((everAdmitted ?? members).Any(m => SessionCrypto.NoncePrefix(m) == SessionCrypto.NoncePrefix(joining)))
            return "연결 식별자가 겹쳤습니다. 다시 참가해 주세요.";
        return null;
    }
}
