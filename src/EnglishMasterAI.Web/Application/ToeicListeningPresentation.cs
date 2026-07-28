namespace EnglishMasterAI.Web.Application;

public static class ToeicListeningPresentation
{
    public static bool IsListening(QuestionPrompt question) =>
        question.ToeicPart is >= 1 and <= 4;

    public static bool HideOptionText(QuestionPrompt question) =>
        question.ToeicPart is 1 or 2;

    public static string DisplayPrompt(QuestionPrompt question) =>
        question.ToeicPart switch
        {
            1 => StripPartPrefix(question.Prompt),
            2 => "Listen and choose the best response.",
            3 or 4 when string.IsNullOrWhiteSpace(question.SupportingText) =>
                "Listen and choose the best answer.",
            _ => StripPartPrefix(question.Prompt)
        };

    public static string BuildAudioText(QuestionPrompt question)
    {
        if (!IsListening(question))
        {
            throw new ArgumentException(
                "Audio is available only for TOEIC Listening Parts 1-4.",
                nameof(question));
        }

        var spokenOptions = string.Join(
            " ",
            question.Options.Select(
                (option, index) => $"{(char)('A' + index)}. {option}"));
        return question.ToeicPart switch
        {
            1 => spokenOptions,
            2 => $"{StripPartPrefix(question.Prompt)} {spokenOptions}",
            _ when !string.IsNullOrWhiteSpace(question.SupportingText) =>
                $"{question.SupportingText} Question. {StripPartPrefix(question.Prompt)}",
            _ => StripPartPrefix(question.Prompt)
        };
    }

    private static string StripPartPrefix(string prompt)
    {
        var separator = prompt.IndexOf('—');
        return separator >= 0 && separator < prompt.Length - 1
            ? prompt[(separator + 1)..].Trim()
            : prompt.Trim();
    }
}
