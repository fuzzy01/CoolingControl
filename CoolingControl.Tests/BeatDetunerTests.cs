using CoolingControl;
using Xunit;

namespace CoolingControl.Tests;

public class BeatDetunerTests
{
    private const float Separation = 150f;
    private const float Nudge = 200f;
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
            Nudge);

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
            maxNudgeRpm: 40f);

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
            Nudge);

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

    private static List<BeatPlacement> Adjust(params BeatFan[] fans) =>
        BeatDetuner.Adjust(fans, Separation, Nudge);

    private static BeatPlacement placement(List<BeatPlacement> result, string alias) =>
        Assert.Single(result, item => item.Alias == alias);

    private static float Placed(List<BeatPlacement> result, string alias) =>
        placement(result, alias).ToRpm;
}
