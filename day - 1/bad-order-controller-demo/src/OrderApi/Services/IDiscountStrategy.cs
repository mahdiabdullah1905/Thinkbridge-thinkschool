using OrderApi.Models;

namespace OrderApi.Services
{
    public interface IDiscountStrategy
    {
        int Priority { get; }
        decimal CalculateDiscount(decimal grandTotal, Customer customer);
    }
}
