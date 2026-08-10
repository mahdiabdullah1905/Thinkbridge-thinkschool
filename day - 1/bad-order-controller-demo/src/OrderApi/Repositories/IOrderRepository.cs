using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using OrderApi.Models;

namespace OrderApi.Repositories
{
    public interface IOrderRepository
    {
        Task<Customer?> GetCustomerByIdAsync(int customerId, CancellationToken ct);
        Task<decimal> GetUnpaidOrdersTotalAsync(int customerId, CancellationToken ct);
        Task<List<Product>> GetProductsByIdsAsync(IEnumerable<int> productIds, CancellationToken ct);
        Task SaveOrderAsync(Order order, CancellationToken ct);
        Task SaveAuditRecordAsync(AuditRecord auditRecord, CancellationToken ct);
    }
}
