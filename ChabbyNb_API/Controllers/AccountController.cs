using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services;
using ChabbyNb_API.Controllers;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Auth;
using System.Security.Claims;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : BaseApiController
    {
        private readonly IUserService _userService;

        public AccountController(
            IUserService userService,
            ILogger<AccountController> logger)
            : base(context: null, logger) // The context is now managed by the service
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
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
                var user = await _userService.RegisterUserAsync(model);

                return ApiSuccess("Registration successful. Please check your email to verify your account.",
                    new { userId = user.UserID, email = user.Email });
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
        /// Update user profile
        /// </summary>
        [HttpPut("Profile")]
        [Authorize(Policy = "RequireGuest")]
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

                var result = await _userService.UpdateUserProfileAsync(userId.Value, model);
                if (!result)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                var user = await _userService.GetUserByIdAsync(userId.Value);

                return ApiSuccess("Profile updated successfully", new
                {
                    userId = user.UserID,
                    username = user.Username,
                    email = user.Email,
                    firstName = user.FirstName,
                    lastName = user.LastName,
                    phoneNumber = user.PhoneNumber
                });
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
        [Authorize(Policy = "RequireGuest")]
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

                if (result)
                {
                    return ApiSuccess("Password changed successfully. Please log in again with your new password.");
                }
                else
                {
                    return ApiError("Current password is incorrect", null, 400);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error changing password");
            }
        }

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
                // First find the user
                var user = await _userService.GetUserByEmailAsync(model.Email);

                // Don't reveal whether the user exists
                if (user != null)
                {
                    await _userService.GeneratePasswordResetTokenAsync(user);
                }

                return ApiSuccess("If your email is registered, you will receive a password reset link shortly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating password reset");
                // Still return OK to not reveal whether the email exists
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

                if (result)
                {
                    return ApiSuccess("Password has been reset successfully. You can now log in with your new password.");
                }
                else
                {
                    return ApiError("Invalid or expired reset token", null, 400);
                }
            }
            catch (Exception ex)
            {
                return HandleException(ex, "Error resetting password");
            }
        }

        /// <summary>
        /// Verify email
        /// </summary>
        [HttpGet("VerifyEmail")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
        {
            try
            {
                var result = await _userService.VerifyEmailAsync(email, token);

                if (result)
                {
                    return ApiSuccess("Email verified successfully. You can now log in.");
                }
                else
                {
                    return ApiError("Invalid or expired verification link", null, 400);
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

                // Don't reveal whether the email exists or was already verified
                return ApiSuccess("If your email is registered and not verified, you will receive a verification email shortly.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending verification email");
                // Still return OK to not reveal whether the email exists
                return ApiSuccess("If your email is registered and not verified, you will receive a verification email shortly.");
            }
        }

        #region User Role Management (Admin Only)

        // GET: api/Account/Roles
        [HttpGet("Roles")]
        [Authorize]
        public async Task<IActionResult> GetUserRoles()
        {
            try
            {
                var userId = GetCurrentUserId();
                if (!userId.HasValue)
                {
                    return Unauthorized(CreateResponse(false, "User not authenticated"));
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

        // Admin endpoints for role management

        // GET: api/Account/Users/{userId}/Roles
        [HttpGet("Users/{userId}/Roles")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> GetRolesForUser(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateResponse(false, "User not found"));
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

        // POST: api/Account/Users/{userId}/Roles/{role}
        [HttpPost("Users/{userId}/Roles/{role}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> AssignRoleToUser(int userId, string role)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateResponse(false, "User not found"));
                }

                // Parse the role
                if (!Enum.TryParse<UserRole>(role, true, out var userRole))
                {
                    return ApiError($"Invalid role: {role}", null, 400);
                }

                // Get admin ID for tracking
                var adminId = GetCurrentUserId().Value;

                // Assign the role
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

        // DELETE: api/Account/Users/{userId}/Roles/{role}
        [HttpDelete("Users/{userId}/Roles/{role}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> RemoveRoleFromUser(int userId, string role)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateResponse(false, "User not found"));
                }

                // Parse the role
                if (!Enum.TryParse<UserRole>(role, true, out var userRole))
                {
                    return ApiError($"Invalid role: {role}", null, 400);
                }

                // Get admin ID for tracking
                var adminId = GetCurrentUserId().Value;

                // Remove the role
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

        // Permission management (for Partners)

        // POST: api/Account/Users/{userId}/Permissions
        [HttpPost("Users/{userId}/Permissions")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> SetUserPermissions(int userId, [FromBody] SetPermissionsDto model)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateResponse(false, "User not found"));
                }

                // Validate user has Partner role
                var roles = await _authService.GetUserRolesAsync(userId);
                if (!roles.Contains(UserRole.Partner))
                {
                    return BadRequest(CreateResponse(false, "Permissions can only be set for Partner role users"));
                }

                // Parse the permissions
                if (!Enum.TryParse<Services.Auth.UserPermission>(model.Permissions, true, out var permissions))
                {
                    return ApiError($"Invalid permissions: {model.Permissions}", null, 400);
                }

                // Get admin ID for tracking
                var adminId = GetCurrentUserId().Value;

                // Set the permissions
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

        // GET: api/Account/Roles/All
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
    }
    public class SetPermissionsDto
    {
        [Required]
        public string Permissions { get; set; }
    }
    #endregion

}