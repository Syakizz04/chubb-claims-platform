using Claims.Api.Data;
using Claims.Contracts;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Claims.Api.Outbox;

public class OutboxOptions
{
    public bool Enabled { get; set; } = true;
    public int PollIntervalMs { get; set; } = 500;
    public int BatchSize { get; set; } = 100;
}

/// <summary>
/// Relays outbox rows to Kafka (E-2). Delivery is at-least-once: if we crash after Kafka acks but
/// before PublishedAt is saved, the row is sent again, and the consumer's idempotency handles that (E-3).
///
/// Rows are published strictly in insert order, and a failure stops the batch, so one claim's events
/// can't overtake each other. While Kafka is down, the API keeps accepting writes and rows build up here.
///
/// Assumes a single publisher instance. To scale out, partition the work by key (or use leader election);
/// plain SKIP LOCKED would break per-claim ordering.
/// </summary>
public class OutboxPublisher(
    IServiceScopeFactory scopes,
    IConfiguration config,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher> log) : BackgroundService
{
    private readonly OutboxOptions _opt = options.Value;
    private string Bootstrap => config["Kafka:BootstrapServers"] ?? "localhost:9092";

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        await EnsureTopicAsync(ct);

        using var producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = Bootstrap,
            Acks = Acks.All,          // wait for all in-sync replicas
            EnableIdempotence = true, // no duplicates or reordering from producer retries
            MessageTimeoutMs = 10_000,
        }).Build();

        var backoff = TimeSpan.FromMilliseconds(_opt.PollIntervalMs);
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var sent = await PublishBatchAsync(producer, ct);
                backoff = TimeSpan.FromMilliseconds(_opt.PollIntervalMs);
                if (sent == _opt.BatchSize) continue; // more waiting: go again straight away
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                log.LogWarning("Outbox publish failed, retrying in {Backoff}s: {Error}", backoff.TotalSeconds, ex.Message);
                backoff = TimeSpan.FromSeconds(Math.Min(backoff.TotalSeconds * 2, 30));
            }

            await Task.Delay(backoff, ct).ContinueWith(_ => { }, CancellationToken.None);
        }
    }

    private async Task<int> PublishBatchAsync(IProducer<string, string> producer, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ClaimsDbContext>();

        var batch = await db.Outbox
            .Where(o => o.PublishedAt == null)
            .OrderBy(o => o.Id)
            .Take(_opt.BatchSize)
            .ToListAsync(ct);

        var sent = 0;
        foreach (var row in batch)
        {
            try
            {
                await producer.ProduceAsync(row.Topic, new Message<string, string>
                {
                    Key = row.Key,
                    Value = row.Payload,
                    Headers = new Headers
                    {
                        { "event-id", System.Text.Encoding.UTF8.GetBytes(row.EventId.ToString()) },
                        { "event-type", System.Text.Encoding.UTF8.GetBytes(row.EventType) },
                    },
                }, ct);

                row.PublishedAt = DateTimeOffset.UtcNow;
                row.LastError = null;
                sent++;
            }
            catch (ProduceException<string, string> ex)
            {
                row.Attempts++;
                row.LastError = ex.Error.Reason;
                await db.SaveChangesAsync(CancellationToken.None);
                throw; // stop the batch: preserve ordering
            }
        }

        if (sent > 0)
        {
            await db.SaveChangesAsync(CancellationToken.None);
            log.LogInformation("Published {Count} claim event(s) to Kafka", sent);
        }
        return sent;
    }

    /// <summary>Topics are created explicitly (auto-create is disabled on the broker).</summary>
    private async Task EnsureTopicAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = Bootstrap }).Build();
                await admin.CreateTopicsAsync(
                [
                    // 3 partitions: room for parallel consumers. The claim-id key keeps per-claim order.
                    new TopicSpecification { Name = Topics.ClaimEvents, NumPartitions = 3, ReplicationFactor = 1 },
                ]);
                log.LogInformation("Created Kafka topic {Topic}", Topics.ClaimEvents);
                return;
            }
            catch (CreateTopicsException ex) when (ex.Results.All(r => r.Error.Code == ErrorCode.TopicAlreadyExists))
            {
                return;
            }
            catch (Exception ex)
            {
                log.LogWarning("Kafka not reachable yet ({Error}); retrying in 5s. The API keeps accepting claims.", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }
}
