using System;
using System.Collections.Generic;

namespace OrderApi.DTOs
{
    public class CreateOrderDto
    {
        public int CustomerId { get; set; }
        public AddressDto? ShippingDetails { get; set; }
        public AddressDto? BillingDetails { get; set; }
        public List<OrderItemDto> Items { get; set; } = new List<OrderItemDto>();
    }

    public class AddressDto
    {
        public string? AddressLine1 { get; set; }
        public string? City { get; set; }
        public string? Country { get; set; }
        public string? ZipCode { get; set; }
    }

    public class OrderItemDto
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    public class OrderResponseDto
    {
        public int OrderId { get; set; }
        public DateTime OrderDate { get; set; }
        public decimal Total { get; set; }
    }
}
