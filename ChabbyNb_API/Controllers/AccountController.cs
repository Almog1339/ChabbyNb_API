using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Interfaces;
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

        // POST: api/Account/Login
        [HttpPost("Login")]
        public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto model)
        {
            // Custom validation to ensure either password or reservation number is provided
            if (!model.IsValid())
            {
                return BadRequest(new { error = "Either Password or Reservation Number is required." });
            }

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
                return StatusCode(403, new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during login");
                return StatusCode(500, new { error = "An error occurred during login." });
            }
        }

        // POST: api/Account/RefreshToken
        [HttpPost("RefreshToken")]
        public async Task<ActionResult<LoginResultDto>> RefreshToken([FromBody] RefreshTokenDto refreshRequest)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var result = await _userService.RefreshTokenAsync(refreshRequest);
                return Ok(result);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger.LogWarning(ex, "Invalid refresh token attempt");
                return Unauthorized(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return StatusCode(500, new { error = "An error occurred while refreshing the token" });
            }
        }

        // POST: api/Account/Logout
        [HttpPost("Logout")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Logout([FromBody] LogoutDto model)
        {
            try
            {
                await _userService.LogoutAsync(model);
                return Ok(new { success = true, message = "You have been logged out successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return Ok(new { success = true, message = "You have been logged out successfully, but there was an error revoking your refresh token." });
            }
        }

        // POST: api/Account/Register
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
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during registration");
                return StatusCode(500, new { error = "An error occurred during registration." });
            }
        }

        // PUT: api/Account/Profile
        [HttpPut("Profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            try
            {
                var user = await _userService.UpdateProfileAsync(userId, model);
                return Ok(new
                {
                    success = true,
                    message = "Profile updated successfully.",
                    user = new
                    {
                        userId = user.UserID,
                        email = user.Email,
                        username = user.Username,
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
                return StatusCode(500, new { error = "An error occurred while updating your profile." });
            }
        }

        // PUT: api/Account/ChangePassword
        [HttpPut("ChangePassword")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));

            try
            {
                await _userService.ChangePasswordAsync(userId, model);
                return Ok(new { success = true, message = "Password changed successfully. Please log in again with your new password." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error changing password");
                return StatusCode(500, new { error = "An error occurred while changing your password." });
            }
        }

        // POST: api/Account/ForgotPassword
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
                return Ok(new { success = true, message = "If your email is registered, you will receive a password reset link shortly." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initiating password reset");
                // Still return OK to not reveal whether the email exists
                return Ok(new { success = true, message = "If your email is registered, you will receive a password reset link shortly." });
            }
        }

        // POST: api/Account/ResetPassword
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
                return Ok(new { success = true, message = "Password has been reset successfully. You can now log in with your new password." });
            }
            catch (UnauthorizedAccessException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password");
                return StatusCode(500, new { error = "An error occurred while resetting your password." });
            }
        }

        // GET: api/Account/Roles
        [HttpGet("Roles")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<IEnumerable<string>>> GetUserRoles()
        {
            try
            {
                int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var roles = await _roleService.GetUserRolesAsync(userId);
                return Ok(roles);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user roles");
                return StatusCode(500, new { error = "An error occurred while retrieving user roles" });
            }
        }

        // ROLE MANAGEMENT ENDPOINTS (ADMIN ONLY)

        // GET: api/Account/Users/{userId}/Roles
        [HttpGet("Users/{userId}/Roles")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<IEnumerable<string>>> GetUserRolesById(int userId)
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
                _logger.LogError(ex, $"Error retrieving roles for user {userId}");
                return StatusCode(500, new { error = "An error occurred while retrieving user roles" });
            }
        }

        // POST: api/Account/Users/{userId}/Roles
        [HttpPost("Users/{userId}/Roles")]
        [Authorize(Policy = "RequireAdminRole")]
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

                // Don't allow assigning Admin role through this endpoint for security
                if (model.Role == "Admin")
                {
                    var isCurrentUserSuperAdmin = User.HasClaim(c => c.Type == "IsSuperAdmin" && c.Value == "True");
                    if (!isCurrentUserSuperAdmin)
                    {
                        return Forbid();
                    }
                }

                var success = await _roleService.AssignRoleToUserAsync(userId, model.Role);
                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to assign role" });
                }

                return Ok(new { success = true, message = $"Role {model.Role} assigned to user successfully" });
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

        // DELETE: api/Account/Users/{userId}/Roles/{role}
        [HttpDelete("Users/{userId}/Roles/{role}")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> RemoveRoleFromUser(int userId, string role)
        {
            try
            {
                var user = await _userService.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Don't allow removing Admin role through this endpoint for security
                if (role == "Admin")
                {
                    var isCurrentUserSuperAdmin = User.HasClaim(c => c.Type == "IsSuperAdmin" && c.Value == "True");
                    if (!isCurrentUserSuperAdmin)
                    {
                        return Forbid();
                    }
                }

                var success = await _roleService.RemoveRoleFromUserAsync(userId, role);
                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to remove role" });
                }

                return Ok(new { success = true, message = $"Role {role} removed from user successfully" });
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

        // GET: api/Account/Roles/{role}/Users
        [HttpGet("Roles/{role}/Users")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersInRole(string role)
        {
            try
            {
                var users = await _roleService.GetUsersInRoleAsync(role);
                return Ok(users);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving users in role {role}");
                return StatusCode(500, new { error = "An error occurred while retrieving users" });
            }
        }

        // ACCOUNT LOCKOUT MANAGEMENT (ADMIN ONLY)

        // GET: api/Account/Users/{userId}/LockoutStatus
        [HttpGet("Users/{userId}/LockoutStatus")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> GetUserLockoutStatus(int userId)
        {
            try
            {
                var lockoutStatus = await _userService.GetUserLockoutStatusAsync(userId);
                return Ok(lockoutStatus);
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving lockout status for user {userId}");
                return StatusCode(500, new { error = "An error occurred while retrieving lockout status" });
            }
        }

        // POST: api/Account/Users/{userId}/Lock
        [HttpPost("Users/{userId}/Lock")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> LockUserAccount(int userId, [FromBody] LockAccountDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var success = await _userService.LockUserAccountAsync(userId, model);
                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to lock account" });
                }

                return Ok(new { success = true, message = "Account locked successfully" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error locking account for user {userId}");
                return StatusCode(500, new { error = "An error occurred while locking the account" });
            }
        }

        // POST: api/Account/Users/{userId}/Unlock
        [HttpPost("Users/{userId}/Unlock")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<IActionResult> UnlockUserAccount(int userId, [FromBody] UnlockAccountDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var success = await _userService.UnlockUserAccountAsync(userId, model, adminId);

                if (!success)
                {
                    return StatusCode(500, new { error = "Failed to unlock account" });
                }

                return Ok(new { success = true, message = "Account unlocked successfully" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlocking account for user {userId}");
                return StatusCode(500, new { error = "An error occurred while unlocking the account" });
            }
        }
    }
}