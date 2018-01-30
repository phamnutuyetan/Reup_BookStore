using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using BookStore.Models;
using Newtonsoft.Json;
using BookStore.Data;
using System.Data.Entity;
using System.Web.Security;
using Rotativa;

namespace BookStore.Controllers
{

    public class OrderController : Controller
    {
        BookStore.Data.BookStoreContext db = new Data.BookStoreContext();
        JsonSerializerSettings serializerSettings = new JsonSerializerSettings()
        {
            ReferenceLoopHandling = Newtonsoft.Json.ReferenceLoopHandling.Ignore
        };

        [HttpGet]
        public ActionResult New()
        {
            if (Request.Cookies["order_id"] != null)
            {
                Response.Cookies["order_id"].Expires = DateTime.Now.AddDays(-1);
            }

            return RedirectToAction("Cashier", "Staff");
        }


        #region list of orders
        [HttpGet]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult GetOrders()
        {
            IQueryable<Order> orders;
            if (!User.IsInRole("Admin"))
            {
                //orders = db.Orders.Where(o => o.Status == OrderStatus.Pending);

                int userId = db.Users.Where(u => u.Username == User.Identity.Name).FirstOrDefault().ID;

                orders = (from childs in db.Orders
                               where ((childs.Status == OrderStatus.Pending || childs.Status == OrderStatus.Packing) && (childs.AnonymousUserId != null))
                               || ((childs.UserId != null) && (childs.UserId == userId) && (childs.Status == OrderStatus.New))
                               select childs);
            }
            else
            {
                orders = db.Orders;
            }
            
            var result = new JsonNetResult
            {
                Data = orders.ToList(),
                JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                Settings = {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 2
                }
            };

            return result;
        }

        [HttpPost]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult FilterOrders(int userId, int state)
        {
            if (!User.IsInRole("Admin"))
            {
                return RedirectToAction("Error", "Staff"
                        , new
                        {
                            errorCode = "Lỗi 403."
                            ,
                            errorDetail = "Bạn không có quyền"
                        });
            }

            IQueryable<Order> orders;

            orders = db.Orders;
            if(orders == null)// no orders
            {
                return null;
            }

            if(userId != -1)
            {
                if(userId == 0)// filtered by "no user"
                {
                    orders = orders.Where(od => od.UserId == null);
                }
                else
                {
                    orders = orders.Where(od => od.UserId == userId);
                }
            }

            if(state != -1)
            {
                orders = orders.Where(od => od.Status == (OrderStatus)state);
            }

            //var converted = JsonConvert.SerializeObject(orders, Formatting.Indented, serializerSettings);
            //return Content(converted, "json");

            var result = new JsonNetResult
            {
                Data = orders.ToList(),
                JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                Settings = {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 2
                }
            };

            return result;
        }
        #endregion

