using System;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Services
{
    public interface IPaymentService
    {
        // Create a payment intent (when booking is created)
        Task<string> CreatePaymentIntent(Booking booking);

        // Confirm payment after successful client-side confirmation
        Task<Payment> ConfirmPayment(string paymentIntentId);

        // Process a refund
        Task<Refund> ProcessRefund(int paymentId, decimal amount, string reason, int adminId);

        // Create and process a direct charge (additional charges)
        Task<Payment> ProcessManualCharge(ManualChargeDto chargeDetails, int adminId);

        // Get payment details
        Task<Payment> GetPaymentById(int paymentId);

        // Webhook handler for processing payment events from Stripe
        Task<bool> HandleWebhookEvent(string json, string signature);
    }
}