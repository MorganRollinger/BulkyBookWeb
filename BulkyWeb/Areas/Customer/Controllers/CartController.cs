using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Stripe.Checkout;
using System.Security.Claims;

namespace BulkyWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        [BindProperty]
        public CartVM CartVM { get; set; }
        public CartController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            CartVM = new CartVM() {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product"),
                OrderHeader = new()
            };

            foreach (ShoppingCart shoppingCart in CartVM.ShoppingCartList)
            {
                CartVM.OrderHeader.OrderTotal += GetPriceBasedOnQuantity(shoppingCart);
            }

            return View(CartVM);
        }
        public IActionResult Summary()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            CartVM = new CartVM()
            {
                ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties: "Product"),
                OrderHeader = new()
            };

            foreach (ShoppingCart shoppingCart in CartVM.ShoppingCartList)
            {
                CartVM.OrderHeader.OrderTotal += GetPriceBasedOnQuantity(shoppingCart);
            }

            CartVM.OrderHeader.ApplicationUser=_unitOfWork.ApplicationUser.Get(u=>u.Id== userId);

            CartVM.OrderHeader.Name = CartVM.OrderHeader.ApplicationUser.Name;
            CartVM.OrderHeader.PhoneNumber = CartVM.OrderHeader.ApplicationUser.PhoneNumber;
            CartVM.OrderHeader.State = CartVM.OrderHeader.ApplicationUser.State;
            CartVM.OrderHeader.StreetAddress = CartVM.OrderHeader.ApplicationUser.StreetAddress;
            CartVM.OrderHeader.City = CartVM.OrderHeader.ApplicationUser.City;
            CartVM.OrderHeader.PostalCode = CartVM.OrderHeader.ApplicationUser.PostalCode;
            
            return View(CartVM);
        }

        [HttpPost]
        [ActionName("Summary")]
		public IActionResult SummaryPOST()
		{
			var claimsIdentity = (ClaimsIdentity)User.Identity;
			var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

			ApplicationUser applicationUser = _unitOfWork.ApplicationUser.Get(u => u.Id == userId);
			CartVM.OrderHeader.ApplicationUserId = userId;
			CartVM.OrderHeader.OrderDate = DateTime.Now;

			CartVM.ShoppingCartList = _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
				includeProperties: "Product");

			foreach (ShoppingCart shoppingCart in CartVM.ShoppingCartList)
			{
				CartVM.OrderHeader.OrderTotal += GetPriceBasedOnQuantity(shoppingCart);
			}

            if(applicationUser.CompanyId.GetValueOrDefault() == 0)
            {
                //It is a regular account
                CartVM.OrderHeader.PaymentStatus = SD.PaymentStatusPending;
				CartVM.OrderHeader.OrderStatus = SD.StatusPending;

			}
			else
            {
				//It is a company account
				CartVM.OrderHeader.PaymentStatus = SD.PaymentStatusDelayedPayment;
				CartVM.OrderHeader.OrderStatus = SD.StatusApproved;
			}
			_unitOfWork.OrderHeader.Add(CartVM.OrderHeader);
			_unitOfWork.Save();

			foreach (ShoppingCart item in CartVM.ShoppingCartList)
			{
				OrderDetail orderDetail = new()
				{
					OrderHeaderId = CartVM.OrderHeader.Id,
					ProductId = item.ProductId,
					Count = item.Count,
					Price = item.UnitaryPrice
				};
				_unitOfWork.OrderDetail.Add(orderDetail);
			}
			_unitOfWork.Save();

			if (applicationUser.CompanyId.GetValueOrDefault() == 0)
			{
                //It is a regular account, need to capoture payment
                //Stripe Logic
                var domain = "https://localhost:44370/";
				var options = new SessionCreateOptions
				{
					SuccessUrl = domain+$"Customer/Cart/OrderConfirmation?id={CartVM.OrderHeader.Id}",
                    CancelUrl = domain+"Customer/Cart/Index",
					LineItems = new List<SessionLineItemOptions>(),
					Mode = "payment",
				};

                foreach (var item in CartVM.ShoppingCartList)
                {
                    var sessionLineItem = new SessionLineItemOptions
                    {
                        PriceData = new SessionLineItemPriceDataOptions
                        {
                            UnitAmount = (long)(item.UnitaryPrice * 100), // 20,50euros => 2050
                            Currency = "eur",
                            ProductData = new SessionLineItemPriceDataProductDataOptions
                            {
                                Name = item.Product.Title
                            }
                        },
                        Quantity = item.Count
                    };
                    options.LineItems.Add(sessionLineItem);
                }

				var service = new SessionService();
				Session session = service.Create(options);
                _unitOfWork.OrderHeader.UpdateStripePaymentId(CartVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
                _unitOfWork.Save();
                Response.Headers.Add("Location", session.Url);
                return new StatusCodeResult(303);
			}

			return RedirectToAction(nameof(OrderConfirmation),new { id=CartVM.OrderHeader.Id });
		}

        public IActionResult OrderConfirmation(int id)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.Get(u => u.Id == id,includeProperties:"ApplicationUser");
            if(orderHeader.PaymentStatus!=SD.PaymentStatusDelayedPayment)
            {
                //Order by a customer
                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);

                if (session.PaymentStatus.ToLower() == "paid")
                {
					_unitOfWork.OrderHeader.UpdateStripePaymentId(id, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderHeader.UpdateStatus(id, SD.StatusApproved, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
				}
                HttpContext.Session.Clear();
			}

            List<ShoppingCart> cart = _unitOfWork.ShoppingCart
                .GetAll(u => u.ApplicationUserId == orderHeader.ApplicationUserId ).ToList();

            _unitOfWork.ShoppingCart.RemoveRange(cart);
            _unitOfWork.Save();

            return View(id);
        }

		public IActionResult Plus(int cartId)
        {
            ShoppingCart cart = _unitOfWork.ShoppingCart.Get(u=>u.Id== cartId);
            cart.Count++;
            _unitOfWork.ShoppingCart.Update(cart);
            _unitOfWork.Save();
            TempData["Success"] = "One Product Added";
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Minus(int cartId)
        {
            ShoppingCart cart = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId,tracked:true);
            if(cart.Count == 1)
            {
                HttpContext.Session.SetInt32(SD.SessionCart,
                    _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cart.ApplicationUserId).Count() - 1);
                _unitOfWork.ShoppingCart.Remove(cart);
				TempData["Success"] = "Product Removed";
            }
            else
            {
                cart.Count--;
                _unitOfWork.ShoppingCart.Update(cart);
                TempData["Success"] = "One Product Removed"; 
            }
            _unitOfWork.Save();
            return RedirectToAction(nameof(Index));
        }

        public IActionResult Remove(int cartId)
        {
            ShoppingCart cart = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId);
			_unitOfWork.ShoppingCart.Remove(cart);
			HttpContext.Session.SetInt32(SD.SessionCart,
                _unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == cart.ApplicationUserId).Count() - 1);

			_unitOfWork.Save();
			TempData["Success"] = "Product Removed";
            return RedirectToAction(nameof(Index));
        }

        private double GetPriceBasedOnQuantity(ShoppingCart shoppingCart)
        {
            if(shoppingCart.Count>=50)
            {
                shoppingCart.UnitaryPrice=shoppingCart.Product.Price;
            }
            else if (shoppingCart.Count>=100) 
            {
                shoppingCart.UnitaryPrice = shoppingCart.Product.Price50;
            }
            else
            {
                shoppingCart.UnitaryPrice = shoppingCart.Product.Price100;
            }
            return shoppingCart.UnitaryPrice * shoppingCart.Count;
        }
    }
}
