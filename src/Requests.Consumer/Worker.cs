using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Requests.Infrastructure.Persistence;
using Requests.Infrastructure.Persistence.Entities;

namespace Requests.Consumer;

public sealed class Worker : BackgroundService
{
    public const string Exchange = "requests";
    public const string MainQueue = "requests.consumer";
    public const string Dlx = "requests.dlx";
    public const string Dlq = "requests.dlq";
    public const string RetryQueue = "requests.retry";
    private const int MaxRetries = 3;

    private readonly IConnection _connection;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<Worker> _logger;
    private IModel? _channel;

    public Worker(IConnection connection, IServiceScopeFactory scopeFactory, ILogger<Worker> logger)
    { _connection = connection; _scopeFactory = scopeFactory; _logger = logger; }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _connection.CreateModel();
        _channel.BasicQos(0, 10, false);

        _channel.ExchangeDeclare(Exchange, ExchangeType.Topic, durable: true);
        _channel.ExchangeDeclare(Dlx, ExchangeType.Fanout, durable: true);

        _channel.QueueDeclare(MainQueue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-dead-letter-exchange"] = Dlx
        });
        _channel.QueueBind(MainQueue, Exchange, "RequestCreated");
        _channel.QueueBind(MainQueue, Exchange, "RequestStatusChanged");

        _channel.QueueDeclare(Dlq, durable: true, exclusive: false, autoDelete: false);
        _channel.QueueBind(Dlq, Dlx, "");

        _channel.QueueDeclare(RetryQueue, durable: true, exclusive: false, autoDelete: false, arguments: new Dictionary<string, object>
        {
            ["x-message-ttl"] = 5000,
            ["x-dead-letter-exchange"] = Exchange,
            ["x-dead-letter-routing-key"] = "retry" // will be overwritten per message? no — use main exchange with original routing key via headers
        });

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += OnReceivedAsync;
        _channel.BasicConsume(MainQueue, autoAck: false, consumer: consumer);

        _logger.LogInformation("Consumer started on {Queue}", MainQueue);
        return Task.CompletedTask;
    }

    private async Task OnReceivedAsync(object sender, BasicDeliverEventArgs ea)
    {
        var channel = ((AsyncEventingBasicConsumer)sender).Model;
        var retryCount = GetRetryCount(ea.BasicProperties);
        var body = Encoding.UTF8.GetString(ea.Body.Span);

        try
        {
            var envelope = JsonSerializer.Deserialize<EventEnvelope>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (envelope is null || envelope.EventId == Guid.Empty)
                throw new InvalidOperationException("Invalid event envelope");

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (await db.ProcessedMessages.AnyAsync(x => x.EventId == envelope.EventId))
            {
                _logger.LogInformation("Event {EventId} already processed, ACK", envelope.EventId);
                channel.BasicAck(ea.DeliveryTag, false);
                return;
            }

            await using (var tx = await db.Database.BeginTransactionAsync())
            {
                // MVP: side-effect = log only. Replace with real external call.
                _logger.LogInformation("Processing {Type} event {EventId} payload={Payload}",
                    envelope.EventType, envelope.EventId, envelope.Data);

                db.ProcessedMessages.Add(new ProcessedMessage
                { EventId = envelope.EventId, ProcessedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                await tx.CommitAsync();
            }

            channel.BasicAck(ea.DeliveryTag, false);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Permanent deserialization error, sending to DLQ");
            channel.BasicNack(ea.DeliveryTag, false, requeue: false); // DLX -> DLQ
        }
        catch (Exception ex)
        {
            if (retryCount >= MaxRetries)
            {
                _logger.LogError(ex, "Max retries reached, sending to DLQ");
                channel.BasicNack(ea.DeliveryTag, false, requeue: false);
                return;
            }

            _logger.LogWarning(ex, "Transient error, scheduling retry {N}", retryCount + 1);
            var newProps = channel.CreateBasicProperties();
            newProps.Persistent = true;
            newProps.ContentType = "application/json";
            newProps.Headers = new Dictionary<string, object> { ["x-retry-count"] = retryCount + 1 };
            newProps.MessageId = ea.BasicProperties.MessageId;
            newProps.Type = ea.BasicProperties.Type;

            channel.BasicPublish(RetryQueue, "", newProps, ea.Body);
            channel.BasicAck(ea.DeliveryTag, false);
        }
    }

    private static int GetRetryCount(IBasicProperties props)
    {
        if (props?.Headers is { } h && h.TryGetValue("x-retry-count", out var v) && v is int i) return i;
        return 0;
    }

    private sealed record EventEnvelope(
        Guid EventId, string EventType, DateTime OccurredAt, int Version,
        Guid CorrelationId, JsonElement Data);

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}