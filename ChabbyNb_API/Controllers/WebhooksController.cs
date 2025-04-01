using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using ChabbyNb_API.Services.Iterfaces;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class WebhooksController : ControllerBase
    {
        private readonly IPaymentService _paymentService;

        public WebhooksController(IPaymentService paymentService)
        {
            _paymentService = paymentService;
        }

        [HttpPost("Stripe")]
        public async Task<IActionResult> HandleStripeWebhook()
        {
            var json = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();

            try
            {
                // The signature comes in the Stripe-Signature header
                string signature = Request.Headers["Stripe-Signature"];

                bool handled = await _paymentService.HandleWebhookEvent(json, signature);

                if (handled)
                {
                    return Ok();
                }
                else
                {
                    return BadRequest("Failed to handle webhook");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error handling webhook: {ex.Message}");
                return BadRequest($"Webhook error: {ex.Message}");
            }
        }
    }
}