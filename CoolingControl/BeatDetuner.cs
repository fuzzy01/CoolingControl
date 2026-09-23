namespace CoolingControl;

public readonly record struct BeatFan(string Alias, float EffectiveRpm, float MaxRpm);

public readonly record struct BeatPlacement(
    string Alias,
    float FromRpm,
    float ToRpm,
    bool NudgeCapped,
    bool CalibrationCapped);

/// <summary>
/// Raises opted-in fan speeds that sit closer than the separation window.
/// A fan at or below 0 RPM is left out. Speeds are never lowered.
/// </summary>
public static class BeatDetuner
{
    public const float MatchWindowRpm = 1f;

    public static List<BeatPlacement> Adjust(
        IReadOnlyList<BeatFan> fans,
        float minSeparationRpm,
        float maxNudgeRpm)
    {
        var sorted = fans
            .Where(fan => fan.EffectiveRpm > 0f)
            .OrderBy(fan => fan.EffectiveRpm)
            .ThenBy(fan => fan.Alias, StringComparer.Ordinal)
            .ToList();

        var placements = new List<BeatPlacement>(sorted.Count);
        for (int i = 0; i < sorted.Count; i++)
        {
            var fan = sorted[i];
            float target;
            if (i == 0)
            {
                target = fan.EffectiveRpm;
            }
            else
            {
                var previous = sorted[i - 1];
                float previousPlaced = placements[i - 1].ToRpm;
                if (fan.EffectiveRpm - previous.EffectiveRpm <= MatchWindowRpm)
                    target = previousPlaced;
                else if (fan.EffectiveRpm - previousPlaced >= minSeparationRpm)
                    target = fan.EffectiveRpm;
                else
                    target = previousPlaced + minSeparationRpm;
            }

            float limited = target;
            bool nudgeCapped = false;
            bool calibrationCapped = false;
            float nudgeLimit = fan.EffectiveRpm + maxNudgeRpm;
            if (target > nudgeLimit)
            {
                limited = nudgeLimit;
                nudgeCapped = true;
            }

            if (target > fan.MaxRpm)
            {
                limited = Math.Min(limited, fan.MaxRpm);
                calibrationCapped = true;
            }

            if (limited < fan.EffectiveRpm)
                limited = fan.EffectiveRpm;

            placements.Add(new BeatPlacement(fan.Alias, fan.EffectiveRpm, limited, nudgeCapped, calibrationCapped));
        }

        return placements;
    }
}
