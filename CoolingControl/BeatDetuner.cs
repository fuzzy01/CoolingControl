namespace CoolingControl;

public readonly record struct BeatFan(string Alias, float EffectiveRpm, float MaxRpm);

public readonly record struct BeatPlacement(
    string Alias,
    float FromRpm,
    float ToRpm,
    bool NudgeCapped,
    bool CalibrationCapped);

/// <summary>
/// Fans within the match window take the previous fan's placed RPM.
/// A wider gap that is still inside the separation minimum is opened by raising the faster fan.
/// A fan at or below 0 RPM is left out.
/// </summary>
public static class BeatDetuner
{
    public static List<BeatPlacement> Adjust(
        IReadOnlyList<BeatFan> fans,
        float minSeparationRpm,
        float maxNudgeRpm,
        float matchWindowRpm)
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
                if (fan.EffectiveRpm - previous.EffectiveRpm <= matchWindowRpm)
                    target = previousPlaced;
                else if (fan.EffectiveRpm - previousPlaced >= minSeparationRpm)
                    target = fan.EffectiveRpm;
                else
                    target = previousPlaced + minSeparationRpm;
            }

            placements.Add(Place(fan, target, maxNudgeRpm));
        }

        return placements;
    }

    private static BeatPlacement Place(BeatFan fan, float target, float maxNudgeRpm)
    {
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

        // A matched fan may sit below its request. An upward target stays at or above the request.
        if (target >= fan.EffectiveRpm && limited < fan.EffectiveRpm)
            limited = fan.EffectiveRpm;

        return new BeatPlacement(fan.Alias, fan.EffectiveRpm, limited, nudgeCapped, calibrationCapped);
    }
}