        #region single order
        // /Order/DeleteOrder/6969
        [HttpPost]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult DeleteOrder(int Id)
        {
            ResultWeb result = new ResultWeb();


            if (!User.IsInRole("Admin"))
            {
                result.Type = ResultWeb.ResultType.ACCESS_VIOLENCE;
                return Json(result, JsonRequestBehavior.AllowGet);
            }

            if (ModelState.IsValid)
            {
                var detailsBelongTheOrder = db.OrderDetails.Where(od => od.OrderId == Id);
                db.OrderDetails.RemoveRange(detailsBelongTheOrder);
                db.SaveChanges();

                Order order = db.Orders.Find(Id);

                if(order == null)
                {
                    result.Type = ResultWeb.ResultType.FIELD_INVALID;
                    return Json(result, JsonRequestBehavior.AllowGet);
                }

                int cookieId;
                if (Request.Cookies["order_id"] != null)
                {
                    cookieId = int.Parse(Request.Cookies["order_id"].Value);
                    if(cookieId == Id)
                    {
                        Response.Cookies["order_id"].Expires = DateTime.Now.AddDays(-1);
                    }
                }               

                db.Orders.Remove(order);
                db.SaveChanges();

                result.Type = ResultWeb.ResultType.OK_DELETE;
            }
            else
            {
                result.Type = ResultWeb.ResultType.FIELD_INVALID;
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpGet]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult GetOrder(int id)
        {
            var order = db.Orders.Find(id);

            //var converted = JsonConvert.SerializeObject(order, Formatting.None, serializerSettings);
            //return Content(converted, "json");


            var result = new JsonNetResult
            {
                Data = order,
                JsonRequestBehavior = JsonRequestBehavior.AllowGet,
                Settings = {
            ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
            MaxDepth = 3
                }
            };

            return result;
        }

        [HttpPost]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult DeleteOrderDetail(int id)
        {
            ResultWeb result = new ResultWeb();
            if (ModelState.IsValid)
            {
                var detail = db.OrderDetails.Find(id);

                if (detail == null)
                {
                    result.Type = ResultWeb.ResultType.FIELD_INVALID;
                    return Json(result, JsonRequestBehavior.AllowGet);
                }
                int orderId = detail.OrderId;

                db.OrderDetails.Remove(detail);
                db.SaveChanges();

                RecalculateOrderCost(orderId);

                result.Type = ResultWeb.ResultType.OK_DELETE;
            }
            else
            {
                result.Type = ResultWeb.ResultType.FIELD_INVALID;
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }



        [HttpPost]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult AddLine(int orderId, int proId, int count)
        {
            ResultWeb result = new ResultWeb();
            Product product = db.Products.Find(proId);
            if(product == null)
            {
                result.Type = ResultWeb.ResultType.FIELD_INVALID;
                return Json(result, JsonRequestBehavior.AllowGet);
            }
            
            var details = db.OrderDetails.Where(od => od.OrderId == orderId);

            OrderDetail line = details.Where(od => od.ProductId == proId).FirstOrDefault();
            if(line == null)
            {
                line = new OrderDetail
                {
                    OrderId = orderId,
                    ProductId = proId
                };

                if(count > product.InStock)
                {
                    line.Quantity = product.InStock;
                    result.Type = ResultWeb.ResultType.OUT_OF_STOCK;
                }
                else
                {
                    line.Quantity = count;
                    result.Type = ResultWeb.ResultType.OK_ADD;
                }
                line.TotalAmount = product.Price * line.Quantity;

                db.OrderDetails.Add(line);
                db.SaveChanges();
            }
            else
            {
                line.Quantity += count;
                if (line.Quantity > product.InStock)
                {
                    line.Quantity = product.InStock;
                    result.Type = ResultWeb.ResultType.OUT_OF_STOCK;
                }
                else
                {
                    result.Type = ResultWeb.ResultType.OK_ADD;
                }

                line.TotalAmount = product.Price * line.Quantity;
                db.Entry(line).State = EntityState.Modified;
                db.SaveChanges();
            }
            
            RecalculateOrderCost(orderId);
            
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [HttpPost]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult UpdateLine(int detailId, int proId, int count)
        {
            ResultWeb result = new ResultWeb();
            Product product = db.Products.Find(proId);
            if (product == null)
            {
                result.Type = ResultWeb.ResultType.FIELD_INVALID;
                return Json(result, JsonRequestBehavior.AllowGet);
            }

            OrderDetail detail = db.OrderDetails.Find(detailId);
            if (detail == null)
            {
                result.Type = ResultWeb.ResultType.FIELD_INVALID;
                return Json(result, JsonRequestBehavior.AllowGet);
            }

            detail.ProductId = proId;
            if (count > product.InStock)
            {
                detail.Quantity = product.InStock;
                result.Type = ResultWeb.ResultType.OUT_OF_STOCK;
            }
            else
            {
                detail.Quantity = count;
                result.Type = ResultWeb.ResultType.OK_ADD;
            }

            detail.TotalAmount = product.Price * detail.Quantity;

            db.Entry(detail).State = EntityState.Modified;
            db.SaveChanges();

            PreventDetailDuplicate(detail.OrderId, proId);

            RecalculateOrderCost(detail.OrderId);

            result.Type = ResultWeb.ResultType.OK_UPDATE;
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// prevent an order has duplicate product lines of an product
        /// <para> when you update a line, change its product, there may have duplicate </para>
        /// </summary>
        private void PreventDetailDuplicate(int orderId, int proId)
        {
            Product product = db.Products.Find(proId);
            if(product == null)
            {
                throw new NullReferenceException();
            }

            var details = (from childs in db.OrderDetails
                          where (childs.OrderId == orderId) && (childs.ProductId == proId)
                          select childs).ToList();

            if(details.Count > 1)// yes, there are duplicate product line in the order
            {
                OrderDetail lineFirst = details[0];
                details.RemoveAt(0);
                foreach (OrderDetail lineFollow in details)
                {
                    lineFirst.Quantity += lineFollow.Quantity;
                }

                if(lineFirst.Quantity > product.InStock)
                {
                    lineFirst.Quantity = product.InStock;
                }
                lineFirst.TotalAmount = lineFirst.Quantity * product.Price;
                db.Entry(lineFirst).State = EntityState.Modified;
                db.OrderDetails.RemoveRange(details);
                db.SaveChanges();
            }
        }

        /// <summary>
        /// Re-calculate the cost of the order
        /// <para> call this after you modified the order, or the order-details
        /// </para>
        /// </summary>
        /// <param name="id">of the order</param>
        private void RecalculateOrderCost(int id)
        {
            Order order = db.Orders.Find(id);
            double total = 0;
            foreach (OrderDetail aLine in order.OrderDetails)
            {
                total += aLine.TotalAmount;
            }
            order.TotalAmount = total;
            db.Entry(order).State = EntityState.Modified;
            db.SaveChanges();
        }
        #endregion

        [HttpGet]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult DropDownOrderState()
        {
            //int[] itemValues = System.Enum.GetValues(typeof(OrderStatus)) as int[];
            //string[] itemNames = System.Enum.GetNames(typeof(OrderStatus));

            //var dict = new Dictionary<int, string>();
            //for (int i = 0; i <= itemNames.Length - 1; i++)
            //{
            //    dict[itemValues[i]] = itemNames[i];
            //}



            // nah, I hate to hard code this
            Dictionary<int, string> dict;

            if (User.IsInRole("Admin"))
            {
                dict = new Dictionary<int, string>
                {
                    [0] = "Mới tạo",
                    [1] = "Đã đóng gói",
                    [2] = "Đang giao",
                    [3] = "Hoàn tất",
                    [4] = "Chờ duyệt",
                    [5] = "Đã bị huỷ"
                };
            }
            else
            {
                dict = new Dictionary<int, string>
                {
                    [0] = "Mới tạo",
                    [4] = "Chờ duyệt",
                };
            }

            var list = dict.ToList();

            return Json(list, JsonRequestBehavior.AllowGet);
        }

        /// <summary>
        /// Checkout an order
        /// <para> e.g. The customer paid for their order </para>
        /// </summary>
        /// <param name="id"> order id </param>
        /// <returns></returns>
        [HttpPost]
        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult Checkout(int id)
        {
            ResultWeb result = new ResultWeb();
            Order order = db.Orders.Find(id);
            if(order == null)
            {
                result.Type = ResultWeb.ResultType.FIELD_INVALID;
                return Json(result, JsonRequestBehavior.AllowGet);
            }
            switch (order.Status)
            {
                case OrderStatus.New:
                    {
                        SubProduct(order);
                        order.Status = OrderStatus.Completed;
                        db.Entry(order).State = EntityState.Modified;
                        db.SaveChanges();
                        result.Type = ResultWeb.ResultType.OK;
                    }
                    break;
                case OrderStatus.Packing:
                    {
                        order.Status = OrderStatus.Delivering;
                        db.Entry(order).State = EntityState.Modified;
                        db.SaveChanges();
                        result.Type = ResultWeb.ResultType.OK;
                    }
                    break;
                case OrderStatus.Delivering:
                    {
                        order.DeliveryDate = DateTime.Today;
                        order.Status = OrderStatus.Completed;
                        db.Entry(order).State = EntityState.Modified;
                        db.SaveChanges();
                        result.Type = ResultWeb.ResultType.OK;
                    }
                    break;
                case OrderStatus.Pending:
                    {
                        SubProduct(order);
                        order.Status = OrderStatus.Packing;
                        db.Entry(order).State = EntityState.Modified;
                        db.SaveChanges();
                        result.Type = ResultWeb.ResultType.OK;
                    }
                    break;
            }

            int cookieId;
            if (Request.Cookies["order_id"] != null)
            {
                cookieId = int.Parse(Request.Cookies["order_id"].Value);
                if (cookieId == id)
                {
                    
                    if(order.AnonymousUserId != null)
                    {// this mean: this is a order from online shop
                        Order aPendingOrder =
                            db.Orders.Where(o =>
                            o.Status == OrderStatus.Pending).FirstOrDefault();

                        if(aPendingOrder != null)
                        {
                            Response.Cookies["order_id"].Value = aPendingOrder.ID.ToString();
                            Response.Cookies["order_id"].Expires = DateTime.Now.AddDays(7);
                        }
                        else
                        {
                            Response.Cookies["order_id"].Expires = DateTime.Now.AddDays(-1);
                        }
                    }
                    else
                    {
                        Response.Cookies["order_id"].Expires = DateTime.Now.AddDays(-1);
                    }
                }
            }

            return Json(result, JsonRequestBehavior.AllowGet);
        }

        private bool SubProduct(Order order)
        {
            foreach (var detail in order.OrderDetails)
            {
                detail.Product.InStock -= detail.Quantity;
                db.Entry(detail.Product).State = System.Data.Entity.EntityState.Modified;
            }
            
            db.Entry(order).State = System.Data.Entity.EntityState.Modified;
            db.SaveChanges();

            return true;
        }

        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult UpdateState(int orderId, int state)
        {
            if(!User.IsInRole("Admin"))
            {
                return Json(
                    new ResultWeb {Type = ResultWeb.ResultType.ACCESS_VIOLENCE }
                    , JsonRequestBehavior.AllowGet);
            }

            ResultWeb result = new ResultWeb();

            Order order = db.Orders.Find(orderId);
            if(order == null)
            {
                result.Type = ResultWeb.ResultType.NOT_FOUND;
                return Json(result, JsonRequestBehavior.AllowGet);
            }

            // need: check authorize

            order.Status = (OrderStatus)state;
            db.Entry(order).State = EntityState.Modified;
            db.SaveChanges();

            result.Type = ResultWeb.ResultType.OK_UPDATE;
            return Json(result, JsonRequestBehavior.AllowGet);
        }

        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult Export(int? id)
        {
            Order order;

            if (id != null)
            {
                order = db.Orders.Find(id);
                if (order == null)
                {
                    return RedirectToAction("Error", "Staff"
                        , new
                        {
                            errorCode = "Lỗi 404."
                            ,
                            errorDetail = "Không tìm thấy đơn hàng với id: " + id.ToString()
                        });
                }

                if (!User.IsInRole("Admin"))
                {
                    int userId = db.Users.Where(u => u.Username == User.Identity.Name).FirstOrDefault().ID;
                    if (!(order.Status == OrderStatus.New && (order.UserId != null && order.UserId == userId))
                        && order.Status != OrderStatus.Pending
                        && order.Status != OrderStatus.Packing)
                    {
                        return RedirectToAction("Error", "Staff"
                        , new
                        {
                            errorCode = "Lỗi 403."
                            ,
                            errorDetail = "Bạn không có quyền truy cập đơn hàng này"
                        });
                    }
                }

                // ------------------------ this is ok -------------------
                return new ActionAsPdf("PrintPage", order);
            }
            else
            {
                return RedirectToAction("Error", "Staff"
                        , new
                        {
                            errorCode = "Lỗi 404."
                            ,
                            errorDetail = "Không xác định đơn hàng để in"
                        });
            }
        }

        [Authorize(Roles = "Admin,ThuNgan")]
        public ActionResult PrintPage(int id)
        {
            Order order = db.Orders.Find(id);
            if (order == null)
            {
                return RedirectToAction("Error", "Staff"
                    , new
                    {
                        errorCode = "Lỗi 404."
                        ,
                        errorDetail = "Không tìm thấy đơn hàng với id: " + id.ToString()
                    });
            }

            ViewBag.Name = "Shop Hoa Online";
            ViewBag.Title = "Hoá đơn bán hàng";
            ViewBag.Address = "Xa lộ Hà Nội, phường Linh Trung, quận Thủ Đức, thành phố Hồ Chí Minh";
            return View(order);
        }
    }
}