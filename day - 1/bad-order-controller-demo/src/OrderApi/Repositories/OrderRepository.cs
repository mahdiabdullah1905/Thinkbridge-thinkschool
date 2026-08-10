using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using OrderApi.Data;
using OrderApi.Models;

namespace OrderApi.Repositories
{
    public class OrderRepository : IOrderRepository
    {
        private readonly AppDbContext _context;

        public OrderRepository(AppDbContext context)
        {
            _context = context;
        }

        public async Task<Customer?> GetCustomerByIdAsync(int customerId, CancellationToken ct)
        {
            return await _context.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct);
        }

        public async Task<decimal> GetUnpaidOrdersTotalAsync(int customerId, CancellationToken ct)
        {
            return await _context.Orders
                .Where(o => o.CustomerId == customerId && o.PaymentStatus != PaymentStatus.Paid)
                .SumAsync(o => o.TotalAmount, ct);
        }

        public async Task<List<Product>> GetProductsByIdsAsync(IEnumerable<int> productIds, CancellationToken ct)
        {
            return await _context.Products
                .Where(p => productIds.Contains(p.Id))
                .ToListAsync(ct);
        }

        public async Task SaveOrderAsync(Order order, CancellationToken ct)
        {
            _context.Orders.Add(order);
            // Saves both header and line items together due to EF tracking
            await _context.SaveChangesAsync(ct);
        }

        public async Task SaveAuditRecordAsync(AuditRecord auditRecord, CancellationToken ct)
        {
            _context.AuditRecords.Add(auditRecord);
            await _context.SaveChangesAsync(ct);
        }
    }
}
