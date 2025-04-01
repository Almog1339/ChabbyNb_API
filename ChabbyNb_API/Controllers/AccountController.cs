using System;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services;
using ChabbyNb_API.Services.Auth;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly JwtTokenService _jwtTokenService;
        private readonly IRoleService _roleService;
        private readonly IAccountLockoutService _lockoutService;
        private readonly IEmailService _emailService;
        private readonly ILogger<AccountController> _logger;

        public AccountController(
            ChabbyNbDbContext context,
            IConfiguration configuration,
            JwtTokenService jwtTokenService,
            IRoleService roleService,
            IAccountLockoutService lockoutService,
            IEmailService emailService,
            ILogger<AccountController> logger)
        {
            _context = context;
            _configuration = configuration;
            _jwtTokenService = jwtTokenService;
            _roleService = roleService;
            _lockoutService = lockoutService;
            _emailService = emailService;
            _logger = logger;
        }

        // Helper method to hash passwords
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var hash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
                return hash;
            }
        }

        // Helper method to get client IP address
        private string GetClientIpAddress()
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

            // Check if account is locked out
            if (await _lockoutService.IsAccountLockedOutAsync(model.Email))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "This account is temporarily locked due to too many failed login attempts. Please try again later or contact support." });
            }

            User user = null;
            var ipAddress = GetClientIpAddress();

            // Check if user is trying to login with password
            if (!string.IsNullOrEmpty(model.Password))
            {
                // Hash the password for comparison
                string hashedPassword = HashPassword(model.Password);

                // Check if user exists and credentials are valid
                user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email && u.PasswordHash == hashedPassword);

                if (user == null)
                {
                    // Record failed login attempt
                    await _lockoutService.RecordFailedLoginAttemptAsync(model.Email, ipAddress);
                    return BadRequest(new { error = "Invalid login credentials." });
                }

                if (!user.IsEmailVerified)
                {
                    return BadRequest(new { error = "Your email address has not been verified. Please check your email for verification link." });
                }
            }
            // Check if user is trying to login with reservation number
            else if (!string.IsNullOrEmpty(model.ReservationNumber))
            {
                // Find booking with the given reservation number and email
                var booking = await _context.Bookings
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b =>
                        b.ReservationNumber == model.ReservationNumber &&
                        b.User.Email == model.Email);

                if (booking == null)
                {
                    // Record failed login attempt
                    await _lockoutService.RecordFailedLoginAttemptAsync(model.Email, ipAddress);
                    return BadRequest(new { error = "Invalid reservation number or email address." });
                }

                user = booking.User;
            }

            if (user != null)
            {
                // Record successful login
                await _lockoutService.RecordSuccessfulLoginAsync(user.UserID);

                // Generate tokens (JWT + refresh token)
                var tokenResult = await _jwtTokenService.GenerateTokensAsync(user);

                // Get user roles
                var roles = await _roleService.GetUserRolesAsync(user.UserID);

                // For backward compatibility, still store some basic information in session
                if (model.RememberMe)
                {
                    HttpContext.Session.SetInt32("UserID", user.UserID);
                    HttpContext.Session.SetString("FirstName", user.FirstName ?? "");
                    HttpContext.Session.SetString("LastName", user.LastName ?? "");
                    HttpContext.Session.SetString("IsAdmin", user.IsAdmin.ToString());
                }

                return Ok(new LoginResultDto
                {
                    Success = true,
                    Token = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken,
                    TokenExpiration = tokenResult.AccessTokenExpiration,
                    UserId = user.UserID,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    IsAdmin = user.IsAdmin,
                    Roles = roles.Select(r => r.ToString()).ToList()
                });
            }
            else
            {
                // Record failed login attempt
                await _lockoutService.RecordFailedLoginAttemptAsync(model.Email, ipAddress);
                return BadRequest(new { error = "Invalid login attempt. Please check your credentials." });
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
                var ipAddress = GetClientIpAddress();

                // Attempt to refresh the token
                var tokenResult = await _jwtTokenService.RefreshTokenAsync(
                    refreshRequest.RefreshToken,
                    refreshRequest.AccessToken);

                if (tokenResult == null)
                {
                    // Log invalid refresh attempt
                    _logger.LogWarning($"Invalid refresh token attempt from IP {ipAddress}");
                    return Unauthorized(new { error = "Invalid token" });
                }

                // Extract user ID from the new token
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(tokenResult.AccessToken);
                var userIdClaim = jwtToken.Claims.First(claim => claim.Type == ClaimTypes.NameIdentifier).Value;

                if (!int.TryParse(userIdClaim, out int userId))
                {
                    return Unauthorized(new { error = "Invalid token format" });
                }

                // Get user information
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return Unauthorized(new { error = "User not found" });
                }

                // Get user roles
                var roles = await _roleService.GetUserRolesAsync(userId);

                return Ok(new LoginResultDto
                {
                    Success = true,
                    Token = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken,
                    TokenExpiration = tokenResult.AccessTokenExpiration,
                    UserId = user.UserID,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    IsAdmin = user.IsAdmin,
                    Roles = roles.Select(r => r.ToString()).ToList()
                });
            }
            catch (SecurityTokenException ex)
            {
                _logger.LogWarning(ex, "Invalid token during refresh attempt");
                return Unauthorized(new { error = "Invalid token" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while refreshing the token" });
            }
        }

        // POST: api/Account/Logout
        [HttpPost("Logout")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> Logout([FromBody] LogoutDto model)
        {
            // With JWT, we don't need to do anything server-side for basic logout
            // The client should discard the token

            try
            {
                // But we can revoke the refresh token for better security
                if (!string.IsNullOrEmpty(model.RefreshToken))
                {
                    await _jwtTokenService.RevokeTokenAsync(model.RefreshToken);
                }

                // For backward compatibility, clear session
                HttpContext.Session.Clear();

                return Ok(new { success = true, message = "You have been logged out successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during logout");
                return Ok(new { success = true, message = "You have been logged out successfully, but there was an error revoking your refresh token." });
            }
        }

        // POST: api/Account/RevokeAllTokens
        [HttpPost("RevokeAllTokens")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> RevokeAllTokens()
        {
            try
            {
                int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                await _jwtTokenService.RevokeAllUserTokensAsync(userId);
                return Ok(new { success = true, message = "All tokens have been revoked successfully." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking all tokens");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while revoking tokens" });
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
                return Ok(roles.Select(r => r.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user roles");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while retrieving user roles" });
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
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var roles = await _roleService.GetUserRolesAsync(userId);
                return Ok(roles.Select(r => r.ToString()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving roles for user {userId}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while retrieving user roles" });
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
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Make sure the role is valid
                if (!Enum.TryParse<Services.Auth.UserRole>(model.Role, out var roleEnum))
                {
                    return BadRequest(new { error = "Invalid role" });
                }

                // Don't allow assigning Admin role through this endpoint for security
                if (roleEnum == Services.Auth.UserRole.Admin)
                {
                    var isCurrentUserSuperAdmin = User.HasClaim(c => c.Type == "IsSuperAdmin" && c.Value == "True");
                    if (!isCurrentUserSuperAdmin)
                    {
                        return Forbid();
                    }
                }

                var success = await _roleService.AssignRoleToUserAsync(userId, roleEnum);
                if (!success)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to assign role" });
                }

                // Revoke all existing tokens for this user to enforce the new permissions
                await _jwtTokenService.RevokeAllUserTokensAsync(userId);

                return Ok(new { success = true, message = $"Role {roleEnum} assigned to user successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning role to user {userId}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while assigning the role" });
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
                    return NotFound(new { error = "User not found" });
                }

                // Make sure the role is valid
                if (!Enum.TryParse<Services.Auth.UserRole>(role, out var roleEnum))
                {
                    return BadRequest(new { error = "Invalid role" });
                }

                // Don't allow removing Admin role through this endpoint for security
                if (roleEnum == Services.Auth.UserRole.Admin)
                {
                    var isCurrentUserSuperAdmin = User.HasClaim(c => c.Type == "IsSuperAdmin" && c.Value == "True");
                    if (!isCurrentUserSuperAdmin)
                    {
                        return Forbid();
                    }
                }

                var success = await _roleService.RemoveRoleFromUserAsync(userId, roleEnum);
                if (!success)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to remove role" });
                }

                // Revoke all existing tokens for this user to enforce the new permissions
                await _jwtTokenService.RevokeAllUserTokensAsync(userId);

                return Ok(new { success = true, message = $"Role {roleEnum} removed from user successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing role from user {userId}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while removing the role" });
            }
        }

        // GET: api/Account/Roles/{role}/Users
        [HttpGet("Roles/{role}/Users")]
        [Authorize(Policy = "RequireAdminRole")]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetUsersInRole(string role)
        {
            try
            {
                // Make sure the role is valid
                if (!Enum.TryParse<Services.Auth.UserRole>(role, out var roleEnum))
                {
                    return BadRequest(new { error = "Invalid role" });
                }

                var users = await _roleService.GetUsersInRoleAsync(roleEnum);
                var userDtos = users.Select(u => new UserDto
                {
                    UserId = u.UserID,
                    Username = u.Username,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    IsAdmin = u.IsAdmin
                });

                return Ok(userDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving users in role {role}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while retrieving users" });
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
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                var isLocked = await _lockoutService.IsAccountLockedOutAsync(userId);

                // Get active lockout details if locked
                var lockoutDetails = isLocked ?
                    await _context.UserAccountLockouts
                        .Where(l => l.UserId == userId && l.IsActive)
                        .OrderByDescending(l => l.LockoutStart)
                        .Select(l => new
                        {
                            l.LockoutStart,
                            l.LockoutEnd,
                            l.Reason,
                            l.FailedAttempts
                        })
                        .FirstOrDefaultAsync() : null;

                return Ok(new
                {
                    isLocked,
                    lockoutDetails
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error retrieving lockout status for user {userId}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while retrieving lockout status" });
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
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Don't allow locking admin accounts unless you're a super admin
                if (user.IsAdmin)
                {
                    var isCurrentUserSuperAdmin = User.HasClaim(c => c.Type == "IsSuperAdmin" && c.Value == "True");
                    if (!isCurrentUserSuperAdmin)
                    {
                        return Forbid();
                    }
                }

                var success = await _lockoutService.LockoutAccountAsync(
                    userId,
                    model.Reason,
                    GetClientIpAddress(),
                    model.LockoutMinutes);

                if (!success)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to lock account" });
                }

                // Revoke all existing tokens for this user
                await _jwtTokenService.RevokeAllUserTokensAsync(userId);

                return Ok(new { success = true, message = "Account locked successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error locking account for user {userId}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while locking the account" });
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
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                int adminId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var success = await _lockoutService.UnlockAccountAsync(userId, adminId, model.Notes);

                if (!success)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to unlock account" });
                }

                return Ok(new { success = true, message = "Account unlocked successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlocking account for user {userId}");
                return StatusCode(StatusCodes.Status500InternalServerError, new { error = "An error occurred while unlocking the account" });
            }
        }
    }
}