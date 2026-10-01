using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Xml.Linq;
using Azure.Core.Pipeline;
using Azure.Storage.Blobs;
using Azure.Storage.Queues;
using Azure.Storage.Queues.Models;

namespace NotaryGeek.PublicKnowledge.Worker.Tests;

// Socket-free HTTP protocol fixture. Real SDK clients serialize requests and parse responses.
// It does not implement production admission, phase, ordering, or reconciliation decisions.
internal sealed class SyntheticBlobStore : HttpMessageHandler
{
    internal sealed record Blob(byte[] Body, string ETag);
    internal sealed record Request(string Method, string Name, IReadOnlyDictionary<string, string> Headers, byte[] Body,
        IReadOnlyDictionary<string, string> Query)
    {
        public bool IsWrite => Method == "PUT" && !Query.ContainsKey("restype");
        public string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
    }
    private readonly object _gate = new();
    private readonly Dictionary<string, Blob> _blobs = new(StringComparer.Ordinal);
    private long _version;
    private bool _containerCreated;
    public ConcurrentQueue<Request> Requests { get; } = new();
    public ConcurrentQueue<string> QueueBodies { get; } = new();
    public Func<Request, CancellationToken, Task>? Before { get; set; }
    public Func<Request, CancellationToken, Task>? After { get; set; }
    public Func<Request, HttpResponseMessage?>? Fault { get; set; }
    public int PageSize { get; set; } = 2;
    public SyntheticBlobStore() => RequireNoAmbientCloudIdentity();
    public static void RequireNoAmbientCloudIdentity()
    {
        foreach (var name in new[] { "AZURE_CLIENT_ID", "AZURE_TENANT_ID", "AZURE_CLIENT_SECRET",
            "AZURE_CLIENT_CERTIFICATE_PATH", "AZURE_FEDERATED_TOKEN_FILE", "MSI_ENDPOINT", "IDENTITY_ENDPOINT" })
            if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(name)))
                throw new InvalidOperationException("Ambient cloud identity is forbidden in synthetic storage fixtures.");
    }
    public BlobContainerClient Client() => new(new Uri("https://storage.invalid/fixture"), new BlobClientOptions
    {
        Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)),
        Retry = { MaxRetries = 0 }
    });
    public QueueClient QueueClient() => new(new Uri("https://queue.invalid/fixture"), new QueueClientOptions
    {
        Transport = new HttpClientTransport(new HttpClient(this, disposeHandler: false)),
        Retry = { MaxRetries = 0 }, MessageEncoding = QueueMessageEncoding.Base64
    });
    public IReadOnlyDictionary<string, Blob> Snapshot()
    {
        lock (_gate) return _blobs.ToDictionary(x => x.Key, x => x.Value with { Body = x.Value.Body.ToArray() });
    }
    public void Seed(string name, string body)
    {
        lock (_gate) _blobs[name] = new(Encoding.UTF8.GetBytes(body), $"\"{++_version}\"");
    }
    public static HttpResponseMessage Error(int status, string code) => Response(status,
        Encoding.UTF8.GetBytes($"<Error><Code>{code}</Code><Message>Synthetic fault</Message></Error>"),
        "application/xml", ("x-ms-error-code", code));

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var uri = message.RequestUri!;
        if (uri.Host is not ("storage.invalid" or "queue.invalid") || uri.Scheme != "https" ||
            uri.UserInfo.Length != 0 || message.Headers.Contains("Authorization"))
            throw new InvalidOperationException("Synthetic transport rejects external endpoints and credentials.");
        var query = uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Split('=', 2)).ToDictionary(x => Uri.UnescapeDataString(x[0]),
                x => x.Length == 2 ? Uri.UnescapeDataString(x[1]) : "");
        if (query.Keys.Any(x => x is "sig" or "sv" or "se"))
            throw new InvalidOperationException("Synthetic transport rejects SAS credentials.");
        var name = Uri.UnescapeDataString(uri.AbsolutePath).TrimStart('/');
        if (name != "fixture" && !name.StartsWith("fixture/", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected fixture path.");
        name = name.Length > 8 ? name[8..] : "";
        var headers = message.Headers.Concat(message.Content?.Headers ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>())
            .ToDictionary(x => x.Key, x => string.Join(",", x.Value), StringComparer.OrdinalIgnoreCase);
        var request = new Request(message.Method.Method, name, headers,
            message.Content is null ? [] : await message.Content.ReadAsByteArrayAsync(cancellationToken), query);
        Requests.Enqueue(request);
        if (Before is not null) await Before(request, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        HttpResponseMessage response;
        lock (_gate)
        {
            response = Fault?.Invoke(request) ?? (uri.Host == "queue.invalid" ? HandleQueue(request) : HandleBlob(request));
        }
        if (After is not null) await After(request, cancellationToken);
        return response;
    }

    private HttpResponseMessage HandleQueue(Request r)
    {
        if (r.Method == "PUT" && r.Name == "") return Response(201);
        if (r.Method == "POST" && r.Name == "messages")
        {
            var text = XDocument.Load(new MemoryStream(r.Body)).Root!.Element("MessageText")!.Value;
            QueueBodies.Enqueue(Encoding.UTF8.GetString(Convert.FromBase64String(text)));
            return Response(201, Encoding.UTF8.GetBytes("<QueueMessagesList><QueueMessage><MessageId>synthetic</MessageId>" +
                "<InsertionTime>Mon, 01 Jan 2024 00:00:00 GMT</InsertionTime><ExpirationTime>Mon, 08 Jan 2024 00:00:00 GMT</ExpirationTime>" +
                "<PopReceipt>synthetic</PopReceipt><TimeNextVisible>Mon, 01 Jan 2024 00:00:00 GMT</TimeNextVisible>" +
                "</QueueMessage></QueueMessagesList>"), "application/xml");
        }
        throw new InvalidOperationException($"Unexpected queue request {r.Method} {r.Name}");
    }

    private HttpResponseMessage HandleBlob(Request r)
    {
        if (r.Query.TryGetValue("restype", out var restype) && restype == "container")
        {
            if (r.Method == "PUT")
            {
                if (_containerCreated) return Error(409, "ContainerAlreadyExists");
                _containerCreated = true;
                return Response(201);
            }
            if (r.Method == "GET" && r.Query.GetValueOrDefault("comp") == "list")
            {
                var prefix = r.Query.GetValueOrDefault("prefix", "");
                var marker = r.Query.GetValueOrDefault("marker", "");
                var page = _blobs.Where(x => x.Key.StartsWith(prefix, StringComparison.Ordinal) &&
                        string.CompareOrdinal(x.Key, marker) > 0).OrderBy(x => x.Key, StringComparer.Ordinal)
                    .Take(PageSize + 1).ToArray();
                var xml = new XElement("EnumerationResults",
                    new XAttribute("ServiceEndpoint", "https://storage.invalid/"), new XAttribute("ContainerName", "fixture"),
                    new XElement("Prefix", prefix), new XElement("Blobs", page.Take(PageSize).Select(x =>
                        new XElement("Blob", new XElement("Name", x.Key), new XElement("Properties",
                            new XElement("Creation-Time", "Mon, 01 Jan 2024 00:00:00 GMT"),
                            new XElement("Last-Modified", "Mon, 01 Jan 2024 00:00:00 GMT"),
                            new XElement("Etag", x.Value.ETag), new XElement("Content-Length", x.Value.Body.Length),
                            new XElement("Content-Type", "application/json"), new XElement("BlobType", "BlockBlob"),
                            new XElement("LeaseStatus", "unlocked"), new XElement("LeaseState", "available"))))),
                    new XElement("NextMarker", page.Length > PageSize ? page[PageSize - 1].Key : ""));
                return Response(200, Encoding.UTF8.GetBytes(xml.ToString()), "application/xml");
            }
        }
        if (r.Query.Count > 0) throw new InvalidOperationException("Unexpected Blob operation.");
        if (r.Method == "PUT")
        {
            _blobs.TryGetValue(r.Name, out var current);
            if (r.Header("If-None-Match") == "*" && current is not null) return Error(412, "ConditionNotMet");
            if (r.Header("If-Match") is { } match && current?.ETag != match) return Error(412, "ConditionNotMet");
            if (r.Header("x-ms-blob-type") != "BlockBlob") throw new InvalidOperationException("Expected SDK BlockBlob upload.");
            var blob = new Blob(r.Body.ToArray(), $"\"{++_version}\"");
            _blobs[r.Name] = blob;
            return Response(201, null, "application/json", ("ETag", blob.ETag));
        }
        if (r.Method is "GET" or "HEAD")
        {
            if (!_blobs.TryGetValue(r.Name, out var blob)) return Error(404, "BlobNotFound");
            if (r.Header("If-Match") is { } match && blob.ETag != match) return Error(412, "ConditionNotMet");
            // SDK DownloadContent sends a range and may issue multiple range requests.
            return Response(200, r.Method == "HEAD" ? null : blob.Body, "application/json",
                ("ETag", blob.ETag), ("x-ms-blob-type", "BlockBlob"), ("x-ms-creation-time", "Mon, 01 Jan 2024 00:00:00 GMT"));
        }
        throw new InvalidOperationException($"Unexpected Blob request {r.Method} {r.Name}");
    }

    private static HttpResponseMessage Response(int status, byte[]? body = null, string type = "application/xml",
        params (string Name, string Value)[] headers)
    {
        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = new ByteArrayContent(body ?? []) };
        response.Content.Headers.ContentType = new(type);
        response.Headers.TryAddWithoutValidation("x-ms-request-id", "synthetic-request");
        response.Headers.TryAddWithoutValidation("x-ms-version", "2025-11-05");
        response.Content.Headers.LastModified = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (var (name, value) in headers) response.Headers.TryAddWithoutValidation(name, value);
        return response;
    }
}
