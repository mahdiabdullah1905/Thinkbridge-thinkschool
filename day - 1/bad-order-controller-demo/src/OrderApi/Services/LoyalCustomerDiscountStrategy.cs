using System;
using OrderApi.Models;

namespace OrderApi.Services
{
    public class LoyalCustomerDiscountStrategy : IDiscountStrategy
    {
        public int Priority => 2;

        public decimal CalculateDiscount(decimal grandTotal, Customer customer)
        {
            if (customer.RegistrationDate < DateTime.UtcNow.AddYears(-5))
            {
                return grandTotal * 0.05m;
            }
            return 0;
        }
    }
}
