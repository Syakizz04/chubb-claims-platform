using System.Text.Json;
using Claims.Contracts;
using Confluent.Kafka;
using Insights.Api.Projection;

namespace Insights.Api.Kafka;

/// <summary>
/// Consumes claims.events and updates the read model.
///
/// Offsets are committed manually, only after the database transaction commits. If we crash in
/// between, Kafka redelivers and the handler's dedupe makes that safe. Together this gives
/// effectively-once processing on top of at-least-once delivery.
///
/// If Insights is down, Claims is unaffected; on restart we resume from the last committed offset (E-4).
/// </summary>
public class ClaimEventsConsumer(IServiceScopeFactory scopes, IConfiguration config, ILogger<ClaimEventsConsumer> log)
    : BackgroundService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected override Task ExecuteAsync(CancellationToken ct) =>
        Task.Factory.StartNew(() => RunAsync(ct), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();

    private async Task RunAsync(CancellationToken ct)
    {
        var consumerConfig = new ConsumerConfig
        {
            BootstrapServers = config["Kafka:BootstrapServers"] ?? "localhost:9092",
            GroupId = config["Kafka:GroupId"] ?? "insights",
            EnableAutoCommit = false,
            AutoOffsetReset = AutoOffsetReset.Earliest, // a new consumer group rebuilds from the start of the topic
            AllowAutoCreateTopics = false,
        };

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var consumer = new ConsumerBuilder<string, string>(consumerConfig)
                    .SetErrorHandler((_, e) => log.LogWarning("Kafka: {Reason}", e.Reason))
                    .Build();
                consumer.Subscribe(Topics.ClaimEvents);
                log.LogInformation("Subscribed to {Topic}", Topics.ClaimEvents);

                while (!ct.IsCancellationRequested)
                {
                    var result = consumer.Consume(TimeSpan.FromSeconds(1));
                    if (result is null) continue;

                    await HandleAsync(result, ct);
                    consumer.Commit(result); // only after the DB transaction committed
                }
                consumer.Close();
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // e.g. the topic doesn't exist yet (Claims creates it) or the broker is down. Back off and resubscribe.
                log.LogWarning("Consumer error, restarting in 5s: {Error}", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task HandleAsync(ConsumeResult<string, string> result, CancellationToken ct)
    {
        ClaimEventMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<ClaimEventMessage>(result.Message.Value, Json);
        }
        catch (JsonException ex)
        {
            // Poison message: log and skip, so one bad message can't block the partition.
            // Production: route it to a dead-letter topic for inspection.
            log.LogError(ex, "Skipping undeserialisable message at {TopicPartitionOffset}", result.TopicPartitionOffset);
            return;
        }
        if (message is null) return;

        using var scope = scopes.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ClaimEventHandler>().HandleAsync(message, ct);
    }
}
