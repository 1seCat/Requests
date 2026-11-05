using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;

namespace Requests.Infrastructure.Messaging;

public interface IEventPublisher
{
    Task<bool> PublishAsync(string eventType, string payload, Guid eventId, CancellationToken ct);
    void EnsureTopology();
}

public sealed class RabbitMqPublisher : IEventPublisher, IDisposable
{
    public const string Exchange = "requests";
    public const string Dlx = "requests.dlx";

    private readonly IConnection _connection;
    private readonly IModel _channel;
    private readonly ILogger<RabbitMqPublisher> _logger;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<bool>> _confirms = new();
    private ulong _nextTag = 0;

    public RabbitMqPublisher(IConnection connection, ILogger<RabbitMqPublisher> logger)
    {
        _connection = connection;
        _logger = logger;
        _channel = _connection.CreateModel();
        _channel.ConfirmSelect();
        _channel.BasicAcks += (_, e) => Complete(e.DeliveryTag, true);
        _channel.BasicNacks += (_, e) => Complete(e.DeliveryTag, false);
        _channel.BasicReturn += (_, e) =>
            _logger.LogWarning("Unroutable message returned: {Reply}", e.ReplyText);
        EnsureTopology();
    }

    public void EnsureTopology()
    {
        _channel.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true, autoDelete: false);
        _channel.ExchangeDeclare(Dlx, ExchangeType.Fanout, durable: true, autoDelete: false);
    }

    public async Task<bool> PublishAsync(string eventType, string payload, Guid eventId, CancellationToken ct)
    {
        var props = _channel.CreateBasicProperties();
        props.Persistent = true;
        props.ContentType = "application/json";
        props.MessageId = eventId.ToString();
        props.Type = eventType;
        props.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        ulong tag;
        lock (_channel)
        {
            tag = ++_nextTag;
            _confirms[tag] = tcs;
            _channel.BasicPublish(
                exchange: Exchange,
                routingKey: eventType,
                mandatory: true,
                basicProperties: props,
                body: Encoding.UTF8.GetBytes(payload));
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        await using var _ = cts.Token.Register(() => tcs.TrySetResult(false));
        return await tcs.Task;
    }

    private void Complete(ulong tag, bool ok)
    {
        if (_confirms.TryRemove(tag, out var tcs)) tcs.TrySetResult(ok);
    }

    public void Dispose() => _channel?.Dispose();
}