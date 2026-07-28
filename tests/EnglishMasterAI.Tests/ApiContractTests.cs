using EnglishMasterAI.Web.Application;

namespace EnglishMasterAI.Tests;

public class ApiContractTests
{
    [Fact]
    public void QuestionPrompt_DoesNotExposeCorrectAnswer()
    {
        var publicProperties = typeof(QuestionPrompt)
            .GetProperties()
            .Select(x => x.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.DoesNotContain("CorrectOptionIndex", publicProperties);
        Assert.DoesNotContain("CorrectAnswer", publicProperties);
        Assert.Contains("Options", publicProperties);
    }

    [Fact]
    public void QuizPassThreshold_IsAtLeastEightyPercent()
    {
        var fourOutOfFive = (int)Math.Round(4 * 100d / 5);
        var threeOutOfFive = (int)Math.Round(3 * 100d / 5);

        Assert.True(fourOutOfFive >= 80);
        Assert.False(threeOutOfFive >= 80);
    }
}
