using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Auth;

namespace ChabbyNb_API.Controllers
{
    /// <summary>
    /// Base API controller that provides common functionality to all API controllers
    /// </summary>
    [ApiController]
    public abstract class BaseApiController : ControllerBase
    {
        protected readonly ChabbyNbDbContext _context;
        protected readonly ILogger _logger;
        protected readonly IAuthService _authService;

        protected BaseApiController(
            ChabbyNbDbContext context,
            ILogger logger,
            IAuthService authService = null)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _authService = authService;
        }

        #region User Information Methods

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
        /// Gets the current user from the database
        /// </summary>
        /// <returns>The User object or null if not found</returns>
        protected async Task<User> GetCurrentUserAsync()
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return null;

            return await _context.Users.FindAsync(userId.Value);
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
                   User.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == UserRole.Admin.ToString());
        }

        /// <summary>
        /// Checks if the current user has the specified role
        /// </summary>
        /// <param name="role">The role to check</param>
        /// <returns>True if the user has the role, otherwise false</returns>
        protected bool HasRole(UserRole role)
        {
            if (!User.Identity.IsAuthenticated)
                return false;

            return User.HasClaim(c => c.Type == ClaimTypes.Role &&
                                (c.Value == role.ToString() ||
                                 (role == UserRole.Admin && c.Type == "IsAdmin" && c.Value == "True")));
        }

        /// <summary>
        /// Checks if the current user has the specified permission
        /// </summary>
        /// <param name="permission">The permission to check</param>
        /// <returns>True if the user has the permission, otherwise false</returns>
        protected bool HasPermission(UserPermission permission)
        {
            if (!User.Identity.IsAuthenticated)
                return false;

            // Admins have all permissions
            if (IsAdmin())
                return true;

            // Check permissions claim
            var permissionsClaim = User.FindFirstValue("Permissions");
            if (permissionsClaim != null && int.TryParse(permissionsClaim, out int permissionsValue))
            {
                return (permissionsValue & (int)permission) == (int)permission;
            }

            return false;
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

        #endregion

        #region Response Helpers

        /// <summary>
        /// Creates a standardized API response
        /// </summary>
        /// <param name="success">Whether the operation was successful</param>
        /// <param name="message">Optional message</param>
        /// <param name="data">Optional data</param>
        /// <returns>An object representing the response</returns>
        protected object CreateApiResponse(bool success, string message = null, object data = null)
        {
            return new
            {
                success,
                message,
                data,
                timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Creates a successful API response
        /// </summary>
        /// <param name="message">Optional message</param>
        /// <param name="data">Optional data</param>
        /// <returns>An OkObjectResult with the response</returns>
        protected IActionResult ApiSuccess(string message = null, object data = null)
        {
            return Ok(CreateApiResponse(true, message, data));
        }

        /// <summary>
        /// Creates an error API response
        /// </summary>
        /// <param name="message">Error message</param>
        /// <param name="data">Optional data</param>
        /// <param name="statusCode">HTTP status code (default 400 Bad Request)</param>
        /// <returns>An ObjectResult with the response and appropriate status code</returns>
        protected IActionResult ApiError(string message, object data = null, int statusCode = 400)
        {
            return StatusCode(statusCode, CreateApiResponse(false, message, data));
        }

        /// <summary>
        /// Creates a validation error API response
        /// </summary>
        /// <param name="message">Validation error message</param>
        /// <returns>A BadRequestObjectResult with the response</returns>
        protected IActionResult ValidationError(string message)
        {
            return BadRequest(CreateApiResponse(false, message));
        }

        #endregion

        #region Exception Handling

        /// <summary>
        /// Logs an exception and returns a standard error response
        /// </summary>
        /// <param name="ex">The exception</param>
        /// <param name="message">Optional custom message</param>
        /// <returns>An ActionResult with the error response</returns>
        protected IActionResult HandleException(Exception ex, string message = null)
        {
            string errorMessage = message ?? "An error occurred while processing your request";
            _logger.LogError(ex, errorMessage);

            // Determine appropriate status code based on exception type
            int statusCode = ex switch
            {
                UnauthorizedAccessException => 401,
                InvalidOperationException => 400,
                ArgumentException => 400,
                KeyNotFoundException => 404,
                DbUpdateException => 500,
                _ => 500
            };

            // Include exception details in development environments
            object details = null;
            if (HttpContext.Request.Headers["X-Environment"] == "Development")
            {
                details = new
                {
                    exceptionType = ex.GetType().Name,
                    exceptionMessage = ex.Message,
                    stackTrace = ex.StackTrace
                };
            }

            return ApiError(errorMessage, details, statusCode);
        }

        #endregion

        #region Data Access Helpers

        /// <summary>
        /// Checks if an entity exists in the database
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="id">Entity ID</param>
        /// <returns>True if the entity exists, otherwise false</returns>
        protected async Task<bool> EntityExistsAsync<TEntity>(int id) where TEntity : class
        {
            return await _context.Set<TEntity>().FindAsync(id) != null;
        }

        /// <summary>
        /// Gets all entities of a type from the database
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <returns>A list of entities</returns>
        protected async Task<List<TEntity>> GetAllEntitiesAsync<TEntity>() where TEntity : class
        {
            return await _context.Set<TEntity>().ToListAsync();
        }

        /// <summary>
        /// Gets an entity by ID from the database
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="id">Entity ID</param>
        /// <returns>The entity or null if not found</returns>
        protected async Task<TEntity> GetEntityByIdAsync<TEntity>(int id) where TEntity : class
        {
            return await _context.Set<TEntity>().FindAsync(id);
        }

        #endregion

        #region Authorization Helpers

        /// <summary>
        /// Ensures the user is authorized to access the specified entity
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="entityId">Entity ID</param>
        /// <param name="userIdPropertyName">Name of the property containing the user ID</param>
        /// <returns>True if authorized, otherwise false</returns>
        protected async Task<bool> IsAuthorizedForEntityAsync<TEntity>(int entityId, string userIdPropertyName = "UserID")
            where TEntity : class
        {
            var userId = GetCurrentUserId();
            if (!userId.HasValue)
                return false;

            // Admins can access any entity
            if (IsAdmin())
                return true;

            // Get the entity from the database
            var entity = await _context.Set<TEntity>().FindAsync(entityId);
            if (entity == null)
                return false;

            // Check if the entity belongs to the user
            var property = typeof(TEntity).GetProperty(userIdPropertyName);
            if (property == null)
                return false;

            var entityUserId = (int)property.GetValue(entity);
            return entityUserId == userId.Value;
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
        #endregion
    }
}