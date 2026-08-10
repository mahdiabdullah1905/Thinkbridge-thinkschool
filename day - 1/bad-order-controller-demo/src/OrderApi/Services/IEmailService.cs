using System.Threading.Tasks;

namespace OrderApi.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string body);
    }
}
