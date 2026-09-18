using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Rhetos;
using Rhetos.Dom.DefaultConcepts;

namespace InventoryService.Controllers;

[ApiController]
[Route("api/reservations")]
[Authorize(Roles = "event-publisher")]
public sealed class ReservationsController : ControllerBase
{
    private readonly RhetosHost _rhetosHost;
    private readonly ILogger<ReservationsController> _logger;

    public ReservationsController(RhetosHost rhetosHost, ILogger<ReservationsController> logger)
    {
        _rhetosHost = rhetosHost;
        _logger = logger;
    }

    [HttpPost]
    public IActionResult Reserve([FromBody] ReserveRequest? request)
    {
        if (request is null || request.Items is null || request.Items.Count == 0)
            return BadRequest("No items to reserve.");

        if (request.Items.Any(i => i.Quantity <= 0))
            return BadRequest("Quantities must be positive.");

        using var scope = _rhetosHost.CreateScope();
        var repository = scope.Resolve<Common.DomRepository>();

        var productIds = request.Items.Select(i => i.ProductId).ToArray();

        var snapshot = repository.Inventory.StockItem
            .Query()
            .Where(s => s.ProductID.HasValue && productIds.Contains(s.ProductID.Value) && s.Active == true)
            .Select(s => new { s.ID, ProductId = s.ProductID!.Value, s.QuantityOnHand, s.QuantityReserved })
            .ToList();

        var shortages = new List<object>();
        foreach (var line in request.Items)
        {
            var row = snapshot.FirstOrDefault(s => s.ProductId == line.ProductId);
            int available = row is null ? 0 : (row.QuantityOnHand ?? 0) - (row.QuantityReserved ?? 0);
            if (row is null || available < line.Quantity)
                shortages.Add(new { productId = line.ProductId, requested = line.Quantity, available });
        }

        if (shortages.Count > 0)
        {
            _logger.LogInformation("Reservation {ReservationId} rejected - insufficient stock.", request.ReservationId);
            return Conflict(new { status = "insufficient", shortages });
        }

        var ids = snapshot.Select(s => s.ID).ToArray();
        var items = repository.Inventory.StockItem.Load(ids);
        foreach (var line in request.Items)
        {
            var item = items.First(s => s.ProductID == line.ProductId);
            item.QuantityReserved = (item.QuantityReserved ?? 0) + line.Quantity;
        }
        repository.Inventory.StockItem.Update(items);

        scope.CommitAndClose();
        _logger.LogInformation("Reservation {ReservationId} committed - {Count} line(s).", request.ReservationId, request.Items.Count);
        return Ok(new { status = "reserved" });
    }

    public sealed record ReserveRequest
    {
        public Guid ReservationId { get; init; }
        public List<ReserveLine> Items { get; init; } = new();
    }

    public sealed record ReserveLine
    {
        public Guid ProductId { get; init; }
        public int Quantity { get; init; }
    }
}
