using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Stripe;
using Stripe.Checkout;
using System.Diagnostics;
using System.Security.Claims;

namespace BulkyWeb.Areas.Admin.Controllers
{
	[Area("Admin")]
	[Authorize]
	public class OrderController : Controller
	{
		private readonly IUnitOfWork _unitOfWork;
		[BindProperty]
		public OrderVM OrderVM { get; set; }
        public OrderController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public IActionResult Index()
		{
			return View();
		}

		public IActionResult Details(int orderId)
		{
			OrderVM = new()
			{
				OrderHeader=_unitOfWork.OrderHeader.Get(u=>u.Id == orderId,includeProperties:"ApplicationUser"),
				OrderDetails=_unitOfWork.OrderDetail.GetAll(u=>u.OrderHeaderId==orderId,includeProperties:"Product")
			};
			return View(OrderVM);
		}

		[HttpPost]
		[Authorize(Roles =SD.Role_Admin+","+SD.Role_Employee)]
        public IActionResult UpdateOrderDetails(int orderHeaderId)
        {
			var orderHeaderFromDb = _unitOfWork.OrderHeader.Get(u => u.Id == OrderVM.OrderHeader.Id);
			orderHeaderFromDb.Name= OrderVM.OrderHeader.Name;
            orderHeaderFromDb.PhoneNumber = OrderVM.OrderHeader.PhoneNumber;
            orderHeaderFromDb.StreetAddress = OrderVM.OrderHeader.StreetAddress;
            orderHeaderFromDb.City = OrderVM.OrderHeader.City;
            orderHeaderFromDb.State = OrderVM.OrderHeader.State;
            orderHeaderFromDb.PostalCode = OrderVM.OrderHeader.PostalCode;
			if(!string.IsNullOrEmpty(OrderVM.OrderHeader.Carrier))
			{
                orderHeaderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
            }
            if (!string.IsNullOrEmpty(OrderVM.OrderHeader.TrackingNumber))
            {
                orderHeaderFromDb.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
            }
			_unitOfWork.OrderHeader.Update(orderHeaderFromDb);
			_unitOfWork.Save();

			TempData["Success"] = "Order Details Updated Successfully.";

            return RedirectToAction(nameof(Details),new {orderId=orderHeaderFromDb.Id});
        }

		[HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult StartProcessing()
        {
			_unitOfWork.OrderHeader.UpdateStatus(OrderVM.OrderHeader.Id, SD.StatusProcessing);
			_unitOfWork.Save();
            TempData["Success"] = "Order Details Updated Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderHeader.Id });
        }

		[HttpPost]
		[Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
		public IActionResult ShipOrder()
		{
			var orderHeaderFromDb = _unitOfWork.OrderHeader.Get(u => u.Id == OrderVM.OrderHeader.Id);
			orderHeaderFromDb.TrackingNumber = OrderVM.OrderHeader.TrackingNumber;
			orderHeaderFromDb.Carrier = OrderVM.OrderHeader.Carrier;
			orderHeaderFromDb.ShippingDate = DateTime.Now;
			orderHeaderFromDb.OrderStatus = SD.StatusShipped;

			if(orderHeaderFromDb.PaymentStatus == SD.PaymentStatusDelayedPayment)
			{
				orderHeaderFromDb.PaymentDueDate = DateTime.Now.AddDays(30);
			}

			_unitOfWork.OrderHeader.Update(orderHeaderFromDb);
			_unitOfWork.Save();
			TempData["Success"] = "Order Shipped Successfully.";
			return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderHeader.Id });
		}

        [HttpPost]
        [Authorize(Roles = SD.Role_Admin + "," + SD.Role_Employee)]
        public IActionResult CancelOrder()
		{
            var orderHeaderFromDb = _unitOfWork.OrderHeader.Get(u => u.Id == OrderVM.OrderHeader.Id);

			if (orderHeaderFromDb.PaymentStatus == SD.PaymentStatusApproved)
			{
				var options = new RefundCreateOptions
				{
					Reason = RefundReasons.RequestedByCustomer,
					PaymentIntent = orderHeaderFromDb.PaymentIntentId,
				};

				var service = new RefundService();
				Refund refund = service.Create(options);

				_unitOfWork.OrderHeader.UpdateStatus(orderHeaderFromDb.Id, SD.StatusCancelled, SD.StatusRefunded);
			}
			else
			{
				_unitOfWork.OrderHeader.UpdateStatus(orderHeaderFromDb.Id, SD.StatusCancelled, SD.StatusCancelled);
            }
            _unitOfWork.Save();
            TempData["Success"] = "Order Cancelled Successfully.";
            return RedirectToAction(nameof(Details), new { orderId = OrderVM.OrderHeader.Id });
        }

