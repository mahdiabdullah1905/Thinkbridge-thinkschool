using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Net.Mail;
using System.Text;
using System.IO;
using System.Text.Json;

namespace BadOrderControllerDemo.Controllers
{
    // Deliberately bad OrderController for refactoring exercise
    [ApiController]
    public class OrderController : ControllerBase
    {
        private AppDbContext db;
        private ILogger<OrderController> logger;

        public OrderController(AppDbContext _db, ILogger<OrderController> _logger)
        {
            db = _db;
            logger = _logger;
        }

        [HttpPost]
        [Route("/api/orders")]
        public async Task<object> PostOrder([FromBody] CreateOrderDto req)
        {
            logger.LogInformation("Starting order creation");

            if (req != null)
            {
                if (req.CustomerId > 0)
                {
                    // Sync DB query inside async method
                    var cust = db.Customers.FirstOrDefault(c => c.Id == req.CustomerId);
                    
                    if (cust != null)
                    {
                        // Magic number: 1 = Active, 2 = Suspended, 3 = Deleted
                        if (cust.Status == 1)
                        {
                            // Null dereference bug: req.ShippingDetails could be null
                            string zip = req.ShippingDetails.ZipCode;

                            // Duplicated validation code for Shipping
                            if (string.IsNullOrEmpty(req.ShippingDetails.AddressLine1))
                            {
                                logger.LogError("AddressLine1 is missing");
                                return new { error = "Shipping AddressLine1 is required", code = 400 };
                            }
                            if (string.IsNullOrEmpty(req.ShippingDetails.City))
                            {
                                logger.LogError("City is missing");
                                return new { error = "Shipping City is required", code = 400 };
                            }
                            if (string.IsNullOrEmpty(req.ShippingDetails.Country))
                            {
                                logger.LogError("Country is missing");
                                return new { error = "Shipping Country is required", code = 400 };
                            }

                            // Duplicated validation code for Billing
                            if (string.IsNullOrEmpty(req.BillingDetails.AddressLine1))
                            {
                                logger.LogError("AddressLine1 is missing");
                                return new { error = "Billing AddressLine1 is required", code = 400 };
                            }
                            if (string.IsNullOrEmpty(req.BillingDetails.City))
                            {
                                logger.LogError("City is missing");
                                return new { error = "Billing City is required", code = 400 };
                            }
                            if (string.IsNullOrEmpty(req.BillingDetails.Country))
                            {
                                logger.LogError("Country is missing");
                                return new { error = "Billing Country is required", code = 400 };
                            }

                            // Check credit limit
                            decimal currentBalance = db.Orders
                                .Where(o => o.CustomerId == cust.Id && o.PaymentStatus != "PAID")
                                .Sum(o => o.TotalAmount); // Sync EF query

                            decimal orderTotal = 0;
                            decimal taxTotal = 0;
                            decimal discountTotal = 0;
                            List<OrderLineItem> itemsToSave = new List<OrderLineItem>();

                            // Off-by-one bug here: i <= req.Items.Count
                            for (int i = 0; i <= req.Items.Count; i++)
                            {
                                try
                                {
                                    var itemDto = req.Items[i];
                                    
                                    // Repeated DB queries inside a loop
                                    var product = db.Products.FirstOrDefault(p => p.Id == itemDto.ProductId);

                                    if (product != null)
                                    {
                                        if (product.IsActive) // Boolean check
                                        {
                                            if (product.Stock >= itemDto.Quantity)
                                            {
                                                // Business rules inside controller
                                                decimal linePrice = product.Price * itemDto.Quantity;
                                                decimal lineTax = 0;

                                                // Magic strings for category
                                                if (product.CategoryCode == "ELEC")
                                                {
                                                    lineTax = linePrice * 0.18m;
                                                }
                                                else if (product.CategoryCode == "FOOD")
                                                {
                                                    lineTax = linePrice * 0.05m;
                                                }
                                                else if (product.CategoryCode == "BOOK")
                                                {
                                                    lineTax = 0;
                                                }
                                                else
                                                {
                                                    lineTax = linePrice * 0.1m;
                                                }

                                                orderTotal += linePrice;
                                                taxTotal += lineTax;

                                                // Update stock directly in controller
                                                product.Stock -= itemDto.Quantity;
                                                
                                                var lineItem = new OrderLineItem();
                                                lineItem.ProductId = product.Id;
                                                lineItem.Quantity = itemDto.Quantity;
                                                lineItem.UnitPrice = product.Price;
                                                lineItem.Tax = lineTax;
                                                
                                                itemsToSave.Add(lineItem);
                                            }
                                            else
                                            {
                                                return new { error = "Not enough stock for product " + product.Name };
                                            }
                                        }
                                        else
                                        {
                                            return new { error = "Product is not active" };
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    // Empty catch block 1
                                    // Swallows the IndexOutOfRangeException from the off-by-one bug
                                }
                            }

                            decimal grandTotal = orderTotal + taxTotal;

                            // More business logic - discount calculation
                            if (grandTotal > 500)
                            {
                                try
                                {
                                    // Complex nested if for discounts
                                    if (cust.CustomerType == "VIP")
                                    {
                                        discountTotal = grandTotal * 0.10m;
                                    }
                                    else
                                    {
                                        if (cust.RegistrationDate < DateTime.Now.AddYears(-5))
                                        {
                                            discountTotal = grandTotal * 0.05m;
                                        }
                                    }
                                }
                                catch
                                {
                                    // Empty catch block 2
                                }
                            }

                            grandTotal -= discountTotal;

                            // Check against credit limit
                            if ((currentBalance + grandTotal) > cust.CreditLimit)
                            {
                                return new { status = "failed", message = "Credit limit exceeded" };
                            }

                            var newOrder = new Order();
                            newOrder.CustomerId = cust.Id;
                            newOrder.OrderDate = DateTime.Now; // Poor testability, direct DateTime.Now
                            newOrder.SubTotal = orderTotal;
                            newOrder.TaxAmount = taxTotal;
                            newOrder.DiscountAmount = discountTotal;
                            newOrder.TotalAmount = grandTotal;
                            newOrder.Status = "PENDING";
                            newOrder.PaymentStatus = "UNPAID";
                            newOrder.ShippingAddress = req.ShippingDetails.AddressLine1 + ", " + req.ShippingDetails.City + ", " + req.ShippingDetails.ZipCode;

                            // Save order header
                            db.Orders.Add(newOrder);
                            db.SaveChanges(); // Sync save

                            // Save line items
                            foreach (var li in itemsToSave)
                            {
                                li.OrderId = newOrder.Id;
                                db.OrderLineItems.Add(li);
                            }
                            db.SaveChanges(); // Another sync save

                            // Audit log creation
                            try
                            {
                                var audit = new AuditRecord();
                                audit.Action = "OrderCreated";
                                audit.UserId = cust.Id;
                                audit.Timestamp = DateTime.UtcNow;
                                audit.Details = "Order " + newOrder.Id + " created for amount " + grandTotal;
                                db.AuditRecords.Add(audit);
                                db.SaveChanges(); // Yet another sync save
                            }
                            catch (Exception)
                            {
                                // Empty catch block 3
                            }

                            // Send confirmation email
                            try
                            {
                                MailMessage mail = new MailMessage();
                                mail.From = new MailAddress("sales@company.com");
                                mail.To.Add(cust.Email);
                                mail.Subject = "Order Confirmation - " + newOrder.Id;
                                mail.Body = "Dear " + cust.Name + ", your order has been received. Total: $" + grandTotal;
                                
                                SmtpClient smtp = new SmtpClient("smtp.company.local");
                                smtp.Port = 25;
                                smtp.Send(mail); // Synchronous network call
                            }
                            catch
                            {
                                // Empty catch block 4
                            }

                            // Return anonymous object instead of typed response
                            return new 
                            { 
                                success = true, 
                                orderId = newOrder.Id, 
                                orderDate = newOrder.OrderDate,
                                total = grandTotal 
                            };
                        }
                        else
                        {
                            return new { success = false, message = "Customer account is not active" };
                        }
                    }
                    else
                    {
                        return new { success = false, message = "Customer not found" };
                    }
                }
                else
                {
                    return new { success = false, message = "Invalid customer ID" };
                }
            }
            else
            {
                return new { success = false, message = "Request payload is null" };
            }
        }
    }

    // ----------------------------------------------------------------------
    // The classes below are included just to make the file self-contained
    // and realistically simulate a large file where entities and DTOs 
    // are improperly dumped in the same file as the controller.
    // ----------------------------------------------------------------------

    public class CreateOrderDto
    {
        public int CustomerId { get; set; }
        public AddressDto ShippingDetails { get; set; }
        public AddressDto BillingDetails { get; set; }
        public List<OrderItemDto> Items { get; set; }
    }

    public class AddressDto
    {
        public string AddressLine1 { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string ZipCode { get; set; }
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class AppDbContext : DbContext
    {
        public DbSet<Customer> Customers { get; set; }
        public DbSet<Product> Products { get; set; }
        public DbSet<Order> Orders { get; set; }
        public DbSet<OrderLineItem> OrderLineItems { get; set; }
        public DbSet<AuditRecord> AuditRecords { get; set; }
    }

    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Email { get; set; }
        public int Status { get; set; }
        public string CustomerType { get; set; }
        public decimal CreditLimit { get; set; }
        public DateTime RegistrationDate { get; set; }
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public string CategoryCode { get; set; }
        public bool IsActive { get; set; }
    }

    public class Order
    {
        public int Id { get; set; }
        public int CustomerId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal SubTotal { get; set; }
        public decimal TaxAmount { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TotalAmount { get; set; }
        public string Status { get; set; }
        public string PaymentStatus { get; set; }
        public string ShippingAddress { get; set; }
    }

    public class OrderLineItem
    {
        public int Id { get; set; }
        public int OrderId { get; set; }
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Tax { get; set; }
    }

    public class AuditRecord
    {
        public int Id { get; set; }
        public string Action { get; set; }
        public int UserId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Details { get; set; }
    }
}
