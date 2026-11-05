using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Requests.Infrastructure.Messaging;
using Requests.Infrastructure.Persistence;

namespace Requests.Infrastructure.Outbox;

public sealed class OutboxOptions
{
    public int BatchSize { get; set; } = 20;
    public int MaxRetries { get; set; } = 5;
    public int ProcessingTimeoutMinutes { get; set; } = 5;
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(2);
}

public sealed class OutboxPublisherHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IEventPublisher _publisher;
    private readonly ILogger<OutboxPublisherHostedService> _logger;
    private readonly OutboxOptions _opt;

    public OutboxPublisherHostedService(
        IServiceScopeFactory scopeFactory, IEventPublisher publisher,
        IOptions<OutboxOptions> opt, ILogger<OutboxPublisherHostedService> logger)
    { _scopeFactory = scopeFactory; _publisher = publisher; _opt = opt.Value; _logger = logger; }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await PublishBatchAsync(stoppingToken);
                if (processed == 0) await Task.Delay(_opt.PollInterval, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { _logger.LogError(ex, "Outbox publisher cycle failed"); await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
        }
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<Persistence.AppDbContext>();

        var claimed = await ClaimBatchAsync(db, ct);
        if (claimed.Count == 0) return 0;

        foreach (var msg in claimed)
        {
            try
            {
                var ok = await _publisher.PublishAsync(msg.EventType, msg.Payload, msg.Id, ct);
                if (ok)
                {
                    msg.Status = "Published";
                    msg.PublishedAt = DateTime.UtcNow;
                    msg.ErrorMessage = null;
                }
                else
                {
                    await HandleFailureAsync(db, msg, "Publisher confirm not received", ct);
                }
            }
            catch (Exception ex)
            {
                await HandleFailureAsync(db, msg, ex.Message, ct);
            }
            await db.SaveChangesAsync(ct);
        }
        return claimed.Count;
    }

    private async Task<List<Persistence.Entities.OutboxMessage>> ClaimBatchAsync(
        Persistence.AppDbContext db, CancellationToken ct)
    {
        // Atomic claim with UPDLOCK/READPAST + OUTPUT.
        var conn = (SqlConnection)db.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
UPDATE TOP (@batch) OutboxMessages WITH (READPAST, UPDLOCK, ROWLOCK)
SET Status = 'Processing', ClaimedAt = SYSUTCDATETIME()
OUTPUT inserted.Id, inserted.EventType, inserted.Payload, inserted.CreatedAt,
       inserted.RetryCount, inserted.Status, inserted.ClaimedAt, inserted.PublishedAt
WHERE Status = 'Pending'
   OR (Status = 'Processing' AND ClaimedAt < DATEADD(MINUTE, -@stale, SYSUTCDATETIME()));";
        cmd.Parameters.Add(new SqlParameter("@batch", _opt.BatchSize));
        cmd.Parameters.Add(new SqlParameter("@stale", _opt.ProcessingTimeoutMinutes));

        var result = new List<Persistence.Entities.OutboxMessage>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new Persistence.Entities.OutboxMessage
            {
                Id = reader.GetGuid(0),
                EventType = reader.GetString(1),
                Payload = reader.GetString(2),
                CreatedAt = reader.GetDateTime(3),
                RetryCount = reader.GetInt32(4),
                Status = reader.GetString(5),
                ClaimedAt = reader.IsDBNull(6) ? null : reader.GetDateTime(6),
                PublishedAt = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
            });
        }
        return result;
    }

    private async Task HandleFailureAsync(Persistence.AppDbContext db, Persistence.Entities.OutboxMessage msg, string error, CancellationToken ct)
    {
        msg.RetryCount++;
        msg.ErrorMessage = error.Length > 2000 ? error[..2000] : error;
        if (msg.RetryCount >= _opt.MaxRetries)
        {
            msg.Status = "Failed";
            _logger.LogError("Outbox message {Id} moved to Failed after {N} retries", msg.Id, msg.RetryCount);
        }
        else
        {
            msg.Status = "Pending";
            var delaySeconds = Math.Pow(2, msg.RetryCount); // 2,4,8,16,32
            msg.ClaimedAt = DateTime.UtcNow.AddSeconds(-_opt.ProcessingTimeoutMinutes * 60 - delaySeconds);
            _logger.LogWarning("Outbox message {Id} retry {N} scheduled after {S}s", msg.Id, msg.RetryCount, delaySeconds);
        }
    }
}