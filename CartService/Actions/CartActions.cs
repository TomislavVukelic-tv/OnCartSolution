using System;
using System.Linq;
using Rhetos.Dom.DefaultConcepts;
using Rhetos.Utilities;

namespace CartService
{
    public static class CartActions
    {
        public static void AddToCart(
            Cart.AddToCart parameters,
            Common.DomRepository repository,
            IUserInfo userInfo)
        {
            if (parameters.ProductID == null || parameters.ProductID == Guid.Empty)
                throw new Rhetos.UserException("ProductID is required.");
            if (parameters.Quantity == null || parameters.Quantity <= 0)
                throw new Rhetos.UserException("Quantity must be greater than zero.");

            var snapshot = repository.Cart.ProductSnapshot
                .Load(s => s.ProductID == parameters.ProductID && s.Active == true)
                .FirstOrDefault();

            if (snapshot == null)
                throw new Rhetos.UserException("This product is not available.");

            var customer = userInfo.UserName;

            if (string.Equals(customer, "guest", StringComparison.OrdinalIgnoreCase))
                throw new Rhetos.UserException("Guests cannot use a server-side cart. Sign in to check out.");

            var cart = repository.Cart.ShoppingCart
                .Load(c => c.CustomerName == customer && c.Status == "Active")
                .FirstOrDefault();

            if (cart == null)
            {
                cart = new Cart.ShoppingCart
                {
                    ID = Guid.NewGuid(),
                    CustomerName = customer,
                    Status = "Active"
                };
                repository.Cart.ShoppingCart.Insert(cart);
            }


            var line = repository.Cart.CartItem
                .Load(i => i.ShoppingCartID == cart.ID && i.ProductID == parameters.ProductID)
                .FirstOrDefault();

            if (line == null)
            {
                repository.Cart.CartItem.Insert(new Cart.CartItem
                {
                    ID = Guid.NewGuid(),
                    ShoppingCartID = cart.ID,
                    ProductID = parameters.ProductID.Value,
                    ProductName = snapshot.ProductName,
                    UnitPrice = snapshot.UnitPrice,
                    Quantity = parameters.Quantity.Value
                });
            }
            else
            {
                line.Quantity += parameters.Quantity.Value;
                line.ProductName = snapshot.ProductName;
                line.UnitPrice = snapshot.UnitPrice;
                repository.Cart.CartItem.Update(line);
            }
        }
    }
}
