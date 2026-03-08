using System.Threading.Channels;

using OWLProtect.Core;

namespace OWLProtect.ControlPlane.Api;

public sealed record ControlPlaneStreamFrame(
    string Kind,
    string Topic,
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    string? EventType,
    string? EntityId,
    object? Payload);

internal sealed record ControlPlaneTopicNotification(
    string Topic,
    long Sequence,
    DateTimeOffset OccurredAtUtc,
    string EventType,
    string? EntityId);

internal sealed class ControlPlaneEventStream : IControlPlaneEventPublisher
{
    private readonly Lock _gate = new();
    private long _nextSequence;
    private readonly Dictionary<string, long> _latestTopicSequences = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, SubscriptionRegistration> _subscriptions = [];

    public long GetLatestSequence(string topic)
    {
        lock (_gate)
        {
            return _latestTopicSequences.TryGetValue(topic, out var sequence) ? sequence : 0L;
        }
    }

    public ControlPlaneStreamSubscription Subscribe(string topic)
    {
        var channel = Channel.CreateUnbounded<ControlPlaneTopicNotification>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        var subscriptionId = Guid.NewGuid();

        lock (_gate)
        {
            _subscriptions[subscriptionId] = new SubscriptionRegistration(topic, channel.Writer);
        }

        return new ControlPlaneStreamSubscription(channel.Reader, () => Unsubscribe(subscriptionId));
    }

    public void Publish(string topic, string eventType, string? entityId = null)
    {
        ControlPlaneTopicNotification notification;
        ChannelWriter<ControlPlaneTopicNotification>[] writers;

        lock (_gate)
        {
            notification = new ControlPlaneTopicNotification(
                topic,
                ++_nextSequence,
                DateTimeOffset.UtcNow,
                eventType,
                entityId);
            _latestTopicSequences[topic] = notification.Sequence;
            writers = _subscriptions
                .Values
                .Where(subscription => string.Equals(subscription.Topic, topic, StringComparison.OrdinalIgnoreCase))
                .Select(subscription => subscription.Writer)
                .ToArray();
        }

        foreach (var writer in writers)
        {
            writer.TryWrite(notification);
        }
    }

    private void Unsubscribe(Guid subscriptionId)
    {
        lock (_gate)
        {
            if (_subscriptions.Remove(subscriptionId, out var subscription))
            {
                subscription.Writer.TryComplete();
            }
        }
    }

    private sealed record SubscriptionRegistration(
        string Topic,
        ChannelWriter<ControlPlaneTopicNotification> Writer);
}

internal sealed class ControlPlaneStreamSubscription(
    ChannelReader<ControlPlaneTopicNotification> reader,
    Action dispose) : IDisposable
{
    public ChannelReader<ControlPlaneTopicNotification> Reader { get; } = reader;

    public void Dispose() => dispose();
}
