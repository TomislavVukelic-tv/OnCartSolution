using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rhetos;
using Rhetos.Dom.DefaultConcepts;

namespace InventoryService.Controllers;


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
            case "ProductCreated":
                CreateStockItem(repository, envelope);
                break;

            case "ProductRemoved":
                RemoveStockItem(repository, envelope);
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

    private void CreateStockItem(Common.DomRepository repository, EventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductRefPayload>(
            envelope.Payload ?? "{}", JsonOptions);

        if (payload is null || payload.ProductId == Guid.Empty)
        {
            _logger.LogWarning("ProductCreated {EventId} had no ProductId.", envelope.EventId);
            return;
        }

        bool exists = repository.Inventory.StockItem
            .Query().Any(s => s.ProductID == payload.ProductId);
        if (exists)
        {
            _logger.LogInformation("StockItem for product {ProductId} already exists.", payload.ProductId);
            return;
        }

        repository.Inventory.StockItem.Insert(new Inventory.StockItem
        {
            ID = Guid.NewGuid(),
            ProductID = payload.ProductId,
            QuantityOnHand = 0,
            QuantityReserved = 0,
            ReorderThreshold = 0
        });
    }

    private void RemoveStockItem(Common.DomRepository repository, EventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductRefPayload>(
            envelope.Payload ?? "{}", JsonOptions);

        if (payload is null || payload.ProductId == Guid.Empty)
        {
            _logger.LogWarning("ProductRemoved {EventId} had no ProductId.", envelope.EventId);
            return;
        }

        var ids = repository.Inventory.StockItem
            .Query().Where(s => s.ProductID == payload.ProductId && s.Active == true)
            .Select(s => s.ID).ToArray();

        if (ids.Length == 0)
        {
            _logger.LogWarning("No active StockItem for product {ProductId} to remove.", payload.ProductId);
            return;
        }

        var items = repository.Inventory.StockItem.Load(ids);
        foreach (var item in items)
            item.Active = false;
        repository.Inventory.StockItem.Update(items);
    }

    public sealed record EventEnvelope
    {
        public Guid EventId { get; init; }
        public string EventType { get; init; } = "";
        public Guid AggregateId { get; init; }
        public string? Payload { get; init; }
    }

    private sealed record ProductRefPayload
    {
        public Guid ProductId { get; init; }
        public string? Code { get; init; }
        public string? Name { get; init; }
    }
}
