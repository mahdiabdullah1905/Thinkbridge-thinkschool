using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OrderApi.DTOs;
using OrderApi.Exceptions;
using OrderApi.Services;

namespace OrderApi.Controllers
{
    [ApiController]
    [Route("api/refactored/orders")]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderService _orderService;

        public OrdersController(IOrderService orderService)
        {
            _orderService = orderService;
        }

        [HttpPost]
        public async Task<ActionResult<OrderResponseDto>> PostOrder([FromBody] CreateOrderDto request, CancellationToken ct)
        {
            try
            {
                var response = await _orderService.ProcessOrderAsync(request, ct);
                return Ok(response);
            }
            catch (BusinessValidationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (NotFoundException ex)
            {
                return NotFound(new { error = ex.Message });
            }
        }
    }
}
