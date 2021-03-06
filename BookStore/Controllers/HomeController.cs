﻿using BookStore.Data;
using BookStore.Models;
using BookStore.ViewModels;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Security;

namespace BookStore.Controllers
{
    
    public class HomeController : Controller
    {
        private BookStoreContext db = new BookStoreContext();

        public ActionResult Index()
        {
            ViewData["Products"] = db.Products.ToList(); 
            ViewData["Categories"] = db.Categories.ToList();
            ViewData["Authors"] = db.Authors.ToList();
            return View();
        }

        [HttpGet]
        public ActionResult Author(int? id)
        {
            if (id == null)
            {
                return RedirectToAction("Index", "Home");
            }

            var author = db.Authors.Find(id);
            if (author == null)
            {
                return RedirectToAction("Error", new
                {
                    errorCode = "Lỗi 404."
                            ,
                    errorDetail = "Không tìm thấy Nhân dịp này"
                });
            }

            ViewData["Categories"] = db.Categories.ToList();
            ViewData["Authors"] = db.Authors.ToList();
            return View(author);
        }

        [HttpGet]
        public ActionResult DropDownUser()
        {
            var dictUser = db.Users.ToDictionary(us => us.ID, us => us.FirstName);

            List<KeyValuePair<int, string>> result = new List<KeyValuePair<int, string>>
            {
                new KeyValuePair<int, string>(0, "Hệ thống")
            };
            foreach (var child in dictUser)
            {
                result.Add(child);
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }


        [HttpGet]
        public ActionResult Error(string errorCode, string errorDetail)
        {
            ViewBag.ErrorCode = errorCode;
            ViewBag.ErrorDetail = errorDetail;
            return View();
        }
    }
}