using GaugeOverlay.Core.Domain;

namespace GaugeOverlay.Core.Net;

/// <summary>A joiner the host is asking about (참가 요청 승인): answer with <see cref="PartySession.Approve"/> or
/// <see cref="PartySession.Decline"/>. <see cref="Device"/> is its <see cref="DeviceIdentity.Fingerprint"/>.</summary>
public sealed record JoinRequest(ulong Id, string Name, Version App, string Device);

/// <summary>Host only: what happened at the door since the last <see cref="PartySession.TakeChanges"/>. Asked: new
/// requests to show; Withdrawn: requests gone unanswered (the joiner gave up or left); Trusted: devices approved, to
/// keep; Untrusted: devices kicked, to forget; Refused: one line per refused join for a notice (wrong version, full,
/// blocked), at most one per joiner and reason a minute.</summary>
public sealed record AdmissionChanges(IReadOnlyList<JoinRequest> Asked, IReadOnlyList<ulong> Withdrawn,
    IReadOnlyList<TrustedDevice> Trusted, IReadOnlyList<string> Untrusted, IReadOnlyList<string> Refused)
{
    public static readonly AdmissionChanges None = new([], [], [], [], []);
    public bool Any => Asked.Count + Withdrawn.Count + Trusted.Count + Untrusted.Count + Refused.Count > 0;
}

/// <summary>What changed since the last <see cref="PartySession.TakeChanges"/>: <see cref="PartySession.Peers"/>,
/// <see cref="PartySession.Status"/>, a one-off message for the user (refused, host lost…), and the host's admissions.</summary>
public sealed record PartyChanges(bool Peers, bool Status, string? Notice, AdmissionChanges Admission);
