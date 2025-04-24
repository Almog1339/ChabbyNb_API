using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Auth;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services;
using ChabbyNb_API.Controllers;
using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : BaseApiController
    {
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;

        public AccountController(ChabbyNbDbContext context,IEmailService emailService,ILogger<AccountController> logger,IConfiguration configuration)
            : base(context, logger)
        {
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration;
        }

        /// <summary>
        /// Login with email/password or reservation number
        /// </summary>
        [HttpPost("Login")]
        public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto model)
        {
            if (!ModelState.IsValid)
            {
                // Change the return type to match ActionResult<LoginResultDto>
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
        /// Logout user
        /// </summary>
        [HttpPost("Logout")]
        [Authorize]
        public async Task<IActionResult> Logout([FromBody] LogoutDto model)
        {
            try
            {
                var ipAddress = GetClientIpAddress();
                await _authService.RevokeTokenAsync(model.RefreshToken, ipAddress);
                return ApiSuccess("Logged out successfully");
            }
            catch (Exception ex)
            {
                // Still return success even if token revocation fails
                _logger.LogError(ex, "Error during logout");
                return ApiSuccess("Logged out successfully");
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
                if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password))
                {
                    return ValidationError("Email and password are required");
                }

                // Check if email already exists
                var existingUser = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.ToLower());

                if (existingUser != null)
                {
                    return ApiError("A user with this email already exists", null, 409);
                }

                // Check if username already exists
                if (!string.IsNullOrEmpty(model.Username))
                {
                    var existingUsername = await _context.Users
                        .AnyAsync(u => u.Username.ToLower() == model.Username.ToLower());

                    if (existingUsername)
                    {
                        return ApiError("This username is already taken", null, 409);
                    }
                }

                // Create the user
                var newUser = new User
                {
                    Email = model.Email,
                    Username = model.Username ?? model.Email, // Use email as username if not provided
                    PasswordHash = _authService.HashPassword(model.Password),
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    PhoneNumber = model.PhoneNumber,
                    IsAdmin = false, // New users are never admins
                    CreatedDate = DateTime.UtcNow,
                    IsEmailVerified = false // Require email verification
                };

                // Start a transaction
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
                    // Add the user
                    _context.Users.Add(newUser);
                    await _context.SaveChangesAsync();

                    // Create email verification token
                    string verificationToken = _authService.GenerateSecureToken(32);
                    var verification = new EmailVerification
                    {
                        UserID = newUser.UserID,
                        Email = newUser.Email,
                        VerificationToken = verificationToken,
                        ExpiryDate = DateTime.UtcNow.AddDays(3),
                        IsVerified = false,
                        CreatedDate = DateTime.UtcNow
                    };

                    _context.EmailVerifications.Add(verification);
                    await _context.SaveChangesAsync();

                    // Commit the transaction
                    await transaction.CommitAsync();

                    // Send verification email
                    await SendVerificationEmailAsync(newUser, verificationToken);

                    return ApiSuccess("Registration successful. Please check your email to verify your account.",
                        new { userId = newUser.UserID, email = newUser.Email });
                }
                catch (Exception ex)
                {
                    // Roll back the transaction on error
                    await transaction.RollbackAsync();
                    return HandleException(ex, "Error registering user");
                }
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

                var user = await _context.Users.FindAsync(userId.Value);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                // Check if username is changed and already exists
                if (!string.IsNullOrEmpty(model.Username) &&
                    model.Username != user.Username)
                {
                    var existingUsername = await _context.Users
                        .AnyAsync(u => u.Username.ToLower() == model.Username.ToLower() && u.UserID != userId.Value);

                    if (existingUsername)
                    {
                        return ApiError("This username is already taken", null, 409);
                    }
                }

                // Update user properties
                user.Username = model.Username ?? user.Username;
                user.FirstName = model.FirstName ?? user.FirstName;
                user.LastName = model.LastName ?? user.LastName;
                user.PhoneNumber = model.PhoneNumber ?? user.PhoneNumber;

                _context.Users.Update(user);
                await _context.SaveChangesAsync();

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

                var result = await _authService.ChangePasswordAsync(userId.Value, model.CurrentPassword, model.NewPassword);

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
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.ToLower());

                // Don't reveal whether the user exists
                if (user == null)
                {
                    return ApiSuccess("If your email is registered, you will receive a password reset link shortly.");
                }

                // Generate reset token
                var token = await _authService.GeneratePasswordResetTokenAsync(user.UserID);

                // Send password reset email
                await SendPasswordResetEmailAsync(user, token);

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
                // Validate email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.ToLower());

                if (user == null)
                {
                    return ApiError("Invalid or expired reset token", null, 400);
                }

                // Reset the password
                var result = await _authService.ResetPasswordAsync(user.UserID, model.Token, model.Password);

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
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                return ValidationError("Email and token are required");
            }

            try
            {
                // Find user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

                if (user == null)
                {
                    return ApiError("Invalid verification link", null, 400);
                }

                // Find the verification record
                var verification = await _context.EmailVerifications
                    .FirstOrDefaultAsync(v =>
                        v.UserID == user.UserID &&
                        v.VerificationToken == token &&
                        !v.IsVerified &&
                        v.ExpiryDate > DateTime.UtcNow);

                if (verification == null)
                {
                    return ApiError("Invalid or expired verification link", null, 400);
                }

                // Mark as verified
                verification.IsVerified = true;
                verification.VerifiedDate = DateTime.UtcNow;
                _context.EmailVerifications.Update(verification);

                // Update user's email verification status
                user.IsEmailVerified = true;
                _context.Users.Update(user);

                await _context.SaveChangesAsync();

                return ApiSuccess("Email verified successfully. You can now log in.");
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
                // Find user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == model.Email.ToLower());

                // Don't reveal whether the user exists
                if (user == null)
                {
                    return ApiSuccess("If your email is registered and not verified, you will receive a verification email shortly.");
                }

                // Check if already verified
                if (user.IsEmailVerified)
                {
                    return ApiSuccess("Your email is already verified. You can log in.");
                }

                // Generate new token
                string token = _authService.GenerateSecureToken(32);

                // Create or update verification entry
                var verification = await _context.EmailVerifications
                    .FirstOrDefaultAsync(v => v.UserID == user.UserID && !v.IsVerified);

                if (verification == null)
                {
                    // Create new verification
                    verification = new EmailVerification
                    {
                        UserID = user.UserID,
                        Email = user.Email,
                        VerificationToken = token,
                        ExpiryDate = DateTime.UtcNow.AddDays(3),
                        IsVerified = false,
                        CreatedDate = DateTime.UtcNow
                    };
                    _context.EmailVerifications.Add(verification);
                }
                else
                {
                    // Update existing verification
                    verification.VerificationToken = token;
                    verification.ExpiryDate = DateTime.UtcNow.AddDays(3);
                    _context.EmailVerifications.Update(verification);
                }

                await _context.SaveChangesAsync();

                // Send verification email
                await SendVerificationEmailAsync(user, token);

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

        /// <summary>
        /// Get current user's roles
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

                return ApiSuccess("Roles retrieved successfully", new
                {
                    roles = roles.Select(r => r.ToString()),
                    permissions = permissions.Select(p => p.ToString())
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
        [Authorize(Policy = "RequireAdmin")]
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
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> AssignRoleToUser(int userId, string role)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
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
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> RemoveRoleFromUser(int userId, string role)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
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
        /// Set permissions for a partner user (Admin only)
        /// </summary>
        [HttpPost("Users/{userId}/Permissions")]
        [Authorize(Policy = "RequireAdmin")]
        public async Task<IActionResult> SetUserPermissions(int userId, [FromBody] SetPermissionsDto model)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(CreateApiResponse(false, "User not found"));
                }

                // Parse the permissions
                if (!Enum.TryParse<UserPermission>(model.Permissions, true, out var permissions))
                {
                    return ApiError($"Invalid permissions: {model.Permissions}", null, 400);
                }

                // Get admin ID for tracking
                var adminId = GetCurrentUserId().Value;

                // Set the permissions
                var success = await _authService.SetUserPermissionsAsync(userId, permissions, adminId);

                if (success)
                {
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
        /// Get all available roles (Admin only)
        /// </summary>
        [HttpGet("Roles/All")]
        [Authorize(Policy = "RequireAdmin")]
        public IActionResult GetAllRoles()
        {
            try
            {
                var roles = Enum.GetNames(typeof(UserRole));
                var permissions = Enum.GetNames(typeof(UserPermission));

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

        #region Helper Methods

        /// <summary>
        /// Sends a verification email to a user
        /// </summary>
        private async Task SendVerificationEmailAsync(User user, string token)
        {
            try
            {
                // Build verification URL using configuration
                string baseUrl = string.IsNullOrEmpty(_configuration["ApplicationUrl"])
                    ? $"{Request.Scheme}://{Request.Host}"
                    : _configuration["ApplicationUrl"];

                string verifyUrl = $"{baseUrl}/verify-email?email={Uri.EscapeDataString(user.Email)}&token={token}";

                // Create email model
                var model = new
                {
                    UserName = !string.IsNullOrEmpty(user.FirstName) ? user.FirstName : user.Username,
                    VerificationLink = verifyUrl,
                    ExpiryHours = 72 // 3 days in hours
                };

                // Send email
                await _emailService.SendEmailAsync(
                    user.Email,
                    "Verify Your ChabbyNb Account",
                    "EmailVerification",
                    model
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending verification email to {user.Email}");
                // Continue without throwing - verification can be re-sent later
            }
        }

        /// <summary>
        /// Sends a password reset email
        /// </summary>
        private async Task SendPasswordResetEmailAsync(User user, string token)
        {
            try
            {
                // Build reset URL using configuration
                string baseUrl = string.IsNullOrEmpty(_configuration["ApplicationUrl"])
                    ? $"{Request.Scheme}://{Request.Host}"
                    : _configuration["ApplicationUrl"];

                string resetUrl = $"{baseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={token}";

                // Create email model
                var model = new
                {
                    UserName = !string.IsNullOrEmpty(user.FirstName) ? user.FirstName : user.Username,
                    ResetLink = resetUrl,
                    ExpiryHours = 24
                };

                // Send email
                await _emailService.SendEmailAsync(
                    user.Email,
                    "Reset Your ChabbyNb Password",
                    "PasswordReset",
                    model
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error sending password reset email to {user.Email}");
                // Continue without throwing - password reset can be re-initiated later
            }
        }

        #endregion
    }

    public class SetPermissionsDto
    {
        [Required]
        public string Permissions { get; set; }
    }
}