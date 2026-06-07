using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using QQListener.Models;
using QQListener.Services;

var samplePath = Path.Combine(AppContext.BaseDirectory, "qq-toast-samples.json");
var samples = JsonSerializer.Deserialize<List<SampleCase>>(File.ReadAllText(samplePath), new JsonSerializerOptions
{
    PropertyNameCaseInsensitive = true
}) ?? [];

if (samples.Count < 20)
{
    Fail($"Expected at least 20 samples, found {samples.Count}.");
}

var settings = new QqListenerSettings
{
    ImportantPersonsText = "班主任",
    ImportantKeywordsText = "作业\n通知\n考试\nNOTICE",
    BlacklistText = "广告\n抽奖",
    CallingKeyword = "呼叫",
    CallingEnabled = true,
    SomeoneAtMe = true,
    CooldownSeconds = 60
};
settings.Normalize();

var diagnostics = new QqListenerDiagnosticsState();
var parser = new QqToastMessageParser();
var evaluator = new MessageRuleEvaluator(settings);
var pipeline = new MessageEventPipeline(settings, evaluator, diagnostics, NullLogger<MessageEventPipeline>.Instance);

var failures = new List<string>();
foreach (var sample in samples)
{
    if (sample.CandidateWhenAppInfoUnavailable is { } expectedCandidate
        && QqToastMessageParser.LooksLikeQqToastShape(sample.Texts) != expectedCandidate)
    {
        failures.Add($"{sample.Name}: AppInfo-unavailable candidate expected {expectedCandidate}.");
    }

    if (!parser.TryParse(sample.Texts, DateTimeOffset.Parse("2026-06-07T12:00:00+08:00"), out var parsed, out var parseReason)
        || parsed == null)
    {
        if (sample.ParseShouldFail)
        {
            continue;
        }

        failures.Add($"{sample.Name}: parse failed unexpectedly: {parseReason}");
        continue;
    }

    if (sample.ParseShouldFail)
    {
        failures.Add($"{sample.Name}: expected parse failure but parser produced an event.");
        continue;
    }

    var result = pipeline.Process(parsed);
    var actualEvent = result.Event ?? parsed;
    var expectedImportance = Enum.Parse<MessageImportance>(sample.Importance, ignoreCase: true);

    Check(sample, "sender", sample.Sender, actualEvent.Sender);
    Check(sample, "conversation", sample.Conversation, actualEvent.Conversation);
    Check(sample, "body", NormalizeNewLines(sample.Body), NormalizeNewLines(actualEvent.Body));

    if (result.Accepted == sample.Ignored)
    {
        failures.Add($"{sample.Name}: ignored expected {sample.Ignored}, accepted was {result.Accepted}.");
    }

    if (actualEvent.Importance != expectedImportance)
    {
        failures.Add($"{sample.Name}: importance expected {expectedImportance}, got {actualEvent.Importance}.");
    }

    if (!string.IsNullOrWhiteSpace(sample.IgnoreReasonContains)
        && !result.Reason.Contains(sample.IgnoreReasonContains, StringComparison.OrdinalIgnoreCase))
    {
        failures.Add($"{sample.Name}: ignore reason should contain '{sample.IgnoreReasonContains}', got '{result.Reason}'.");
    }
}

if (failures.Count > 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine(failure);
    }

    Environment.Exit(1);
}

Console.WriteLine($"Verified {samples.Count} QQ toast samples.");

void Check(SampleCase sample, string field, string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        failures.Add($"{sample.Name}: {field} expected '{expected}', got '{actual}'.");
    }
}

string NormalizeNewLines(string value) =>
    value.Replace("\r\n", "\n", StringComparison.Ordinal)
        .Replace("\r", "\n", StringComparison.Ordinal);

void Fail(string message)
{
    Console.Error.WriteLine(message);
    Environment.Exit(1);
}

internal sealed record SampleCase(
    string Name,
    IReadOnlyList<string> Texts,
    string Sender,
    string Conversation,
    string Body,
    string Importance,
    bool Ignored,
    bool ParseShouldFail = false,
    string IgnoreReasonContains = "",
    bool? CandidateWhenAppInfoUnavailable = null);
