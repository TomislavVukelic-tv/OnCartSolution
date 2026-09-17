using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rhetos;
using Rhetos.Dom.DefaultConcepts;

namespace CatalogService.Controllers;

[ApiController]
[Route("api/events")]
[Authorize(Roles = "event-publisher")]
public sealed class EventsController : ControllerBase
{
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private readonly RhetosHost _rhetosHost;
    private readonly ILogger<EventsController> _logger;

    public EventsController(RhetosHost rhetosHost, ILogger<EventsController> logger)
    {
        _rhetosHost = rhetosHost;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Receive([FromBody] EventEnvelope? envelope)
    {
        if (envelope is null || envelope.EventId == Guid.Empty)
            return BadRequest("Missing eventId.");

        using var scope = _rhetosHost.CreateScope();
        var repository = scope.Resolve<Common.DomRepository>();

        bool alreadyHandled = repository.Messaging.ProcessedEvent
            .Query().Any(e => e.ID == envelope.EventId);
        if (alreadyHandled)
        {
            _logger.LogInformation("Event {EventId} already processed - skipping.", envelope.EventId);
            return Ok(new { status = "duplicate" });
        }

        switch (envelope.EventType)
        {
            case "StockStatusChanged":
                ApplyStockStatus(repository, envelope);
                break;
            default:
                _logger.LogWarning("Ignoring unhandled event type '{EventType}'.", envelope.EventType);
                break;
        }

        repository.Messaging.ProcessedEvent.Insert(new Messaging.ProcessedEvent
        {
            ID = envelope.EventId,
            EventType = envelope.EventType,
            ProcessedAt = DateTime.Now
        });

        scope.CommitAndClose();
        return Ok(new { status = "processed" });
    }

    private void ApplyStockStatus(Common.DomRepository repository, EventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<StockStatusPayload>(
            envelope.Payload ?? "{}", JsonOptions);

        if (payload is null || payload.ProductId == Guid.Empty)
        {
            _logger.LogWarning("StockStatusChanged {EventId} had no ProductId.", envelope.EventId);
            return;
        }

        var product = repository.Catalog.Product.Load(new[] { payload.ProductId }).SingleOrDefault();
        if (product is null)
        {
            _logger.LogWarning("Product {ProductId} not found for StockStatusChanged.", payload.ProductId);
            return;
        }

        product.StockStatus = payload.Status;
        repository.Catalog.Product.Save(null, new[] { product }, null);
    }

    public sealed record EventEnvelope
    {
        public Guid EventId { get; init; }
        public string EventType { get; init; } = "";
        public Guid AggregateId { get; init; }
        public string? Payload { get; init; }
    }

    private sealed record StockStatusPayload
    {
        public Guid ProductId { get; init; }
        public string? Status { get; init; }
        public int Available { get; init; }
    }
}