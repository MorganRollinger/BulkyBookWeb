using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace BulkyWeb.Areas.Customer.Controllers
{
    [Area("Customer")]
    [Authorize]
    public class CartController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
        public CartVM CartVM { get; set; }
        public CartController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public IActionResult Index()
        {
            var claimsIdentity = (ClaimsIdentity)User.Identity;
            var userId = claimsIdentity.FindFirst(ClaimTypes.NameIdentifier).Value;

            CartVM = new CartVM(){
                ShoppingCartList=_unitOfWork.ShoppingCart.GetAll(u => u.ApplicationUserId == userId,
                includeProperties:"Product")
            };

            foreach(ShoppingCart shoppingCart in CartVM.ShoppingCartList)
            {
                CartVM.OrderTotal += GetPriceBasedOnQuantity(shoppingCart);
            }

            return View(CartVM);
        }
        public IActionResult Summary()
        {
            return View();
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
            ShoppingCart cart = _unitOfWork.ShoppingCart.Get(u => u.Id == cartId);
            if(cart.Count == 1)
            {
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
