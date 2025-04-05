using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Data;

namespace ChabbyNb_API.Controllers
{
    /// <summary>
    /// Base API controller that provides common functionality to all API controllers
    /// </summary>
    public abstract class BaseApiController : ControllerBase
    {
        protected readonly ChabbyNbDbContext _context;
        protected readonly ILogger _logger;

        protected BaseApiController(ChabbyNbDbContext context, ILogger logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets the ID of the current user from claims
        /// </summary>
        /// <returns>The user ID or null if not authenticated</returns>
        protected int? GetCurrentUserId()
        {
            if (!User.Identity.IsAuthenticated)
                return null;

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return null;

            return userId;
        }

        /// <summary>
        /// Gets the email of the current user from claims
        /// </summary>
        /// <returns>The user email or null if not authenticated</returns>
        protected string GetCurrentUserEmail()
        {
            if (!User.Identity.IsAuthenticated)
                return null;

            return User.FindFirstValue(ClaimTypes.Email);
        }

        /// <summary>
        /// Checks if the current user is an administrator
        /// </summary>
        /// <returns>True if the user is an administrator, otherwise false</returns>
        protected bool IsAdmin()
        {
            if (!User.Identity.IsAuthenticated)
                return false;

            return User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True") ||
                   User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin");
        }

        /// <summary>
        /// Gets the client IP address
        /// </summary>
        /// <returns>The client IP address</returns>
        protected string GetClientIpAddress()
        {
            // Try to get the forwarded IP if behind a proxy
            string ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            // If no forwarded IP, use the remote IP
            if (string.IsNullOrEmpty(ip))
            {
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            // If still no IP, use a default
            if (string.IsNullOrEmpty(ip))
            {
                ip = "127.0.0.1";
            }

            // If multiple IPs (comma separated), take the first one
            if (ip.Contains(","))
            {
                ip = ip.Split(',').First().Trim();
            }

            return ip;
        }

        /// <summary>
        /// Creates a standard API response
        /// </summary>
        /// <param name="success">Whether the operation was successful</param>
        /// <param name="message">Optional message</param>
        /// <param name="data">Optional data</param>
        /// <returns>An object representing the response</returns>
        protected object CreateResponse(bool success, string message = null, object data = null)
        {
            var response = new
            {
                success,
                message,
                data
            };

            return response;
        }

        /// <summary>
        /// Logs an exception and returns a standard error response
        /// </summary>
        /// <param name="ex">The exception</param>
        /// <param name="message">Optional custom message</param>
        /// <returns>An ActionResult with the error response</returns>
        protected ActionResult HandleException(Exception ex, string message = null)
        {
            string errorMessage = message ?? "An error occurred while processing your request";
            _logger.LogError(ex, errorMessage);

            return StatusCode(500, CreateResponse(false, errorMessage));
        }
    }
}