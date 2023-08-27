using Bulky.DataAccess.Data;
using Bulky.DataAccess.Repository.IRepository;
using Bulky.Models;
using Bulky.Models.ViewModels;
using Bulky.Utility;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.Collections.Generic;

namespace BulkyWeb.Areas.Admin.Controllers
{
	[Area("Admin")]
    [Authorize(Roles = SD.Role_Admin)]
    public class CompanyController : Controller
    {
        private readonly IUnitOfWork _unitOfWork;
 
        public CompanyController(IUnitOfWork unitOfWork)
        {
            _unitOfWork = unitOfWork;
        }
        public IActionResult Index()
        {
			return View();
		}

        public IActionResult Upsert(int? id)
        {
            if(id == null || id==0)
            {
                //create
                return View(new Company());
			}
            else
            {
                //update
                Company company = _unitOfWork.Company.Get(u => u.Id == id);
				return View(company);
			}
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Upsert(Company company)
        {
            if (ModelState.IsValid)
            {
                if(company.Id == 0)
                {
					_unitOfWork.Company.Add(company);
					_unitOfWork.Save();
					TempData["Success"] = "Company Created Successfully";
				}
                else
                {
                    _unitOfWork.Company.Update(company);
                    _unitOfWork.Save();
					TempData["Success"] = "Company Updated Successfully";
				}
                return RedirectToAction("Index");
            }
            else
            {
				return View(company);
			}
        }

		#region APICALLS
		[HttpGet]
        public IActionResult GetAll()
        {
			List<Company> objCompanyList = _unitOfWork.Company.GetAll().ToList();
            return Json(new { data = objCompanyList });
		}
        [HttpDelete]
		public IActionResult Delete(int? id)
		{
			var companyToBeDeleted = _unitOfWork.Company.Get(u => u.Id == id);
			if (companyToBeDeleted == null)
			{
				return Json(new { success = false, message = "Error while deleting" });
			}
			_unitOfWork.Company.Remove(companyToBeDeleted);
			_unitOfWork.Save();
			return Json(new { success = true, message = "Delete Successful" });
		}
		#endregion
	}
}
