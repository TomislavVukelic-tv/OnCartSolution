using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rhetos;
using Rhetos.Dom.DefaultConcepts;

namespace CartService.Controllers;

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
                ApplyProductCreated(repository, envelope);
                break;

            case "ProductPriceChanged":
                ApplyProductPriceChanged(repository, envelope);
                break;

            case "ProductRemoved":
                ApplyProductRemoved(repository, envelope);
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

    private void ApplyProductCreated(Common.DomRepository repository, EventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductCreatedPayload>(
            envelope.Payload ?? "{}", JsonOptions);

        if (payload is null || payload.ProductId == Guid.Empty)
        {
            _logger.LogWarning("ProductCreated {EventId} had no ProductId.", envelope.EventId);
            return;
        }

        var existing = repository.Cart.ProductSnapshot
            .Query().Where(p => p.ProductID == payload.ProductId).SingleOrDefault();

        if (existing is null)
        {
            repository.Cart.ProductSnapshot.Insert(new Cart.ProductSnapshot
            {
                ID = Guid.NewGuid(),
                ProductID = payload.ProductId,
                ProductName = payload.Name,
                UnitPrice = payload.Price,
                Currency = payload.Currency ?? "EUR",
                Active = true
            });
        }
        else
        {
            existing.ProductName = payload.Name;
            existing.UnitPrice = payload.Price;
            existing.Currency = payload.Currency ?? "EUR";
            existing.Active = true;
            repository.Cart.ProductSnapshot.Update(existing);
        }
    }

    private void ApplyProductPriceChanged(Common.DomRepository repository, EventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductPriceChangedPayload>(
            envelope.Payload ?? "{}", JsonOptions);

        if (payload is null || payload.ProductId == Guid.Empty)
        {
            _logger.LogWarning("ProductPriceChanged {EventId} had no ProductId.", envelope.EventId);
            return;
        }

        var existing = repository.Cart.ProductSnapshot
            .Query().Where(p => p.ProductID == payload.ProductId).SingleOrDefault();
        if (existing is null)
        {
            _logger.LogWarning("ProductSnapshot {ProductId} not found for ProductPriceChanged.", payload.ProductId);
            return;
        }

        existing.UnitPrice = payload.Price;
        existing.Currency = payload.Currency ?? existing.Currency;
        repository.Cart.ProductSnapshot.Update(existing);
    }

    private void ApplyProductRemoved(Common.DomRepository repository, EventEnvelope envelope)
    {
        var payload = JsonSerializer.Deserialize<ProductRemovedPayload>(
            envelope.Payload ?? "{}", JsonOptions);

        if (payload is null || payload.ProductId == Guid.Empty)
        {
            _logger.LogWarning("ProductRemoved {EventId} had no ProductId.", envelope.EventId);
            return;
        }

        var existing = repository.Cart.ProductSnapshot
            .Query().Where(p => p.ProductID == payload.ProductId && p.Active == true).SingleOrDefault();
        if (existing is null)
        {
            _logger.LogWarning("Active ProductSnapshot {ProductId} not found for ProductRemoved.", payload.ProductId);
            return;
        }

        existing.Active = false;
        repository.Cart.ProductSnapshot.Update(existing);
    }

    public sealed record EventEnvelope
    {
        public Guid EventId { get; init; }
        public string EventType { get; init; } = "";
        public Guid AggregateId { get; init; }
        public string? Payload { get; init; }
    }

    private sealed record ProductCreatedPayload
    {
        public Guid ProductId { get; init; }
        public string? Code { get; init; }
        public string Name { get; init; } = "";
        public decimal Price { get; init; }
        public string? Currency { get; init; }
    }

    private sealed record ProductPriceChangedPayload
    {
        public Guid ProductId { get; init; }
        public decimal Price { get; init; }
        public string? Currency { get; init; }
    }

    private sealed record ProductRemovedPayload
    {
        public Guid ProductId { get; init; }
    }
}
    