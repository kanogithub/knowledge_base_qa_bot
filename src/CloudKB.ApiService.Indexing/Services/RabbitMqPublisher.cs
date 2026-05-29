using System;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CloudKB.SharedKernel;
using RabbitMQ.Client;

namespace CloudKB.ApiService.Indexing.Services;

public class RabbitMqPublisher
{
    private readonly IConnection _connection;

    public RabbitMqPublisher(IConnection connection)
    {
        _connection = connection;
    }

    public Task PublishCompileTaskAsync(CompileKnowledgeTaskPayload task, CancellationToken ct)
    {
        using var channel = _connection.CreateModel();

        var exchangeName = "cloudkb.indexing";
        channel.ExchangeDeclare(
            exchange: exchangeName,
            type: ExchangeType.Topic,
            durable: true,
            autoDelete: false
        );

        var queueName = "cloudkb.indexing.compile";
        var dlqQueueName = "cloudkb.indexing.compile.dlq";
        var args = new System.Collections.Generic.Dictionary<string, object>
        {
            { "x-dead-letter-exchange", "" },
            { "x-dead-letter-routing-key", dlqQueueName }
        };
        channel.QueueDeclare(
            queue: queueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: args
        );

        var routingKey = $"compile.{task.TenantId}";
        channel.QueueBind(
            queue: queueName,
            exchange: exchangeName,
            routingKey: routingKey
        );

        var json = JsonSerializer.Serialize(task);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = channel.CreateBasicProperties();
        properties.Persistent = true;
        properties.ContentType = "application/json";
        properties.MessageId = task.TaskId.ToString();
        properties.Timestamp = new AmqpTimestamp((long)(task.RequestedAt - DateTime.UnixEpoch).TotalSeconds);
        properties.Headers = new System.Collections.Generic.Dictionary<string, object>
        {
            { "messageId", task.TaskId.ToString() },
            { "timestamp", task.RequestedAt.ToString("O") },
            { "retryCount", 0 }
        };

        channel.BasicPublish(
            exchange: exchangeName,
            routingKey: routingKey,
            basicProperties: properties,
            body: body
        );

        return Task.CompletedTask;
    }
}
