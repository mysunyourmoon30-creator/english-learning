using EnglishMasterAI.Web.Application;

namespace EnglishMasterAI.Tests;

public class AssessmentScorerTests
{
    [Theory]
    [InlineData(0, "Pre-A1")]
    [InlineData(24, "Pre-A1")]
    [InlineData(25, "A1")]
    [InlineData(40, "A2")]
    [InlineData(55, "B1")]
    [InlineData(70, "B2")]
    [InlineData(85, "C1")]
    [InlineData(95, "C2")]
    [InlineData(100, "C2")]
    public void ToCefrLevel_UsesStableBoundaries(int accuracy, string expected)
    {
        Assert.Equal(expected, AssessmentScorer.ToCefrLevel(accuracy));
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(50, 500)]
    [InlineData(100, 990)]
    public void EstimateToeicScore_IsClampedAndRoundedToFive(int accuracy, int expected)
    {
        var score = AssessmentScorer.EstimateToeicScore(accuracy);
        Assert.Equal(expected, score);
        Assert.InRange(score, 10, 990);
        Assert.Equal(0, score % 5);
    }
}
