using System.Text.Json;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using NotaryGeek.PublicKnowledge.Worker.Configuration;
using NotaryGeek.PublicKnowledge.Worker.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Services;

public sealed class PublicKnowledgeQueueService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false
    };

    private readonly IConfiguration _configuration;
    private readonly PublicKnowledgeOptions _options;

    public PublicKnowledgeQueueService(
        IConfiguration configuration,
        IOptions<PublicKnowledgeOptions> options)
    {
        _configuration = configuration;
        _options = options.Value;
    }

    public async Task EnqueueAsync(
        PublicKnowledgeQueuedRunMessage message,
        CancellationToken cancellationToken)
    {
        var queue = await GetQueueAsync(cancellationToken);
        var json = JsonSerializer.Serialize(message, JsonOptions);
        await queue.SendMessageAsync(json, cancellationToken);
    }

    private async Task<QueueClient> GetQueueAsync(CancellationToken cancellationToken)
    {
        var connectionString = _configuration[_options.OutputStorageConnectionStringSetting];
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException($"Storage setting '{_options.OutputStorageConnectionStringSetting}' is not configured.");
        }

        var queue = new QueueClient(
            connectionString,
            _options.QueueName,
            new QueueClientOptions { MessageEncoding = QueueMessageEncoding.Base64 });

        await queue.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        return queue;
    }
}
