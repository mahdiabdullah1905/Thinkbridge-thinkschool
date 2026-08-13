using OrderApi.Models;

namespace OrderApi.Services
{
    public class VipDiscountStrategy : IDiscountStrategy
    {
        public int Priority => 1;

        public decimal CalculateDiscount(decimal grandTotal, Customer customer)
        {
            if (customer.CustomerType == CustomerType.VIP)
            {
                return grandTotal * 0.10m;
            }
            return 0;
        }
    }
}
