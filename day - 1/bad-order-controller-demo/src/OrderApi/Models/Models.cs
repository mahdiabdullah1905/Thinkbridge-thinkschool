using System;
using System.Collections.Generic;

namespace OrderApi.Models
{
    public class Customer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public CustomerStatus Status { get; set; }
        public CustomerType CustomerType { get; set; }
        public decimal CreditLimit { get; set; }
        public DateTime RegistrationDate { get; set; }
    }

    public enum CustomerStatus
    {
        Inactive = 0,
        Active = 1,
        Suspended = 2,
        Deleted = 3
    }

    public enum CustomerType
    {
        Regular = 0,
        VIP = 1
    }

    public class Product
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public decimal Price { get; set; }
        public int Stock { get; set; }
        public ProductCategory Category { get; set; }
        public bool IsActive { get; set; }
    }

    public enum ProductCategory
    {
        Other = 0,
        Electronics = 1,
        Food = 2,
        Book = 3
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
        public OrderStatus Status { get; set; }
        public PaymentStatus PaymentStatus { get; set; }
        public string ShippingAddress { get; set; } = string.Empty;

        public ICollection<OrderLineItem> Items { get; set; } = new List<OrderLineItem>();
    }

    public enum OrderStatus
    {
        Pending = 0,
        Processing = 1,
        Completed = 2,
        Cancelled = 3
    }

    public enum PaymentStatus
    {
        Unpaid = 0,
        Paid = 1,
        Failed = 2
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
        public string Action { get; set; } = string.Empty;
        public int UserId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Details { get; set; } = string.Empty;
    }
}
