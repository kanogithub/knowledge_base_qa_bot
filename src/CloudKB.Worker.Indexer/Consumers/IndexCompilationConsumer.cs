using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloudKB.SharedKernel;
using CloudKB.Worker.Indexer.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace CloudKB.Worker.Indexer.Consumers;

public class IndexCompilationConsumer : BackgroundService
{
    private readonly IConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<IndexCompilationConsumer> _logger;
    private IModel? _channel;

    public IndexCompilationConsumer(
        IConnection connection,
        IServiceProvider serviceProvider,
        ILogger<IndexCompilationConsumer> logger)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _channel = _connection.CreateModel();

        var queueName = "cloudkb.indexing.compile";
        var dlqQueueName = "cloudkb.indexing.compile.dlq";

        // Declare the queue with DLQ arguments
        var args = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", dlqQueueName }
        };

        _channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args
        );

        // Declare the DLQ
        _channel.QueueDeclare(
            queue: dlqQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null
        );

        // Process one message at a time per consumer instance
        _channel.BasicQos(0, 1, false);

        var consumer = new AsyncEventingBasicConsumer(_channel);
        consumer.Received += async (sender, ea) =>
        {
            var body = ea.Body.ToArray();
            var message = Encoding.UTF8.GetString(body);

            try
            {
                var payload = JsonSerializer.Deserialize<CompileKnowledgeTaskPayload>(message);
                if (payload != null)
                {
                    using var scope = _serviceProvider.CreateScope();
                    var pipeline = scope.ServiceProvider.GetRequiredService<CompilationPipeline>();
                    await pipeline.ExecuteAsync(payload, stoppingToken);
                }

                _channel.BasicAck(ea.DeliveryTag, false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing compilation task. Nacking message.");

                int retryCount = 0;
                if (ea.BasicProperties.Headers != null && ea.BasicProperties.Headers.TryGetValue("retryCount", out var retryObj))
                {
                    if (retryObj is int count)
                    {
                        retryCount = count;
                    }
                    else if (retryObj is byte[] bytes)
                    {
                        if (int.TryParse(Encoding.UTF8.GetString(bytes), out var parsedCount))
                        {
                            retryCount = parsedCount;
                        }
                    }
                    else
                    {
                        try { retryCount = Convert.ToInt32(retryObj); } catch { }
                    }
                }

                if (retryCount >= 2)
                {
                    _logger.LogWarning("Message exceeded retry limit (3 attempts). Dead-lettering.");
                    // Requeue = false pushes it to the DLQ automatically due to the x-dead-letter-routing-key argument
                    _channel.BasicNack(ea.DeliveryTag, false, false);
                }
                else
                {
                    retryCount++;
                    _logger.LogInformation($"Re-queuing message. Retry attempt {retryCount}/3.");

                    var properties = _channel.CreateBasicProperties();
                    properties.Persistent = true;
                    properties.ContentType = "application/json";
                    properties.MessageId = ea.BasicProperties.MessageId;
                    properties.Headers = new Dictionary<string, object>
                    {
                        { "retryCount", retryCount }
                    };

                    _channel.BasicPublish(
                        exchange: "",
                        routingKey: queueName,
                        basicProperties: properties,
                        body: body
                    );

                    _channel.BasicAck(ea.DeliveryTag, false);
                }
            }
        };

        _channel.BasicConsume(
            queue: queueName,
            autoAck: false,
            consumer: consumer
        );

        return Task.CompletedTask;
    }

    public override void Dispose()
    {
        _channel?.Dispose();
        base.Dispose();
    }
}
