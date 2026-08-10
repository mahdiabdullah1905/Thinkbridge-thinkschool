using System;
using System.Threading;
using System.Threading.Tasks;
using OrderApi.DTOs;

namespace OrderApi.Services
{
    public interface IOrderService
    {
        Task<OrderResponseDto> ProcessOrderAsync(CreateOrderDto request, CancellationToken ct);
    }
}
