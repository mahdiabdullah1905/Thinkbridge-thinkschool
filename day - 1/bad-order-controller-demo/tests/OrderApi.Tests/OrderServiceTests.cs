using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Moq;
using OrderApi.DTOs;
using OrderApi.Exceptions;
using OrderApi.Models;
using OrderApi.Repositories;
using OrderApi.Services;
using Xunit;

namespace OrderApi.Tests
{
    public class OrderServiceTests
    {
        private readonly Mock<IOrderRepository> _repoMock;
        private readonly Mock<IEmailService> _emailMock;
        private readonly Mock<ILogger<OrderService>> _loggerMock;
        private readonly OrderService _service;

        public OrderServiceTests()
        {
            _repoMock = new Mock<IOrderRepository>();
            _emailMock = new Mock<IEmailService>();
            _loggerMock = new Mock<ILogger<OrderService>>();
            _service = new OrderService(_repoMock.Object, _emailMock.Object, _loggerMock.Object);
        }

        [Fact]
        public async Task ProcessOrderAsync_ValidRequest_CreatesOrderSuccessfully()
        {
            // Arrange
            var req = CreateValidRequest();
            var cust = new Customer { Id = 1, Status = CustomerStatus.Active, CreditLimit = 1000, Email = "test@test.com" };
            
            _repoMock.Setup(r => r.GetCustomerByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(cust);
            _repoMock.Setup(r => r.GetUnpaidOrdersTotalAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(0m);
            _repoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Product>
                {
                    new Product { Id = 10, Price = 100, Stock = 10, IsActive = true, Category = ProductCategory.Electronics }
                });

            // Act
            var result = await _service.ProcessOrderAsync(req, CancellationToken.None);

            // Assert
            Assert.NotNull(result);
            _repoMock.Verify(r => r.SaveOrderAsync(It.Is<Order>(o => o.TotalAmount == 118m), It.IsAny<CancellationToken>()), Times.Once);
            _emailMock.Verify(e => e.SendEmailAsync("test@test.com", It.IsAny<string>(), It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task ProcessOrderAsync_MissingShippingDetails_ThrowsBusinessValidationException()
        {
            // Arrange
            var req = CreateValidRequest();
            req.ShippingDetails = null; // simulate null deref bug from old code

            // Act & Assert
            var ex = await Assert.ThrowsAsync<BusinessValidationException>(() => _service.ProcessOrderAsync(req, CancellationToken.None));
            Assert.Equal("ShippingDetails are missing", ex.Message);
        }

        [Fact]
        public async Task ProcessOrderAsync_MultipleItemsOffByOneCheck_ProcessesAllItems()
        {
            // Arrange
            var req = CreateValidRequest();
            req.Items.Add(new OrderItemDto { ProductId = 20, Quantity = 1 }); // Now has 2 items
            
            var cust = new Customer { Id = 1, Status = CustomerStatus.Active, CreditLimit = 1000 };
            
            _repoMock.Setup(r => r.GetCustomerByIdAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(cust);
            _repoMock.Setup(r => r.GetProductsByIdsAsync(It.IsAny<IEnumerable<int>>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<Product>
                {
                    new Product { Id = 10, Price = 100, Stock = 10, IsActive = true, Category = ProductCategory.Other },
                    new Product { Id = 20, Price = 50, Stock = 5, IsActive = true, Category = ProductCategory.Other }
                });

            // Act
            var result = await _service.ProcessOrderAsync(req, CancellationToken.None);

            // Assert
            // 100*1 + 50*1 = 150. Tax is 10% for Other category = 15. Total = 165
            _repoMock.Verify(r => r.SaveOrderAsync(It.Is<Order>(o => o.TotalAmount == 165m), It.IsAny<CancellationToken>()), Times.Once);
        }

        private CreateOrderDto CreateValidRequest()
        {
            return new CreateOrderDto
            {
                CustomerId = 1,
                ShippingDetails = new AddressDto { AddressLine1 = "123 St", City = "City", Country = "US", ZipCode = "12345" },
                BillingDetails = new AddressDto { AddressLine1 = "123 St", City = "City", Country = "US", ZipCode = "12345" },
                Items = new List<OrderItemDto> { new OrderItemDto { ProductId = 10, Quantity = 1 } }
            };
        }
    }
}
