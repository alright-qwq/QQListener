using Microsoft.Extensions.Logging;
using QQListener.Models;

namespace QQListener.Services;

public class QqMessageProcessor
{
    private readonly QqToastMessageParser _parser;
    private readonly MessageEventPipeline _pipeline;
    private readonly ClassIslandMessageNotifier _notifier;

    public QqMessageProcessor(
        QqToastMessageParser parser,
        MessageEventPipeline pipeline,
        ClassIslandMessageNotifier notifier,
        ILogger<QqMessageProcessor> logger)
    {
        _parser = parser;
        _pipeline = pipeline;
        _notifier = notifier;
        _ = logger;
    }

    public QqNotificationMessage? Process(IReadOnlyList<string> texts)
    {
        if (!_parser.TryParse(texts, DateTimeOffset.Now, out var messageEvent, out _)
            || messageEvent == null)
            return null;

        var result = _pipeline.Process(messageEvent);
        if (!result.Accepted || result.Event == null)
            return null;

        return QqNotificationMessage.FromMessageEvent(result.Event, _notifier.GetDuration(result.Event.Importance));
    }

    public void UpdateActiveToasts(IEnumerable<IReadOnlyList<string>> visibleToasts)
    {
        _pipeline.UpdateActiveEvents(visibleToasts
            .Select(x => _parser.ParseOrDefault(x, DateTimeOffset.Now))
            .OfType<MessageEvent>());
    }

    public void ClearState()
    {
        _pipeline.ClearState();
    }
}
