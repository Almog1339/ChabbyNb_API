using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services;
using ChabbyNb_API.Services.Auth;
using System.Security.Claims;
using Microsoft.Extensions.Logging;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly IRoleService _roleService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            IUserService userService,
            IRoleService roleService,
            ILogger<AccountController> logger)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Login with email/password or reservation number
        /// </summary>
        [HttpPost("Login")]
        public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _userService.AuthenticateAsync(model);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(401, new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new { error = "An error occurred during login" });
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
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _userService.RefreshTokenAsync(model);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                return StatusCode(401, new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing token");
                return StatusCode(500, new { error = "An error occurred while refreshing the token" });
            }
        }

        /// <summary>
        /// Logout user
        /// </summary>
        [HttpPost("Logout")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Logout([FromBody] LogoutDto model)
        {
            try
            {
                await _userService.LogoutAsync(model);
                return Ok(new { success = true, message = "Logged out successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                // Still return success even if token revocation fails
                return Ok(new { success = true, message = "Logged out successfully" });
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
                return BadRequest(ModelState);
            }

            try
            {
                var user = await _userService.RegisterAsync(model);

                return Ok(new
                {
                    success = true,
                    message = "Registration successful. Please check your email to verify your account.",
                    userId = user.UserID,
                    email = user.Email
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return StatusCode(500, new { error = "An error occurred during registration" });
            }
        }

        /// <summary>
        /// Update user profile
        /// </summary>
        [HttpPut("Profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _userService.UpdateProfileAsync(userId, model);

                return Ok(new
                {
                    success = true,
                    message = "Profile updated successfully",
                    data = new
                    {
                        userId = user.UserID,
                        username = user.Username,
                        email = user.Email,
                        firstName = user.FirstName,
                        lastName = user.LastName,
                        phoneNumber = user.PhoneNumber
                    }
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating profile");
                return StatusCode(500, new { error = "An error occurred while updating your profile" });
            }
        }

        /// <summary>
        /// Change password
        /// </summary>
        [HttpPut("ChangePassword")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                await _userService.ChangePasswordAsync(userId, model);

                return Ok(new
                {
                    success = true,
                    message = "Password changed successfully. Please log in again with your new password."
                });
            }
            catch (UnauthorizedAccessException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return StatusCode(500, new { error = "An error occurred while changing your password" });
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
                return BadRequest(ModelState);
            }

            try
            {
                await _userService.InitiatePasswordResetAsync(model.Email);

                // Don't reveal whether the user exists
                return Ok(new
                {
                    success = true,
                    message = "If your email is registered, you will receive a password reset link shortly."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating password reset");
                // Still return OK to not reveal whether the email exists
                return Ok(new
                {
                    success = true,
                    message = "If your email is registered, you will receive a password reset link shortly."
                });
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
                return BadRequest(ModelState);
            }

            try
            {
                await _userService.ResetPasswordAsync(model);

                return Ok(new
                {
                    success = true,
                    message = "Password has been reset successfully. You can now log in with your new password."
                });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password");
                return StatusCode(500, new { error = "An error occurred while resetting your password" });
            }
        }

        /// <summary>
        /// Verify email
        /// </summary>
        [HttpGet("VerifyEmail")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                return BadRequest(new { error = "Email and token are required" });
            }

            try
            {
                bool success = await _userService.VerifyEmailAsync(email, token);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Email verified successfully. You can now log in."
                    });
                }
                else
                {
                    return BadRequest(new { error = "Invalid or expired verification token" });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error verifying email");
                return StatusCode(500, new { error = "An error occurred while verifying your email" });
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
                return BadRequest(ModelState);
            }

            try
            {
                await _userService.ResendVerificationEmailAsync(model.Email);

                // Don't reveal whether the user exists
                return Ok(new
                {
                    success = true,
                    message = "If your email is registered and not verified, you will receive a verification email shortly."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resending verification email");
                // Still return OK to not reveal whether the email exists
                return Ok(new
                {
                    success = true,
                    message = "If your email is registered and not verified, you will receive a verification email shortly."
                });
            }
        }

        #region Role Management (Admin Only)

        /// <summary>
        /// Get current user's roles
        /// </summary>
        [HttpGet("Roles")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> GetUserRoles()
        {
            try
            {
                int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var roles = await _roleService.GetUserRolesAsync(userId);

                return Ok(roles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user roles");
                return StatusCode(500, new { error = "An error occurred while retrieving roles" });
            }
        }

        /// <summary>
        /// Get roles for a specific user (Admin only)
        /// </summary>
        [HttpGet("Users/{userId}/Roles")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetRolesForUser(int userId)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var roles = await _roleService.GetUserRolesAsync(userId);
                return Ok(roles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting roles for user {userId}");
                return StatusCode(500, new { error = "An error occurred while retrieving roles" });
            }
        }

        /// <summary>
        /// Assign role to user (Admin only)
        /// </summary>
        [HttpPost("Users/{userId}/Roles")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> AssignRoleToUser(int userId, [FromBody] AssignRoleDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                bool success = await _roleService.AssignRoleToUserAsync(userId, model.Role);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = $"Role '{model.Role}' assigned to user successfully"
                    });
                }
                else
                {
                    return BadRequest(new { error = "Failed to assign role" });
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning role to user {userId}");
                return StatusCode(500, new { error = "An error occurred while assigning the role" });
            }
        }

        /// <summary>
        /// Remove role from user (Admin only)
        /// </summary>
        [HttpDelete("Users/{userId}/Roles/{role}")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> RemoveRoleFromUser(int userId, string role)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                bool success = await _roleService.RemoveRoleFromUserAsync(userId, role);

                if (success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = $"Role '{role}' removed from user successfully"
                    });
                }
                else
                {
                    return BadRequest(new { error = "Failed to remove role" });
                }
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing role from user {userId}");
                return StatusCode(500, new { error = "An error occurred while removing the role" });
            }
        }

        /// <summary>
        /// Get users with a specific role (Admin only)
        /// </summary>
        [HttpGet("Roles/{role}/Users")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetUsersInRole(string role)
        {
            try
            {
                var users = await _userService.GetUsersByRoleAsync(role);
                return Ok(users);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting users with role {role}");
                return StatusCode(500, new { error = "An error occurred while retrieving users" });
            }
        }

        /// <summary>
        /// Get all available roles (Admin only)
        /// </summary>
        [HttpGet("Roles/All")]
        [Authorize(Roles = "Admin")]
        public IActionResult GetAllRoles()
        {
            try
            {
                var roles = _roleService.GetAllRoles();
                return Ok(roles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all roles");
                return StatusCode(500, new { error = "An error occurred while retrieving roles" });
            }
        }

        #endregion
    }
}