namespace GaugeOverlay.Core.Domain;

public enum MeterRole { 딜러, 서포터 }

/// <summary>Who the meter shows: me, my party (4), the whole raid (8), or both parties each in its own block.</summary>
public enum MeterScope { 나, 파티, 공대, 파티별 }

/// <summary>A member's 1 Hz figures as sent to the party (spec § C Party): <see cref="Damage"/> is 피해량, or 조력 피해량 for a
/// support; <see cref="Party"/> 1 or 2; the 전분 timer and record (<see cref="Fight"/>) and their own boss damage figure;
/// <see cref="AtkBuffed"/>: their damage this record dealt under their own 공증 (a 공증 field counts only for who stands in
/// it, so each sends their own; null: their buff row isn't read).</summary>
public sealed record MeterSample(ulong Id, string Name, int ClassId, MeterRole Role, int Party, long Damage, long Dps,
    int? Timer, bool Running, int Fight, long BossTaken, long? AtkBuffed = null);

/// <summary>One meter row: <see cref="Share"/> of the boss damage taken (null: "–" under the dash rule), <see cref="Trend"/>
/// against 10 s ago (−1, 0, 1).</summary>
public sealed record MeterRow(ulong Id, string Name, int ClassId, MeterRole Role, int Party, double? Share, long Damage, long Dps, int Trend);
