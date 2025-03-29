// EmailService.cs in Services folder
using System;
using System.IO;
using System.Net;
using System.Net.Mail;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ChabbyNb_API.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string to, string subject, string templateName, object model);
        Task SendEmailWithAttachmentAsync(string to, string subject, string templateName, object model, string attachmentPath);
    }

    public class EmailService : IEmailService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<EmailService> _logger;
        private readonly string _templatesPath;

        public EmailService(IConfiguration configuration, ILogger<EmailService> logger, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _logger = logger;
            _templatesPath = Path.Combine(environment.ContentRootPath, "EmailTemplates");
        }

        public async Task SendEmailAsync(string to, string subject, string templateName, object model)
        {
            await SendEmailWithAttachmentAsync(to, subject, templateName, model, null);
        }

        public async Task SendEmailWithAttachmentAsync(string to, string subject, string templateName, object model, string attachmentPath)
        {
            // Check if we should send real emails
            if (!_configuration.GetValue<bool>("SendRealEmails", false))
            {
                // For development, just log the email
                _logger.LogInformation($"Email would be sent to: {to}");
                _logger.LogInformation($"Subject: {subject}");
                _logger.LogInformation($"Template: {templateName}");
                _logger.LogInformation($"Model: {System.Text.Json.JsonSerializer.Serialize(model)}");
                return;
            }

            try
            {
                // Load template
                string templatePath = Path.Combine(_templatesPath, $"{templateName}.html");
                if (!File.Exists(templatePath))
                {
                    _logger.LogError($"Email template {templateName} not found at {templatePath}");
                    throw new FileNotFoundException($"Email template {templateName} not found");
                }

                string templateContent = await File.ReadAllTextAsync(templatePath);
                string body = ReplaceTokens(templateContent, model);

                // Get SMTP settings from configuration
                var smtpSettings = _configuration.GetSection("SmtpSettings");
                string host = smtpSettings["Host"];
                int port = int.Parse(smtpSettings["Port"] ?? "587");
                bool enableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                string username = smtpSettings["Username"];
                string password = smtpSettings["Password"];
                string fromEmail = smtpSettings["FromEmail"];
                string fromName = smtpSettings["FromName"] ?? "ChabbyNb";

                if (string.IsNullOrEmpty(host) || string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                {
                    throw new InvalidOperationException("SMTP settings are not properly configured");
                }

                // Create mail message
                using var mailMessage = new MailMessage
                {
                    From = new MailAddress(fromEmail, fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(new MailAddress(to));

                // Add attachment if provided
                if (!string.IsNullOrEmpty(attachmentPath) && File.Exists(attachmentPath))
                {
                    var attachment = new Attachment(attachmentPath);
                    mailMessage.Attachments.Add(attachment);
                }

                // Send email
                using var client = new SmtpClient(host)
                {
                    Port = port,
                    EnableSsl = enableSsl,
                    Credentials = new NetworkCredential(username, password),
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };

                await client.SendMailAsync(mailMessage);
                _logger.LogInformation($"Email sent to {to} with subject '{subject}'");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Failed to send email to {to} with subject '{subject}'");
                throw;
            }
        }

        private string ReplaceTokens(string template, object model)
        {
            // Use reflection to get property values from the model
            if (model == null) return template;

            var type = model.GetType();
            var properties = type.GetProperties();

            string result = template;
            foreach (var property in properties)
            {
                var value = property.GetValue(model)?.ToString() ?? string.Empty;
                result = result.Replace($"{{{{${property.Name}$}}}}", value);
            }

            return result;
        }
    }
}