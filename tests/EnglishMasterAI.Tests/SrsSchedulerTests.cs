using EnglishMasterAI.Web.Application;

namespace EnglishMasterAI.Tests;

public class SrsSchedulerTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Again_ResetsRepetitionAndReturnsCardInTenMinutes()
    {
        var result = SrsScheduler.Calculate(0, 4, 12, 2.5, Now);

        Assert.Equal(0, result.Repetition);
        Assert.Equal(0, result.IntervalDays);
        Assert.Equal(Now.AddMinutes(10), result.NextReviewAt);
        Assert.True(result.EaseFactor < 2.5);
    }

    [Fact]
    public void Good_FirstReviewSchedulesTomorrow()
    {
        var result = SrsScheduler.Calculate(2, 0, 0, 2.5, Now);

        Assert.Equal(1, result.Repetition);
        Assert.Equal(1, result.IntervalDays);
        Assert.Equal(Now.AddDays(1), result.NextReviewAt);
    }

    [Fact]
    public void Easy_IncreasesEaseAndInterval()
    {
        var result = SrsScheduler.Calculate(3, 3, 5, 2.5, Now);

        Assert.Equal(4, result.Repetition);
        Assert.True(result.EaseFactor > 2.5);
        Assert.True(result.IntervalDays > 5);
    }

    [Theory]
    [InlineData(-99)]
    [InlineData(99)]
    public void Rating_IsSafelyClamped(int rating)
    {
        var result = SrsScheduler.Calculate(rating, 0, 0, 2.5, Now);
        Assert.InRange(result.EaseFactor, 1.3, 3.0);
    }
}
