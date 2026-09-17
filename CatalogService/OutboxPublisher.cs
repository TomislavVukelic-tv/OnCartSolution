using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;
using Rhetos;

namespace CatalogService;

public sealed class OutboxPublisher : BackgroundService
{
    private readonly RhetosHost _rhetosHost;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<OutboxPublisher> _logger;

    public OutboxPublisher(
        RhetosHost rhetosHost,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<OutboxPublisher> logger)
    {
        _rhetosHost = rhetosHost;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollSeconds = _configuration.GetValue<int?>("Outbox:PollSeconds") ?? 10;
        var batchSize = _configuration.GetValue<int?>("Outbox:BatchSize") ?? 50;

        var subscribers = _configuration
            .GetSection("Outbox:Subscribers")
            .Get<Dictionary<string, List<string>>>()
            ?? new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(pollSeconds));

        do
        {
            try
            {
                await PublishPendingAsync(subscribers, batchSize, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Outbox publish cycle failed.");
            }
        }
        while (await WaitForNextTickAsync(timer, stoppingToken));
    }

    private static async Task<bool> WaitForNextTickAsync(PeriodicTimer timer, CancellationToken token)
    {
        try { return await timer.WaitForNextTickAsync(token); }
        catch (OperationCanceledException) { return false; }
    }

    private async Task PublishPendingAsync(
        Dictionary<string, List<string>> subscribers, int batchSize, CancellationToken token)
    {
        List<PendingEvent> pending;
        using (var scope = _rhetosHost.CreateScope())
        {
            var repository = scope.Resolve<Common.DomRepository>();
            pending = repository.Messaging.OutboxEvent.Query()
                .Where(e => e.Processed != true)
                .OrderBy(e => e.CreatedAt)
                .Take(batchSize)
                .Select(e => new PendingEvent
                {
                    Id = e.ID,
                    EventType = e.EventType,
                    AggregateId = e.AggregateId!.Value,
                    Payload = e.Payload
                })
                .ToList();
        }

        if (pending.Count == 0)
            return;

        var client = _httpClientFactory.CreateClient("events");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", GenerateServiceToken());

        var outcomes = new Dictionary<Guid, string?>();
        foreach (var evt in pending)
        {
            if (token.IsCancellationRequested)
                break;

            if (!subscribers.TryGetValue(evt.EventType, out var targets) || targets.Count == 0)
            {
                outcomes[evt.Id] = null;
                continue;
            }

            var envelope = JsonSerializer.Serialize(new
            {
                eventId = evt.Id,
                eventType = evt.EventType,
                aggregateId = evt.AggregateId,
                payload = evt.Payload
            });


            var errors = new List<string>();
            foreach (var target in targets)
            {
                if (token.IsCancellationRequested)
                    break;

                try
                {
                    using var content = new StringContent(envelope, Encoding.UTF8, "application/json");
                    using var response = await client.PostAsync(target, content, token);

                    if (!response.IsSuccessStatusCode)
                        errors.Add($"{target} -> HTTP {(int)response.StatusCode}");
                }
                catch (Exception ex)
                {
                    var msg = ex.Message.Length > 128 ? ex.Message[..128] : ex.Message;
                    errors.Add($"{target} -> {msg}");
                }
            }

            if (errors.Count == 0)
            {
                outcomes[evt.Id] = null;
            }
            else
            {
                var joined = string.Join("; ", errors);
                outcomes[evt.Id] = joined.Length > 256 ? joined[..256] : joined;
            }
        }

        var ids = outcomes.Keys.ToArray();
        using (var scope = _rhetosHost.CreateScope())
        {
            var repository = scope.Resolve<Common.DomRepository>();
            var items = repository.Messaging.OutboxEvent.Load(ids);

            foreach (var item in items)
            {
                var error = outcomes[item.ID];
                if (error is null)
                {
                    item.Processed = true;
                    item.ProcessedAt = DateTime.Now;
                    item.PublishError = null;
                }
                else
                {
                    item.PublishError = error;
                }
            }

            repository.Messaging.OutboxEvent.Save(null, items, null);
            scope.CommitAndClose();
        }

        var delivered = outcomes.Values.Count(e => e is null);
        _logger.LogInformation(
            "Outbox: delivered {Delivered}/{Total} event(s).", delivered, pending.Count);
    }

    private string GenerateServiceToken()
    {
        var signingKey = _configuration["Jwt:SigningKey"]
            ?? throw new InvalidOperationException("Jwt:SigningKey is not set.");

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey)),
            SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "catalog-publisher"),
            new Claim(ClaimTypes.Role, "event-publisher")
        };

        var jwt = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: DateTime.UtcNow.AddMinutes(5),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    private sealed class PendingEvent
    {
        public Guid Id { get; init; }
        public string EventType { get; init; } = "";
        public Guid AggregateId { get; init; }
        public string? Payload { get; init; }
    }
}