        [HttpPost]
		[ActionName("Details")]
        //[Authorize(Roles = SD.Role_Company)]
        public IActionResult Details_PAY_NOW()
		{
			OrderVM.OrderHeader = _unitOfWork.OrderHeader
				.Get(u => u.Id == OrderVM.OrderHeader.Id,includeProperties:"ApplicationUser");
			OrderVM.OrderDetails = _unitOfWork.OrderDetail
				.GetAll(u => u.Id == OrderVM.OrderHeader.Id,includeProperties: "Product");

            var domain = "https://localhost:44370/";
            var options = new SessionCreateOptions
            {
                SuccessUrl = domain + $"Admin/Order/PaymentConfirmation?orderHeaderId={OrderVM.OrderHeader.Id}",
                CancelUrl = domain + $"Admin/Order/Details?orderId={OrderVM.OrderHeader.Id}",
                LineItems = new List<SessionLineItemOptions>(),
                Mode = "payment",
            };

            foreach (var item in OrderVM.OrderDetails)
            {
                var sessionLineItem = new SessionLineItemOptions
                {
                    PriceData = new SessionLineItemPriceDataOptions
                    {
                        UnitAmount = (long)(item.Price * 100), // 20,50euros => 2050
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
            _unitOfWork.OrderHeader.UpdateStripePaymentId(OrderVM.OrderHeader.Id, session.Id, session.PaymentIntentId);
            _unitOfWork.Save();
            Response.Headers.Add("Location", session.Url);
            return new StatusCodeResult(303);
        }

        public IActionResult PaymentConfirmation(int orderHeaderId)
        {
            OrderHeader orderHeader = _unitOfWork.OrderHeader.Get(u => u.Id == orderHeaderId, includeProperties: "ApplicationUser");
            if (orderHeader.PaymentStatus == SD.PaymentStatusDelayedPayment)
            {
                //Order by a company
                var service = new SessionService();
                Session session = service.Get(orderHeader.SessionId);

                if (session.PaymentStatus.ToLower() == "paid")
                {
                    _unitOfWork.OrderHeader.UpdateStripePaymentId(orderHeaderId, session.Id, session.PaymentIntentId);
                    _unitOfWork.OrderHeader.UpdateStatus(orderHeaderId, orderHeader.OrderStatus, SD.PaymentStatusApproved);
                    _unitOfWork.Save();
                }
            }
            return View(orderHeaderId);
        }


        #region APICALLS
        [HttpGet]
		public IActionResult GetAll(string status)
		{
			IEnumerable<OrderHeader> objOrderHeaders;

			if(User.IsInRole(SD.Role_Admin) || User.IsInRole(SD.Role_Employee))
			{
                objOrderHeaders = _unitOfWork.OrderHeader.GetAll(includeProperties: "ApplicationUser");
            }
			else
			{
                var claimsIdentity = (ClaimsIdentity)User.Identity;
                var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;
                objOrderHeaders = _unitOfWork.OrderHeader.GetAll(u=>u.ApplicationUserId==userId
				,includeProperties: "ApplicationUser");
            }

			switch (status)
			{
				case "inprocess":
					objOrderHeaders = objOrderHeaders.Where(u=>u.OrderStatus == SD.StatusProcessing);
					break;
				case "pending":
					objOrderHeaders = objOrderHeaders.Where(u => u.PaymentStatus == SD.PaymentStatusDelayedPayment);
					break;
				case "completed":
					objOrderHeaders = objOrderHeaders.Where(u => u.OrderStatus == SD.StatusShipped);
					break;
				case "approved":
					objOrderHeaders = objOrderHeaders.Where(u => u.OrderStatus == SD.StatusApproved);
					break;
				default:
					break;
			}
			return Json(new { data = objOrderHeaders });
		}

		#endregion
	}
}
