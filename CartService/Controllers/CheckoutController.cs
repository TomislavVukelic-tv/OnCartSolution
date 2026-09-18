using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Rhetos;
using Rhetos.Dom.DefaultConcepts;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text;

namespace CartService.Controllers;

[ApiController]
[Route("api/checkout")]
[Authorize]
public sealed class CheckoutController : ControllerBase
{
    private readonly RhetosHost _rhetosHost;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<CheckoutController> _logger;

    public CheckoutController(
        RhetosHost rhetosHost,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<CheckoutController> logger)
    {
        _rhetosHost = rhetosHost;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> Checkout(CancellationToken cancellationToken)
    {
        var customer = User.Identity?.Name;
        if (string.IsNullOrEmpty(customer))
            return Unauthorized("No customer identity on the token.");

        if (string.Equals(customer, "guest", StringComparison.OrdinalIgnoreCase))
            return Forbid();

        Guid cartId;
        List<ReserveLine> lines;
        using (var scope = _rhetosHost.CreateScope())
        {
            var repository = scope.Resolve<Common.DomRepository>();

            var cart = repository.Cart.ShoppingCart
                .Query()
                .Where(c => c.CustomerName == customer && c.Status == "Active")
                .Select(c => new { c.ID })
                .FirstOrDefault();

            if (cart is null)
                return BadRequest("There is no active cart to check out.");

            cartId = cart.ID;

            lines = repository.Cart.CartItem
                .Query()
                .Where(i => i.ShoppingCartID == cartId)
                .Select(i => new ReserveLine { ProductId = i.ProductID!.Value, Quantity = i.Quantity!.Value })
                .ToList();
        }

        if (lines.Count == 0)
            return BadRequest("Cannot check out an empty cart.");

        var reserveUrl = _configuration["Inventory:ReserveUrl"]
            ?? throw new InvalidOperationException("Inventory:ReserveUrl is not set.");

        var client = _httpClientFactory.CreateClient("inventory");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", ServiceTokenFactory.GenerateServiceToken(_configuration));

        var reserveBody = System.Text.Json.JsonSerializer.Serialize(new
        {
            reservationId = cartId,
            items = lines
        });

        HttpResponseMessage response;
        try
        {
            using var content = new StringContent(reserveBody, Encoding.UTF8, "application/json");
            response = await client.PostAsync(reserveUrl, content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Checkout {CartId}: reservation call failed.", cartId);
            return StatusCode(StatusCodes.Status502BadGateway, "Could not reach the inventory service to reserve stock.");
        }

        if (response.StatusCode == System.Net.HttpStatusCode.Conflict)
        {
            var conflict = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Checkout {CartId}: insufficient stock.", cartId);
            return Conflict(conflict);
        }

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError("Checkout {CartId}: reserve returned HTTP {Status}.", cartId, (int)response.StatusCode);
            return StatusCode(StatusCodes.Status502BadGateway, "Inventory rejected the reservation.");
        }

        using (var scope = _rhetosHost.CreateScope())
        {
            var repository = scope.Resolve<Common.DomRepository>();
            var cart = repository.Cart.ShoppingCart.Load(new[] { cartId }).FirstOrDefault();

            if (cart is null || cart.Status != "Active")
                return Conflict("Cart is no longer active.");

            cart.Status = "CheckedOut";
            cart.CheckedOutAt = DateTime.Now;
            repository.Cart.ShoppingCart.Update(cart);

            scope.CommitAndClose();
        }

        _logger.LogInformation("Checkout {CartId} completed.", cartId);
        return Ok(new { status = "checkedout", cartId });
    }

    public sealed record ReserveLine
    {
        public Guid ProductId { get; init; }
        public int Quantity { get; init; }
    }
}
