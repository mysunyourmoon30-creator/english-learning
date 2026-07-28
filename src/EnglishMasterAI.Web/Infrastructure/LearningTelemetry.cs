using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace EnglishMasterAI.Web.Infrastructure;

public sealed class LearningTelemetry : IDisposable
{
    public const string ActivitySourceName = "EnglishMasterAI.Learning";
    public const string MeterName = "EnglishMasterAI.Learning";

    private readonly Meter _meter = new(MeterName);
    private readonly ActivitySource _activitySource = new(ActivitySourceName);
    private readonly Counter<long> _aiRequests;
    private readonly Counter<long> _aiFailures;
    private readonly Counter<long> _aiFallbacks;
    private readonly Histogram<double> _aiLatency;
    private readonly Counter<long> _learningActivities;

    public LearningTelemetry()
    {
        _aiRequests = _meter.CreateCounter<long>("englishmaster.ai.requests");
        _aiFailures = _meter.CreateCounter<long>("englishmaster.ai.failures");
        _aiFallbacks = _meter.CreateCounter<long>("englishmaster.ai.fallbacks");
        _aiLatency = _meter.CreateHistogram<double>(
            "englishmaster.ai.duration",
            "ms");
        _learningActivities = _meter.CreateCounter<long>(
            "englishmaster.learning.activities");
    }

    public Activity? StartAiActivity(string operation, string model)
    {
        var activity = _activitySource.StartActivity(
            $"ai.{operation}",
            ActivityKind.Client);
        activity?.SetTag("gen_ai.system", "openai");
        activity?.SetTag("gen_ai.operation.name", operation);
        activity?.SetTag("gen_ai.request.model", model);
        return activity;
    }

    public void RecordAiRequest(string operation, string model, double durationMs)
    {
        var tags = new TagList
        {
            { "operation", operation },
            { "model", model }
        };
        _aiRequests.Add(1, tags);
        _aiLatency.Record(durationMs, tags);
    }

    public void RecordAiFailure(string operation, string model) =>
        _aiFailures.Add(
            1,
            new TagList { { "operation", operation }, { "model", model } });

    public void RecordAiFallback(string operation, string reason) =>
        _aiFallbacks.Add(
            1,
            new TagList { { "operation", operation }, { "reason", reason } });

    public void RecordLearningActivity(string kind) =>
        _learningActivities.Add(1, new TagList { { "kind", kind } });

    public void Dispose()
    {
        _activitySource.Dispose();
        _meter.Dispose();
    }
}
