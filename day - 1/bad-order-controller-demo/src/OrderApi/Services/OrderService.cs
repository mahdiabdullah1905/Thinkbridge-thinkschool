using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using OrderApi.DTOs;
using OrderApi.Exceptions;
using OrderApi.Models;
using OrderApi.Repositories;

namespace OrderApi.Services
{
    public class OrderService : IOrderService
    {
        private readonly IOrderRepository _repository;
        private readonly IEmailService _emailService;
        private readonly ILogger<OrderService> _logger;
        private readonly IEnumerable<IDiscountStrategy> _discountStrategies;

        public OrderService(
            IOrderRepository repository, 
            IEmailService emailService, 
            ILogger<OrderService> logger,
            IEnumerable<IDiscountStrategy> discountStrategies)
        {
            _repository = repository;
            _emailService = emailService;
            _logger = logger;
            _discountStrategies = discountStrategies;
        }

        public async Task<OrderResponseDto> ProcessOrderAsync(CreateOrderDto request, CancellationToken ct)
        {
            ValidateRequest(request);

            var customer = await _repository.GetCustomerByIdAsync(request.CustomerId, ct);
            if (customer == null)
            {
                throw new NotFoundException("Customer not found");
            }

            if (customer.Status != CustomerStatus.Active)
            {
                throw new BusinessValidationException("Customer account is not active");
            }

            var currentBalance = await _repository.GetUnpaidOrdersTotalAsync(customer.Id, ct);
            
            var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
            var products = await _repository.GetProductsByIdsAsync(productIds, ct);
            var productDict = products.ToDictionary(p => p.Id);

            decimal orderTotal = 0;
            decimal taxTotal = 0;
            var itemsToSave = new List<OrderLineItem>();

            foreach (var itemDto in request.Items)
            {
                if (itemDto.Quantity <= 0)
                {
                    throw new BusinessValidationException("Quantity must be greater than zero");
                }

                if (!productDict.TryGetValue(itemDto.ProductId, out var product))
                {
                    throw new BusinessValidationException($"Product with ID {itemDto.ProductId} not found");
                }

                if (!product.IsActive)
                {
                    throw new BusinessValidationException($"Product {product.Name} is not active");
                }

                if (product.Stock < itemDto.Quantity)
                {
                    throw new BusinessValidationException($"Not enough stock for product {product.Name}");
                }

                decimal linePrice = product.Price * itemDto.Quantity;
                decimal lineTax = CalculateTax(product.Category, linePrice);

                orderTotal += linePrice;
                taxTotal += lineTax;

                product.Stock -= itemDto.Quantity;

                itemsToSave.Add(new OrderLineItem
                {
                    ProductId = product.Id,
                    Quantity = itemDto.Quantity,
                    UnitPrice = product.Price,
                    Tax = lineTax
                });
            }

            decimal grandTotal = orderTotal + taxTotal;
            decimal discountTotal = CalculateDiscount(grandTotal, customer);
            
            grandTotal -= discountTotal;

            if (currentBalance + grandTotal > customer.CreditLimit)
            {
                throw new BusinessValidationException("Credit limit exceeded");
            }

            var newOrder = new Order
            {
                CustomerId = customer.Id,
                OrderDate = DateTime.UtcNow,
                SubTotal = orderTotal,
                TaxAmount = taxTotal,
                DiscountAmount = discountTotal,
                TotalAmount = grandTotal,
                Status = OrderStatus.Pending,
                PaymentStatus = PaymentStatus.Unpaid,
                ShippingAddress = $"{request.ShippingDetails!.AddressLine1}, {request.ShippingDetails.City}, {request.ShippingDetails.ZipCode}",
                Items = itemsToSave
            };

            await _repository.SaveOrderAsync(newOrder, ct);

            try
            {
                var audit = new AuditRecord
                {
                    Action = "OrderCreated",
                    UserId = customer.Id,
                    Timestamp = DateTime.UtcNow,
                    Details = $"Order {newOrder.Id} created for amount {grandTotal}"
                };
                await _repository.SaveAuditRecordAsync(audit, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save audit record for Order {OrderId}", newOrder.Id);
                // We choose not to fail the order creation if the audit log fails
            }

            try
            {
                await _emailService.SendEmailAsync(
                    customer.Email, 
                    $"Order Confirmation - {newOrder.Id}", 
                    $"Dear {customer.Name}, your order has been received. Total: ${grandTotal}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send confirmation email to {Email}", customer.Email);
            }

            return new OrderResponseDto
            {
                OrderId = newOrder.Id,
                OrderDate = newOrder.OrderDate,
                Total = grandTotal
            };
        }

        private void ValidateRequest(CreateOrderDto request)
        {
            if (request == null) throw new BusinessValidationException("Request payload is null");
            if (request.CustomerId <= 0) throw new BusinessValidationException("Invalid customer ID");

            if (request.ShippingDetails == null) throw new BusinessValidationException("ShippingDetails are missing");
            ValidateAddress(request.ShippingDetails, "Shipping");

            if (request.BillingDetails == null) throw new BusinessValidationException("BillingDetails are missing");
            ValidateAddress(request.BillingDetails, "Billing");
            
            if (request.Items == null || !request.Items.Any())
            {
                throw new BusinessValidationException("Order must contain at least one item");
            }
        }

        private void ValidateAddress(AddressDto address, string type)
        {
            if (string.IsNullOrWhiteSpace(address.AddressLine1))
                throw new BusinessValidationException($"{type} AddressLine1 is required");
            if (string.IsNullOrWhiteSpace(address.City))
                throw new BusinessValidationException($"{type} City is required");
            if (string.IsNullOrWhiteSpace(address.Country))
                throw new BusinessValidationException($"{type} Country is required");
            // Also checking ZipCode because it was used in original code
            if (string.IsNullOrWhiteSpace(address.ZipCode))
                throw new BusinessValidationException($"{type} ZipCode is required");
        }

        private decimal CalculateTax(ProductCategory category, decimal linePrice)
        {
            return category switch
            {
                ProductCategory.Electronics => linePrice * 0.18m,
                ProductCategory.Food => linePrice * 0.05m,
                ProductCategory.Book => 0m,
                _ => linePrice * 0.10m
            };
        }

        private decimal CalculateDiscount(decimal grandTotal, Customer cust)
        {
            if (grandTotal <= 500) return 0;

            foreach (var strategy in _discountStrategies.OrderBy(s => s.Priority))
            {
                var discount = strategy.CalculateDiscount(grandTotal, cust);
                if (discount > 0)
                {
                    return discount;
                }
            }

            return 0;
        }
    }
}
