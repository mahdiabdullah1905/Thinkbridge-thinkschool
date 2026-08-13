using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using OrderApi.Data;
using OrderApi.DTOs;
using OrderApi.Models;
using Xunit;

namespace OrderApi.Tests
{
    public class OrderIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
    {
        private readonly HttpClient _client;
        private readonly WebApplicationFactory<Program> _factory;

        public OrderIntegrationTests(WebApplicationFactory<Program> factory)
        {
            _factory = factory;
            _client = factory.CreateClient();
            
            SeedDatabase();
        }

        private void SeedDatabase()
        {
            using var scope = _factory.Services.CreateScope();
            
            // Seed new context
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            
            if (db.Customers.Find(1) == null)
            {
                db.Customers.Add(new Customer { Id = 1, Name = "Test", Email = "test@test.com", Status = CustomerStatus.Active, CreditLimit = 5000 });
                db.Products.Add(new Product { Id = 10, Name = "Laptop", Price = 1000, Stock = 10, IsActive = true, Category = ProductCategory.Electronics });
                db.SaveChanges();
            }


        }

        [Theory]
        [InlineData("/api/orders", HttpStatusCode.InternalServerError)] // Original fails with 500 due to null dereference
        [InlineData("/api/refactored/orders", HttpStatusCode.BadRequest)] // Refactored correctly returns 400
        public async Task MissingShippingDetails_ShouldReturnExpectedStatus(string endpoint, HttpStatusCode expectedStatus)
        {
            // Arrange: Provide a payload missing ShippingDetails
            var req = new CreateOrderDto
            {
                CustomerId = 1,
                ShippingDetails = null, // This exposes the null dereference bug in the original
                BillingDetails = new AddressDto { AddressLine1 = "1", City = "1", Country = "1", ZipCode = "1" },
                Items = new System.Collections.Generic.List<OrderItemDto> { new OrderItemDto { ProductId = 10, Quantity = 1 } }
            };

            // Act
            var response = await _client.PostAsJsonAsync(endpoint, req);

            // Assert
            Assert.Equal(expectedStatus, response.StatusCode);
        }
        
        [Fact]
        public async Task RefactoredController_ValidOrder_Returns200Ok()
        {
            // Arrange
            var req = new CreateOrderDto
            {
                CustomerId = 1,
                ShippingDetails = new AddressDto { AddressLine1 = "1", City = "1", Country = "1", ZipCode = "1" },
                BillingDetails = new AddressDto { AddressLine1 = "1", City = "1", Country = "1", ZipCode = "1" },
                Items = new System.Collections.Generic.List<OrderItemDto> { new OrderItemDto { ProductId = 10, Quantity = 1 } }
            };

            // Act
            var response = await _client.PostAsJsonAsync("/api/refactored/orders", req); 

            // Assert
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
    }
}
