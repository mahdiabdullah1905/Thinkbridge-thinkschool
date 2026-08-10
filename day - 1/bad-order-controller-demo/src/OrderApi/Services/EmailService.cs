using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace OrderApi.Services
{
    public class EmailService : IEmailService
    {
        private readonly ILogger<EmailService> _logger;

        public EmailService(ILogger<EmailService> logger)
        {
            _logger = logger;
        }

        public Task SendEmailAsync(string to, string subject, string body)
        {
            _logger.LogInformation("Simulating sending email to {To} with subject {Subject}", to, subject);
            return Task.CompletedTask;
        }
    }
}
