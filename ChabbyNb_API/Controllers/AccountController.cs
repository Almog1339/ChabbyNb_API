using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services;
using ChabbyNb_API.Controllers;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Auth;
using ChabbyNb_API.Services.Core;
using ChabbyNb_API.Data;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IAuthService _authService;
        private readonly ChabbyNbDbContext _context;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IUserService userService,
            IAuthService authService,
            ChabbyNbDbContext context,
            ILogger<AccountController> logger)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _authService = authService ?? throw new ArgumentNullException(nameof(authService));
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        #region Helper Methods

        private int? GetCurrentUserId()
        {
            if (!User.Identity.IsAuthenticated)
                return null;

            var userIdClaim = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdClaim) || !int.TryParse(userIdClaim, out int userId))
                return null;

            return userId;
        }

        private string GetClientIpAddress()
        {
            string ip = HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            if (string.IsNullOrEmpty(ip))
            {
                ip = HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            if (string.IsNullOrEmpty(ip))
            {
                ip = "127.0.0.1";
            }

            if (ip.Contains(","))
            {
                ip = ip.Split(',').First().Trim();
            }

            return ip;
        }

        private object CreateApiResponse(bool success, string message = null, object data = null)
        {
            return new
            {
                success,
                message,
                data,
                timestamp = DateTime.UtcNow
            };
        }

        private IActionResult ApiSuccess(string message = null, object data = null)
        {
            return Ok(CreateApiResponse(true, message, data));
        }

        private IActionResult ApiError(string message, object data = null, int statusCode = 400)
        {
            return StatusCode(statusCode, CreateApiResponse(false, message, data));
        }

        private IActionResult ValidationError(string message)
        {
            return BadRequest(CreateApiResponse(false, message));
        }

        private IActionResult HandleException(Exception ex, string message = null)
        {
            string errorMessage = message ?? "An error occurred while processing your request";
            _logger.LogError(ex, errorMessage);

            int statusCode = ex switch
            {
                UnauthorizedAccessException => 401,
                InvalidOperationException => 400,
                ArgumentException => 400,
                KeyNotFoundException => 404,
                DbUpdateException => 500,
                _ => 500
            };

            object details = null;
            if (HttpContext.Request.Headers.ContainsKey("X-Environment") &&
                HttpContext.Request.Headers["X-Environment"] == "Development")
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

        #region Authentication Endpoints

        /// <summary>
        /// Login with email/password or reservation number
        /// </summary>
        [HttpPost("Login")]
        public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new LoginResultDto
                {
                    Success = false,
                    Message = "Invalid login data"
                });
            }

            try
            {
                var ipAddress = GetClientIpAddress();
                var result = await _authService.AuthenticateAsync(model, ipAddress);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Unauthorized(new LoginResultDto
                {
                    Success = false,
                    Message = ex.Message
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new LoginResultDto
                {
                    Success = false,
                    Message = "Error during login"
                });
            }
        }

        /// <summary>
        /// Refresh an authentication token
        /// </summary>
        [HttpPost("RefreshToken")]
        public async Task<ActionResult<LoginResultDto>> RefreshToken([FromBody] RefreshTokenDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(new LoginResultDto
                {
                    Success = false,
                    Message = "Invalid refresh token data"
                });
            }

            try
            {
                var ipAddress = GetClientIpAddress();
                var result = await _authService.RefreshTokenAsync(model.RefreshToken, model.AccessToken, ipAddress);

                return Ok(new LoginResultDto
                {
                    Success = true,
                    Message = "Token refreshed successfully",
                    Token = result.AccessToken,
                    RefreshToken = result.RefreshToken,
                    TokenExpiration = result.AccessTokenExpiration
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return StatusCode(500, new LoginResultDto
                {
                    Success = false,
                    Message = "Error refreshing token"
                });
            }
        }

        /// <summary>
        /// Register a new user
        /// </summary>
        [HttpPost("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto model)
        {
            if (!ModelState.IsValid)
            {
                return ValidationError("Invalid registration data");
            }

            try
            {
                var user = await _userService.CreateAsync(model);

                return ApiSuccess("Registration successful. Please check your email to verify your account.",
                    new { userId = user.UserId, email = user.Email });
            }
            catch (InvalidOperationException ex)
            {
                return ApiError(ex.Message, null, 409);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error registering user");
            }
        }

        /// <summary>
        /// Logout user
        /// </summary>
        [HttpPost("Logout")]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] LogoutDto model)
        {
            try
            {
                var userId = GetCurrentUserId();
                if (userId.HasValue && !string.IsNullOrEmpty(model.RefreshToken))
                {
                    
                    //await _authService.RevokeTokenAsync(model.RefreshToken);
                }

                return ApiSuccess("Logged out successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return ApiSuccess("Logged out successfully"); // Still return success to client
            }
        }

        #endregion

        #region Profile Management

        /// <summary>
        /// Get current user profile
        /// </summary>
        [HttpGet("Profile")]
        [Authorize]
        public async Task<IActionResult> GetProfile()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(CreateApiResponse(false, "User not authenticated"));
                }

                var user = await _userService.GetByIdAsync(userId.Value);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                return ApiSuccess("Profile retrieved successfully", user);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error retrieving profile");
            }
        }

        /// <summary>
        /// Update user profile
        /// </summary>
        [HttpPut("Profile")]
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto model)
        {
            if (!ModelState.IsValid)
            {
                return ValidationError("Invalid profile data");
            }

            try
            {
                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(CreateApiResponse(false, "User not authenticated"));
                }

                var updatedUser = await _userService.UpdateAsync(userId.Value, model);
                if (updatedUser == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                return ApiSuccess("Profile updated successfully", updatedUser);
            }
            catch (InvalidOperationException ex)
            {
                return ApiError(ex.Message, null, 409);
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error updating profile");
            }
        }

        /// <summary>
        /// Change password
        /// </summary>
        [HttpPut("ChangePassword")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return ValidationError("Invalid password data");
            }

            try
            {
                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(CreateApiResponse(false, "User not authenticated"));
                }

                var result = await _userService.ChangePasswordAsync(userId.Value, model);

                if (result.Success)
                {
                    return ApiSuccess("Password changed successfully. Please log in again with your new password.");
                }
                else
                {
                    return ApiError(result.Errors.FirstOrDefault() ?? "Failed to change password", null, 400);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error changing password");
            }
        }

        #endregion

        #region Password Reset

        /// <summary>
        /// Initiate password reset
        /// </summary>
        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return ValidationError("Invalid email");
            }

            try
            {
                var user = await _userService.GetUserByEmailAsync(model.Email);

                if (user != null)
                {
                    await _userService.GeneratePasswordResetTokenAsync(user);
                }

                return ApiSuccess("If your email is registered, you will receive a password reset link shortly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating password reset");
                return ApiSuccess("If your email is registered, you will receive a password reset link shortly.");
            }
        }

        /// <summary>
        /// Complete password reset
        /// </summary>
        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return ValidationError("Invalid reset password data");
            }

            try
            {
                var result = await _userService.ResetPasswordAsync(model.Email, model.Token, model.Password);

                if (result.Success)
                {
                    return ApiSuccess("Password has been reset successfully. You can now log in with your new password.");
                }
                else
                {
                    return ApiError(result.Errors.FirstOrDefault() ?? "Invalid or expired reset token", null, 400);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error resetting password");
            }
        }

        #endregion

        #region Email Verification

        /// <summary>
        /// Verify email
        /// </summary>
        [HttpGet("VerifyEmail")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
        {
            try
            {
                var result = await _userService.VerifyEmailAsync(email, token);

                if (result.Success)
                {
                    return ApiSuccess("Email verified successfully. You can now log in.");
                }
                else
                {
                    return ApiError(result.Errors.FirstOrDefault() ?? "Invalid or expired verification link", null, 400);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error verifying email");
            }
        }

        /// <summary>
        /// Resend verification email
        /// </summary>
        [HttpPost("ResendVerification")]
        public async Task<IActionResult> ResendVerification([FromBody] ForgotPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return ValidationError("Invalid email");
            }

            try
            {
                await _userService.ResendVerificationEmailAsync(model.Email);

                return ApiSuccess("If your email is registered and not verified, you will receive a verification email shortly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending verification email");
                return ApiSuccess("If your email is registered and not verified, you will receive a verification email shortly.");
            }
        }

        #endregion

        #region User Role Management (Admin Only)

        /// <summary>
        /// Get current user's roles and permissions
        /// </summary>
        [HttpGet("Roles")]
        [Authorize]
        public async Task<IActionResult> GetUserRoles()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(CreateApiResponse(false, "User not authenticated"));
                }

                var roles = await _authService.GetUserRolesAsync(userId.Value);
                var permissions = await _authService.GetUserPermissionsAsync(userId.Value);
                var highestRole = await _authService.GetHighestRoleAsync(userId.Value);

                return ApiSuccess("Roles retrieved successfully", new
                {
                    roles = roles.Select(r => r.ToString()),
                    permissions = permissions.Select(p => p.ToString()),
                    highestRole = highestRole.ToString()
                });
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error getting user roles");
            }
        }

        /// <summary>
        /// Get roles for a specific user (Admin only)
        /// </summary>
        [HttpGet("Users/{userId}/Roles")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> GetRolesForUser(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                var roles = await _authService.GetUserRolesAsync(userId);
                var permissions = await _authService.GetUserPermissionsAsync(userId);

                return ApiSuccess("Roles retrieved successfully", new
                {
                    userId,
                    email = user.Email,
                    username = user.Username,
                    roles = roles.Select(r => r.ToString()),
                    permissions = permissions.Select(p => p.ToString())
                });
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Error getting roles for user {userId}");
            }
        }

        /// <summary>
        /// Assign role to user (Admin only)
        /// </summary>
        [HttpPost("Users/{userId}/Roles/{role}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> AssignRoleToUser(int userId, string role)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                if (!Enum.TryParse<UserRole>(role, true, out var userRole))
                {
                    return ApiError($"Invalid role: {role}", null, 400);
                }

                var adminId = GetCurrentUserId().Value;
                var success = await _authService.AssignRoleToUserAsync(userId, userRole, adminId);

                if (success)
                {
                    await LogAdminAction($"Role '{role}' assigned to user {userId} by admin {adminId}");
                    return ApiSuccess($"Role '{role}' assigned to user successfully");
                }
                else
                {
                    return ApiError("Failed to assign role", null, 500);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Error assigning role to user {userId}");
            }
        }

        /// <summary>
        /// Remove role from user (Admin only)
        /// </summary>
        [HttpDelete("Users/{userId}/Roles/{role}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> RemoveRoleFromUser(int userId, string role)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                if (!Enum.TryParse<UserRole>(role, true, out var userRole))
                {
                    return ApiError($"Invalid role: {role}", null, 400);
                }

                var adminId = GetCurrentUserId().Value;
                var success = await _authService.RemoveRoleFromUserAsync(userId, userRole, adminId);

                if (success)
                {
                    await LogAdminAction($"Role '{role}' removed from user {userId} by admin {adminId}");
                    return ApiSuccess($"Role '{role}' removed from user successfully");
                }
                else
                {
                    return ApiError("Failed to remove role", null, 500);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Error removing role from user {userId}");
            }
        }

        /// <summary>
        /// Set user permissions (Admin only)
        /// </summary>
        [HttpPost("Users/{userId}/Permissions")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> SetUserPermissions(int userId, [FromBody] SetPermissionsDto model)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                var roles = await _authService.GetUserRolesAsync(userId);
                if (!roles.Contains(UserRole.Partner))
                {
                    return BadRequest(CreateApiResponse(false, "Permissions can only be set for Partner role users"));
                }

                if (!Enum.TryParse<Services.Auth.UserPermission>(model.Permissions, true, out var permissions))
                {
                    return ApiError($"Invalid permissions: {model.Permissions}", null, 400);
                }

                var adminId = GetCurrentUserId().Value;
                var success = await _authService.SetUserPermissionsAsync(userId, permissions, adminId);

                if (success)
                {
                    await LogAdminAction($"Permissions set to '{model.Permissions}' for user {userId} by admin {adminId}");
                    return ApiSuccess($"Permissions set to '{model.Permissions}' for user successfully");
                }
                else
                {
                    return ApiError("Failed to set permissions", null, 500);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, $"Error setting permissions for user {userId}");
            }
        }

        /// <summary>
        /// Get all available roles and permissions (Admin only)
        /// </summary>
        [HttpGet("Roles/All")]
        [Authorize(Policy = "RequireAdminRole")]
        public IActionResult GetAllRoles()
        {
            try
            {
                var roles = Enum.GetNames(typeof(UserRole));
                var permissions = Enum.GetNames(typeof(Services.Auth.UserPermission));

                return ApiSuccess("Roles and permissions retrieved successfully", new
                {
                    roles,
                    permissions
                });
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error getting all roles");
            }
        }

        #endregion

        #region Private Helper Methods

        private async Task LogAdminAction(string action)
        {
            try
            {
                var adminId = GetCurrentUserId();
                if (!adminId.HasValue) return;

                var adminLog = new AdminLog
                {
                    AdminID = adminId.Value,
                    AdminEmail = User.FindFirst(ClaimTypes.Email)?.Value ?? "unknown",
                    Action = action,
                    Timestamp = DateTime.UtcNow,
                    IPAddress = GetClientIpAddress()
                };

                _context.AdminLogs.Add(adminLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error logging admin action: {action}");
            }
        }

        #endregion
    }

    #region Supporting DTOs

    public class SetPermissionsDto
    {
        [Required]
        public string Permissions { get; set; }
    }

    #endregion
}