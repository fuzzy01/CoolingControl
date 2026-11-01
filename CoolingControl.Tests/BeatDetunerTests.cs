using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class BeatDetunerTests
{
    private const float Separation = 150f;
    private const float Nudge = 300f;
    private const float MaxRpm = 2500f;

    [Fact]
    public void Adjust_MatchedFans_StayMatched()
    {
        var result = Adjust(
            new BeatFan("FanB", 1000f, MaxRpm),
            new BeatFan("FanA", 1000f, MaxRpm));

        Assert.Equal(1000f, Placed(result, "FanA"));
        Assert.Equal(1000f, Placed(result, "FanB"));
        Assert.All(result, placement => Assert.False(placement.NudgeCapped || placement.CalibrationCapped));
    }

    [Fact]
    public void Adjust_MatchedPairAboveSlowerFan_RisesTogether()
    {
        var result = Adjust(
            new BeatFan("Low", 1000f, MaxRpm),
            new BeatFan("MidB", 1080f, MaxRpm),
            new BeatFan("MidA", 1080f, MaxRpm));

        Assert.Equal(1000f, Placed(result, "Low"));
        Assert.Equal(1150f, Placed(result, "MidA"));
        Assert.Equal(1150f, Placed(result, "MidB"));
    }

    [Fact]
    public void Adjust_CalibrationSkew_UsesPreviousFan()
    {
        var result = Adjust(
            new BeatFan("Rear Fan", 1504f, MaxRpm),
            new BeatFan("Front Fan", 1517f, MaxRpm));

        Assert.Equal(1504f, Placed(result, "Rear Fan"));
        Assert.Equal(1504f, Placed(result, "Front Fan"));
    }

    [Fact]
    public void Adjust_SkewedPairAboveSlowerFan_RisesTogether()
    {
        var result = Adjust(
            new BeatFan("Low", 1000f, MaxRpm),
            new BeatFan("Mid", 1080f, MaxRpm),
            new BeatFan("MidSkew", 1084f, MaxRpm));

        Assert.Equal(1000f, Placed(result, "Low"));
        Assert.Equal(1150f, Placed(result, "Mid"));
        Assert.Equal(1150f, Placed(result, "MidSkew"));
    }

    [Fact]
    public void Adjust_GapOf91_StillSpreads()
    {
        var result = Adjust(
            new BeatFan("Rear Fan", 450f, MaxRpm),
            new BeatFan("Front Fan", 454f, MaxRpm),
            new BeatFan("AIO Fan", 541f, MaxRpm));

        Assert.Equal(450f, Placed(result, "Rear Fan"));
        Assert.Equal(450f, Placed(result, "Front Fan"));
        Assert.Equal(600f, Placed(result, "AIO Fan"));
    }

    [Fact]
    public void Adjust_GapOf80_OpensToSeparation()
    {
        var result = Adjust(
            new BeatFan("FanA", 1000f, MaxRpm),
            new BeatFan("FanB", 1080f, MaxRpm));

        Assert.Equal(1000f, Placed(result, "FanA"));
        Assert.Equal(1150f, Placed(result, "FanB"));
        Assert.False(placement(result, "FanB").NudgeCapped);
        Assert.False(placement(result, "FanB").CalibrationCapped);
    }

    [Fact]
    public void Adjust_GapAlreadyAtSeparation_IsUnchanged()
    {
        var result = Adjust(
            new BeatFan("FanA", 1000f, MaxRpm),
            new BeatFan("FanB", 1150f, MaxRpm));

        Assert.Equal(1000f, Placed(result, "FanA"));
        Assert.Equal(1150f, Placed(result, "FanB"));
    }

    [Fact]
    public void Adjust_StoppedFan_IsIgnored()
    {
        var result = BeatDetuner.Adjust(
            [
                new BeatFan("Off", 0f, MaxRpm),
                new BeatFan("On", 1000f, MaxRpm)
            ],
            Separation,
            Nudge,
            matchWindowRpm: 50f);

        var on = Assert.Single(result);
        Assert.Equal("On", on.Alias);
        Assert.Equal(1000f, on.ToRpm);
    }

    [Fact]
    public void Adjust_NudgeCap_StopsShortOfFullGap()
    {
        var result = BeatDetuner.Adjust(
            [
                new BeatFan("FanA", 1000f, MaxRpm),
                new BeatFan("FanB", 1080f, MaxRpm)
            ],
            Separation,
            maxNudgeRpm: 40f,
            matchWindowRpm: 50f);

        var high = placement(result, "FanB");
        Assert.Equal(1120f, high.ToRpm);
        Assert.True(high.NudgeCapped);
        Assert.False(high.CalibrationCapped);
    }

    [Fact]
    public void Adjust_CalibrationMax_StopsShortOfFullGap()
    {
        var result = BeatDetuner.Adjust(
            [
                new BeatFan("FanA", 1000f, MaxRpm),
                new BeatFan("FanB", 1080f, 1120f)
            ],
            Separation,
            Nudge,
            matchWindowRpm: 50f);

        var high = placement(result, "FanB");
        Assert.Equal(1120f, high.ToRpm);
        Assert.False(high.NudgeCapped);
        Assert.True(high.CalibrationCapped);
    }

    [Fact]
    public void Adjust_ThreeFanChain_OpensEachGap()
    {
        var result = Adjust(
            new BeatFan("FanA", 1000f, MaxRpm),
            new BeatFan("FanC", 1200f, MaxRpm),
            new BeatFan("FanB", 1100f, MaxRpm));

        Assert.Equal(1000f, Placed(result, "FanA"));
        Assert.Equal(1150f, Placed(result, "FanB"));
        Assert.Equal(1300f, Placed(result, "FanC"));
    }

    [Fact]
    public void Adjust_MatchWindow_ComesFromCaller()
    {
        BeatFan[] fans =
        [
            new("Rear Fan", 1504f, MaxRpm),
            new("Front Fan", 1517f, MaxRpm)
        ];

        var wide = BeatDetuner.Adjust(fans, Separation, Nudge, matchWindowRpm: 50f);
        var narrow = BeatDetuner.Adjust(fans, Separation, Nudge, matchWindowRpm: 0f);

        Assert.Equal(1504f, Placed(wide, "Front Fan"));
        Assert.Equal(1654f, Placed(narrow, "Front Fan"));
    }

    private static List<BeatPlacement> Adjust(params BeatFan[] fans) =>
        BeatDetuner.Adjust(fans, Separation, Nudge, matchWindowRpm: 50f);

    private static BeatPlacement placement(List<BeatPlacement> result, string alias) =>
        Assert.Single(result, item => item.Alias == alias);

    private static float Placed(List<BeatPlacement> result, string alias) =>
        placement(result, alias).ToRpm;
}
