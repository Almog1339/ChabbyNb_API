using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Services.Auth
{
    /// <summary>
    /// Result of token operations containing tokens and expiration information
    /// </summary>
    public class TokenResult
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime AccessTokenExpiration { get; set; }
        public DateTime RefreshTokenExpiration { get; set; }
    }

    /// <summary>
    /// Class representing a security event for audit logging
    /// </summary>
    public class SecurityEvent
    {
        public int UserId { get; set; }
        public string EventType { get; set; }
        public DateTime Timestamp { get; set; }
        public string IpAddress { get; set; }
        public string Details { get; set; }
    }

    /// <summary>
    /// Comprehensive authentication service that handles all aspects of user authentication,
    /// including JWT token management, refresh tokens, account security, and role-based access control.
    /// </summary>
    public interface IAuthService
    {
        // Authentication methods
        Task<LoginResultDto> AuthenticateAsync(LoginDto loginDto, string ipAddress);
        Task<LoginResultDto> AuthenticateWithReservationAsync(string email, string reservationNumber, string ipAddress);

        // Token management
        Task<TokenResult> GenerateTokensAsync(User user, IEnumerable<UserRole> roles, IEnumerable<UserPermission> permissions, string ipAddress);
        Task<TokenResult> RefreshTokenAsync(string refreshToken, string accessToken, string ipAddress);
        Task<bool> RevokeTokenAsync(string refreshToken, string ipAddress, string reason = "Explicit logout");
        Task<bool> RevokeAllUserTokensAsync(int userId, string ipAddress, string reason = "Administrative action");
        bool ValidateAccessToken(string token, out ClaimsPrincipal principal);

        // Account security
        Task<bool> IsAccountLockedOutAsync(int userId);
        Task<bool> IsAccountLockedOutAsync(string email);
        Task<bool> RecordFailedLoginAttemptAsync(string email, string ipAddress);
        Task<bool> RecordSuccessfulLoginAsync(int userId, string ipAddress);
        Task<bool> LockoutAccountAsync(int userId, string reason, string ipAddress, int? minutes = null);
        Task<bool> UnlockAccountAsync(int userId, int adminId, string notes, string ipAddress);

        // Password management
        string HashPassword(string password);
        bool VerifyPassword(string password, string hashedPassword);
        Task<string> GeneratePasswordResetTokenAsync(int userId);
        Task<bool> ValidatePasswordResetTokenAsync(int userId, string token);
        Task<bool> ResetPasswordAsync(int userId, string token, string newPassword);
        Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword);

        // User role/permission management
        Task<IEnumerable<UserRole>> GetUserRolesAsync(int userId);
        Task<UserRole> GetHighestRoleAsync(int userId);
        Task<IEnumerable<UserPermission>> GetUserPermissionsAsync(int userId);
        Task<bool> HasPermissionAsync(int userId, UserPermission permission);
        Task<bool> HasRoleAsync(int userId, UserRole minimumRole);
        Task<bool> AssignRoleToUserAsync(int userId, UserRole role, int adminId);
        Task<bool> RemoveRoleFromUserAsync(int userId, UserRole role, int adminId);
        Task<bool> SetUserPermissionsAsync(int userId, UserPermission permissions, int adminId);

        // Utility methods
        Task<SecurityEvent> LogSecurityEventAsync(int userId, string eventType, string ipAddress, string details = null);
        string GenerateSecureToken(int length = 32);
    }

    /// <summary>
    /// Implementation of the comprehensive authentication service
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        // Configuration values
        private readonly string _jwtKey;
        private readonly string _jwtIssuer;
        private readonly string _jwtAudience;
        private readonly int _jwtExpiryMinutes;
        private readonly int _refreshTokenExpiryDays;
        private readonly int _maxFailedAttempts;
        private readonly int _defaultLockoutMinutes;

        public AuthService(
            ChabbyNbDbContext context,
            IConfiguration configuration,
            ILogger<AuthService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));

            // Load settings from configuration
            _jwtKey = _configuration["Jwt:Key"];
            _jwtIssuer = _configuration["Jwt:Issuer"];
            _jwtAudience = _configuration["Jwt:Audience"];
            _jwtExpiryMinutes = _configuration.GetValue<int>("Jwt:AccessTokenExpiryInMinutes", 60);
            _refreshTokenExpiryDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpiryInDays", 7);
            _maxFailedAttempts = _configuration.GetValue<int>("Security:MaxFailedLoginAttempts", 5);
            _defaultLockoutMinutes = _configuration.GetValue<int>("Security:DefaultLockoutMinutes", 15);

            // Validate required settings
            if (string.IsNullOrEmpty(_jwtKey) || string.IsNullOrEmpty(_jwtIssuer) || string.IsNullOrEmpty(_jwtAudience))
            {
                throw new InvalidOperationException("JWT configuration is incomplete. Please check your appsettings.json file.");
            }
        }

        #region Authentication Methods

        /// <summary>
        /// Authenticates a user with email and password
        /// </summary>
        public async Task<LoginResultDto> AuthenticateAsync(LoginDto loginDto, string ipAddress)
        {
            // Email is always required for authentication
            if (string.IsNullOrEmpty(loginDto.Email))
            {
                throw new ArgumentException("Email is required");
            }

            // Custom validation for password or reservation
            if (string.IsNullOrEmpty(loginDto.Password) && string.IsNullOrEmpty(loginDto.ReservationNumber))
            {
                throw new ArgumentException("Either Password or Reservation Number is required");
            }

            // Check if account is locked out
            if (await IsAccountLockedOutAsync(loginDto.Email))
            {
                throw new UnauthorizedAccessException("This account is temporarily locked. Please try again later or contact support.");
            }

            // Handle login with password
            if (!string.IsNullOrEmpty(loginDto.Password))
            {
                var hashedPassword = HashPassword(loginDto.Password);
                var user = await _context.Users.FirstOrDefaultAsync(u =>
                    u.Email.ToLower() == loginDto.Email.ToLower() &&
                    u.PasswordHash == hashedPassword);

                if (user == null)
                {
                    // Record failed login attempt
                    await RecordFailedLoginAttemptAsync(loginDto.Email, ipAddress);
                    throw new UnauthorizedAccessException("Invalid email or password");
                }

                if (!user.IsEmailVerified)
                {
                    throw new UnauthorizedAccessException("Please verify your email before logging in");
                }

                // Record successful login
                await RecordSuccessfulLoginAsync(user.UserID, ipAddress);

                // Get user roles and permissions
                var roles = await GetUserRolesAsync(user.UserID);
                var permissions = await GetUserPermissionsAsync(user.UserID);

                // Generate tokens
                var tokenResult = await GenerateTokensAsync(user, roles, permissions, ipAddress);

                // Create login result
                return new LoginResultDto
                {
                    Success = true,
                    Message = "Login successful",
                    Token = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken,
                    TokenExpiration = tokenResult.AccessTokenExpiration,
                    UserId = user.UserID,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    IsAdmin = roles.Contains(UserRole.Admin),
                    Roles = roles.Select(r => r.ToString()).ToList()
                };
            }
            else
            {
                // Handle login with reservation number
                return await AuthenticateWithReservationAsync(loginDto.Email, loginDto.ReservationNumber, ipAddress);
            }
        }

        /// <summary>
        /// Authenticates a user with a reservation number
        /// </summary>
        public async Task<LoginResultDto> AuthenticateWithReservationAsync(string email, string reservationNumber, string ipAddress)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(reservationNumber))
            {
                throw new ArgumentException("Email and reservation number are required");
            }

            try
            {
                // First find the user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

                if (user == null)
                {
                    // Record failed login attempt
                    await RecordFailedLoginAttemptAsync(email, ipAddress);
                    throw new UnauthorizedAccessException("Invalid email or reservation number");
                }

                // Then find the booking by reservation number and verify it belongs to the user
                var booking = await _context.Bookings
                    .FirstOrDefaultAsync(b =>
                        b.ReservationNumber == reservationNumber &&
                        b.UserID == user.UserID);

                if (booking == null)
                {
                    // Record failed login attempt
                    await RecordFailedLoginAttemptAsync(email, ipAddress);
                    throw new UnauthorizedAccessException("Invalid email or reservation number");
                }

                // Record successful login
                await RecordSuccessfulLoginAsync(user.UserID, ipAddress);

                // Get user roles and permissions
                var roles = await GetUserRolesAsync(user.UserID);
                var permissions = await GetUserPermissionsAsync(user.UserID);

                // Generate tokens
                var tokenResult = await GenerateTokensAsync(user, roles, permissions, ipAddress);

                // Create login result
                return new LoginResultDto
                {
                    Success = true,
                    Message = "Login successful",
                    Token = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken,
                    TokenExpiration = tokenResult.AccessTokenExpiration,
                    UserId = user.UserID,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    IsAdmin = roles.Contains(UserRole.Admin),
                    Roles = roles.Select(r => r.ToString()).ToList()
                };
            }
            catch (UnauthorizedAccessException)
            {
                throw; // Rethrow specific unauthorized exceptions
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error during reservation authentication: {ex.Message}");
                throw new UnauthorizedAccessException("Authentication failed");
            }
        }

        #endregion

        #region Token Management

        /// <summary>
        /// Generates both access token and refresh token for a user
        /// </summary>
        public async Task<TokenResult> GenerateTokensAsync(User user, IEnumerable<UserRole> roles, IEnumerable<UserPermission> permissions, string ipAddress)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            // Set token expiration times
            var accessTokenExpiration = DateTime.UtcNow.AddMinutes(_jwtExpiryMinutes);
            var refreshTokenExpiration = DateTime.UtcNow.AddDays(_refreshTokenExpiryDays);

            // Create claims for the token
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Username ?? user.Email)
            };

            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role.ToString()));
            }

            // Legacy IsAdmin claim for backward compatibility
            claims.Add(new Claim("IsAdmin", roles.Contains(UserRole.Admin).ToString()));

            // Add permissions as a single claim
            int permissionsValue = permissions.Aggregate(0, (current, permission) => current | (int)permission);
            claims.Add(new Claim("Permissions", permissionsValue.ToString()));

            // Add name claims if available
            if (!string.IsNullOrEmpty(user.FirstName))
                claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));

            if (!string.IsNullOrEmpty(user.LastName))
                claims.Add(new Claim(ClaimTypes.Surname, user.LastName));

            // Add unique token ID
            var tokenId = Guid.NewGuid().ToString();
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, tokenId));

            // Create JWT token
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var jwtToken = new JwtSecurityToken(
                issuer: _jwtIssuer,
                audience: _jwtAudience,
                claims: claims,
                expires: accessTokenExpiration,
                signingCredentials: creds
            );

            // Generate the access token
            var accessToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);

            // Generate refresh token
            var refreshToken = GenerateSecureToken();

            // Save refresh token to database
            var tokenEntity = new RefreshToken
            {
                Token = refreshToken,
                JwtId = tokenId,
                UserId = user.UserID,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = refreshTokenExpiration,
                IsRevoked = false,
                CreatedByIp = ipAddress
            };

            _context.RefreshTokens.Add(tokenEntity);
            await _context.SaveChangesAsync();

            // Log the event
            await LogSecurityEventAsync(
                user.UserID,
                "TokenGenerated",
                ipAddress,
                $"Access token expires: {accessTokenExpiration}");

            return new TokenResult
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiration = accessTokenExpiration,
                RefreshTokenExpiration = refreshTokenExpiration
            };
        }

        /// <summary>
        /// Refreshes an access token using a refresh token
        /// </summary>
        public async Task<TokenResult> RefreshTokenAsync(string refreshToken, string accessToken, string ipAddress)
        {
            if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentException("Refresh token and access token are required");
            }

            // Validate the access token (even if expired)
            ClaimsPrincipal principal;
            var validatedToken = ValidateAccessTokenNoLifetime(accessToken, out principal);

            if (validatedToken == null)
            {
                throw new SecurityTokenException("Invalid access token");
            }

            // Extract claims from the token
            var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var userIdString = principal.FindFirstValue(ClaimTypes.NameIdentifier);

            if (!int.TryParse(userIdString, out int userId))
            {
                throw new SecurityTokenException("Invalid user ID in token");
            }

            // Check if the refresh token exists and is valid
            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t =>
                    t.Token == refreshToken &&
                    t.UserId == userId &&
                    t.JwtId == jti);

            if (storedToken == null)
            {
                _logger.LogWarning($"Invalid refresh token attempt for user {userId}");
                throw new SecurityTokenException("Invalid refresh token");
            }

            if (storedToken.IsRevoked)
            {
                _logger.LogWarning($"Attempt to use revoked refresh token for user {userId}");
                throw new SecurityTokenException("Refresh token has been revoked");
            }

            if (storedToken.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning($"Attempt to use expired refresh token for user {userId}");
                throw new SecurityTokenException("Refresh token has expired");
            }

            // Get the user
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning($"User {userId} not found for refresh token");
                throw new SecurityTokenException("User not found");
            }

            // Extract roles from claims
            var roleClaims = principal.FindAll(c => c.Type == ClaimTypes.Role);
            var roles = roleClaims.Select(c => Enum.Parse<UserRole>(c.Value)).ToList();

            // Extract permissions from claims
            var permissionsClaim = principal.FindFirstValue("Permissions");
            var permissions = new List<UserPermission>();
            if (int.TryParse(permissionsClaim, out int permissionsValue))
            {
                foreach (UserPermission permission in Enum.GetValues(typeof(UserPermission)))
                {
                    if ((permissionsValue & (int)permission) == (int)permission && permission != UserPermission.None)
                    {
                        permissions.Add(permission);
                    }
                }
            }

            // Revoke the current refresh token
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = ipAddress;
            storedToken.ReasonRevoked = "Replaced by new token";
            storedToken.ReplacedByToken = "Pending generation";

            _context.RefreshTokens.Update(storedToken);
            await _context.SaveChangesAsync();

            // Generate new tokens
            var tokenResult = await GenerateTokensAsync(user, roles, permissions, ipAddress);

            // Update the replaced by token reference
            storedToken.ReplacedByToken = tokenResult.RefreshToken;
            await _context.SaveChangesAsync();

            // Log the event
            await LogSecurityEventAsync(
                userId,
                "TokenRefreshed",
                ipAddress,
                $"Old token: {refreshToken.Substring(0, 10)}...");

            return tokenResult;
        }

        /// <summary>
        /// Revokes a refresh token
        /// </summary>
        public async Task<bool> RevokeTokenAsync(string refreshToken, string ipAddress, string reason = "Explicit logout")
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                return false;
            }

            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken);

            if (storedToken == null)
            {
                _logger.LogWarning($"Attempt to revoke non-existent refresh token");
                return false;
            }

            if (storedToken.IsRevoked)
            {
                _logger.LogInformation($"Token {refreshToken.Substring(0, 10)}... already revoked");
                return true; // Already revoked
            }

            // Revoke the token
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = ipAddress;
            storedToken.ReasonRevoked = reason;

            _context.RefreshTokens.Update(storedToken);
            await _context.SaveChangesAsync();

            // Log the event
            await LogSecurityEventAsync(
                storedToken.UserId,
                "TokenRevoked",
                ipAddress,
                $"Reason: {reason}");

            _logger.LogInformation($"Token {refreshToken.Substring(0, 10)}... revoked for user {storedToken.UserId}");

            return true;
        }

        /// <summary>
        /// Revokes all refresh tokens for a user
        /// </summary>
        public async Task<bool> RevokeAllUserTokensAsync(int userId, string ipAddress, string reason = "Administrative action")
        {
            var activeTokens = await _context.RefreshTokens
                .Where(t => t.UserId == userId && !t.IsRevoked)
                .ToListAsync();

            if (!activeTokens.Any())
            {
                _logger.LogInformation($"No active tokens to revoke for user {userId}");
                return true; // No active tokens to revoke
            }

            foreach (var token in activeTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedByIp = ipAddress;
                token.ReasonRevoked = reason;
            }

            await _context.SaveChangesAsync();

            // Log the event
            await LogSecurityEventAsync(
                userId,
                "AllTokensRevoked",
                ipAddress,
                $"Revoked {activeTokens.Count} tokens. Reason: {reason}");

            _logger.LogInformation($"All {activeTokens.Count} tokens revoked for user {userId}");

            return true;
        }

        /// <summary>
        /// Validates an access token
        /// </summary>
        public bool ValidateAccessToken(string token, out ClaimsPrincipal principal)
        {
            principal = null;

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Attempted to validate null or empty token");
                return false;
            }

            try
            {
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = _jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = _jwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);

                // Ensure token's signing algorithm is correct
                var jwtToken = validatedToken as JwtSecurityToken;
                if (jwtToken == null || !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Token validation failed - invalid algorithm");
                    return false;
                }

                return true;
            }
            catch (SecurityTokenExpiredException)
            {
                _logger.LogInformation("Token validation failed - token expired");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating access token");
                return false;
            }
        }

        /// <summary>
        /// Validates an access token without checking its lifetime (for token refresh)
        /// </summary>
        private JwtSecurityToken ValidateAccessTokenNoLifetime(string token, out ClaimsPrincipal principal)
        {
            principal = null;

            try
            {
                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = _jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = _jwtAudience,
                    ValidateLifetime = false, // Don't care about expiration for refresh token validation
                    ClockSkew = TimeSpan.Zero
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);

                // Ensure token's signing algorithm is correct
                var jwtToken = validatedToken as JwtSecurityToken;
                if (jwtToken == null || !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("Token validation failed - invalid algorithm");
                    return null;
                }

                return jwtToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating access token for refresh");
                return null;
            }
        }

        #endregion

        #region Account Lockout Management

        /// <summary>
        /// Checks if a user account is locked out
        /// </summary>
        public async Task<bool> IsAccountLockedOutAsync(int userId)
        {
            try
            {
                var lockout = await _context.UserAccountLockouts
                    .Where(l => l.UserId == userId && l.IsActive)
                    .OrderByDescending(l => l.LockoutStart)
                    .FirstOrDefaultAsync();

                if (lockout == null)
                {
                    return false; // No active lockout
                }

                // Check if lockout has expired
                if (lockout.LockoutEnd != null && lockout.LockoutEnd <= DateTime.UtcNow)
                {
                    // Lockout has expired, update the record
                    lockout.IsActive = false;
                    await _context.SaveChangesAsync();
                    return false;
                }

                // Account is locked out
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking account lockout for user ID {userId}");
                return false; // Default to allowing access in case of error
            }
        }

        /// <summary>
        /// Checks if a user account is locked out by email
        /// </summary>
        public async Task<bool> IsAccountLockedOutAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            try
            {
                // Find the user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

                if (user == null)
                {
                    return false; // User doesn't exist, so not locked out
                }

                // Check if the user's account is locked
                return await IsAccountLockedOutAsync(user.UserID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking account lockout for email {email}");
                return false; // Default to allowing access in case of error
            }
        }

        /// <summary>
        /// Records a failed login attempt and locks the account if necessary
        /// </summary>
        public async Task<bool> RecordFailedLoginAttemptAsync(string email, string ipAddress)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            try
            {
                // Find the user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

                // Record the event even if user doesn't exist (for security auditing)
                var securityEvent = new UserSecurityEvent
                {
                    UserId = user?.UserID ?? 0, // Use 0 for non-existent users
                    EventType = "FailedLogin",
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = user == null
                        ? $"Failed login attempt for non-existent email: {email}"
                        : "Failed login attempt"
                };

                _context.UserSecurityEvents.Add(securityEvent);
                await _context.SaveChangesAsync();

                // If user doesn't exist, just return (we've logged the attempt)
                if (user == null)
                {
                    return true;
                }

                // Check if the account is already locked
                if (await IsAccountLockedOutAsync(user.UserID))
                {
                    // Log attempt on locked account but don't increment counters
                    var lockedEvent = new UserSecurityEvent
                    {
                        UserId = user.UserID,
                        EventType = "FailedLoginWhenLocked",
                        EventTime = DateTime.UtcNow,
                        IpAddress = ipAddress,
                        AdditionalInfo = "Failed login attempt on locked account"
                    };

                    _context.UserSecurityEvents.Add(lockedEvent);
                    await _context.SaveChangesAsync();
                    return true;
                }

                // Count recent failed attempts (last 30 minutes)
                var recentFailures = await _context.UserSecurityEvents
                    .CountAsync(e => e.UserId == user.UserID &&
                           e.EventType == "FailedLogin" &&
                           e.EventTime > DateTime.UtcNow.AddMinutes(-30));

                // If too many failures, lock the account
                if (recentFailures + 1 >= _maxFailedAttempts) // +1 to count the current failure
                {
                    await LockoutAccountAsync(
                        user.UserID,
                        $"Too many failed login attempts ({recentFailures + 1})",
                        ipAddress,
                        _defaultLockoutMinutes);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording failed login attempt for email {email}");
                return false;
            }
        }

        /// <summary>
        /// Records a successful login and resets failed attempt counters
        /// </summary>
        public async Task<bool> RecordSuccessfulLoginAsync(int userId, string ipAddress)
        {
            try
            {
                // Log the successful login
                var successEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "SuccessfulLogin",
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = "Successful login"
                };

                _context.UserSecurityEvents.Add(successEvent);
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording successful login for user ID {userId}");
                return false;
            }
        }

        /// <summary>
        /// Manually locks an account
        /// </summary>
        public async Task<bool> LockoutAccountAsync(int userId, string reason, string ipAddress, int? minutes = null)
        {
            try
            {
                // Validate user exists
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"Attempted to lock non-existent user with ID {userId}");
                    return false;
                }

                // Set lockout duration
                var lockoutMinutes = minutes ?? _defaultLockoutMinutes;
                var lockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);

                // Get recent failed attempts count for context
                int failedAttempts = await _context.UserSecurityEvents
                    .CountAsync(e => e.UserId == userId &&
                           e.EventType == "FailedLogin" &&
                           e.EventTime > DateTime.UtcNow.AddMinutes(-30));

                // Create lockout record
                var lockout = new UserAccountLockout
                {
                    UserId = userId,
                    LockoutStart = DateTime.UtcNow,
                    LockoutEnd = lockoutEnd,
                    Reason = reason,
                    IpAddress = ipAddress,
                    FailedAttempts = failedAttempts,
                    IsActive = true
                };

                _context.UserAccountLockouts.Add(lockout);

                // Record security event
                var lockoutEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "AccountLockout",
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = $"Account locked for {lockoutMinutes} minutes. Reason: {reason}"
                };

                _context.UserSecurityEvents.Add(lockoutEvent);
                await _context.SaveChangesAsync();

                _logger.LogWarning($"Account locked for user ID {userId}. Reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error locking account for user ID {userId}");
                return false;
            }
        }

        /// <summary>
        /// Unlocks a locked account
        /// </summary>
        public async Task<bool> UnlockAccountAsync(int userId, int adminId, string notes, string ipAddress)
        {
            try
            {
                // Find the active lockout
                var activeLockout = await _context.UserAccountLockouts
                    .Where(l => l.UserId == userId && l.IsActive)
                    .OrderByDescending(l => l.LockoutStart)
                    .FirstOrDefaultAsync();

                if (activeLockout == null)
                {
                    _logger.LogWarning($"Attempted to unlock account for user ID {userId}, but no active lockout found");
                    return false;
                }

                // Update lockout record
                activeLockout.IsActive = false;
                activeLockout.UnlockedAt = DateTime.UtcNow;
                activeLockout.UnlockedByAdminId = adminId.ToString();
                activeLockout.Notes = notes;

                // Record security event
                var unlockEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "AccountUnlock",
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = $"Account unlocked by admin ID {adminId}. Notes: {notes}"
                };

                _context.UserSecurityEvents.Add(unlockEvent);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Account unlocked for user ID {userId} by admin ID {adminId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlocking account for user ID {userId}");
                return false;
            }
        }

        #endregion

        #region Password Management

        /// <summary>
        /// Hashes a password using SHA256
        /// </summary>
        public string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be null or empty", nameof(password));
            }

            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var hash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
                return hash;
            }
        }

        /// <summary>
        /// Verifies a password against a hash
        /// </summary>
        public bool VerifyPassword(string password, string hashedPassword)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(hashedPassword))
            {
                return false;
            }

            // Hash the input password and compare with stored hash
            string hashedInput = HashPassword(password);
            return hashedInput == hashedPassword;
        }

        /// <summary>
        /// Generates a password reset token and stores it
        /// </summary>
        public async Task<string> GeneratePasswordResetTokenAsync(int userId)
        {
            // Generate a secure random token
            string token = GenerateSecureToken();

            // Create or update temporary password entry
            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t => t.UserID == userId && !t.IsUsed);

            if (tempPassword == null)
            {
                // Create new entry
                tempPassword = new Tempwd
                {
                    UserID = userId,
                    Token = token,
                    ExperationTime = DateTime.UtcNow.AddHours(24),
                    IsUsed = false
                };
                _context.Tempwds.Add(tempPassword);
            }
            else
            {
                // Update existing entry
                tempPassword.Token = token;
                tempPassword.ExperationTime = DateTime.UtcNow.AddHours(24);
                tempPassword.IsUsed = false;
                _context.Tempwds.Update(tempPassword);
            }

            await _context.SaveChangesAsync();
            return token;
        }

        /// <summary>
        /// Validates a password reset token
        /// </summary>
        public async Task<bool> ValidatePasswordResetTokenAsync(int userId, string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return false;
            }

            // Find the token
            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t =>
                    t.UserID == userId &&
                    t.Token == token &&
                    !t.IsUsed &&
                    t.ExperationTime > DateTime.UtcNow);

            return tempPassword != null;
        }

        /// <summary>
        /// Resets a user's password using a token
        /// </summary>
        public async Task<bool> ResetPasswordAsync(int userId, string token, string newPassword)
        {
            // Validate token
            if (!await ValidatePasswordResetTokenAsync(userId, token))
            {
                return false;
            }

            // Find the user
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            // Update the password
            user.PasswordHash = HashPassword(newPassword);
            _context.Users.Update(user);

            // Mark the token as used
            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t =>
                    t.UserID == userId &&
                    t.Token == token &&
                    !t.IsUsed);

            if (tempPassword != null)
            {
                tempPassword.IsUsed = true;
                _context.Tempwds.Update(tempPassword);
            }

            await _context.SaveChangesAsync();

            // Revoke all existing refresh tokens for security
            await RevokeAllUserTokensAsync(userId, "Password reset", "Password reset");

            return true;
        }

        /// <summary>
        /// Changes a user's password (requires current password verification)
        /// </summary>
        public async Task<bool> ChangePasswordAsync(int userId, string currentPassword, string newPassword)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            // Verify current password
            if (!VerifyPassword(currentPassword, user.PasswordHash))
            {
                return false;
            }

            // Update password
            user.PasswordHash = HashPassword(newPassword);
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            // Revoke all existing refresh tokens for security
            await RevokeAllUserTokensAsync(userId, "Password changed", "Password changed by user");

            return true;
        }

        #endregion

        #region Role and Permission Management

        /// <summary>
        /// Gets all roles assigned to a user
        /// </summary>
        public async Task<IEnumerable<UserRole>> GetUserRolesAsync(int userId)
        {
            // Get the user to check legacy IsAdmin flag
            var user = await _context.Users.FindAsync(userId);

            if (user == null)
            {
                return new List<UserRole> { UserRole.Everyone };
            }

            var roles = new List<UserRole>();

            // Every authenticated user gets the Guest role
            roles.Add(UserRole.Guest);

            // Add CleaningStaff role (in a real implementation, this would be stored in a database table)
            // For now, we'll simulate this by checking username
            if (user.Username?.Contains("cleaner", StringComparison.OrdinalIgnoreCase) == true)
            {
                roles.Add(UserRole.CleaningStaff);
            }

            // Add Partner role (in a real implementation, this would be stored in a database table)
            // For now, we'll simulate this by checking username
            if (user.Username?.Contains("partner", StringComparison.OrdinalIgnoreCase) == true)
            {
                roles.Add(UserRole.Partner);
            }

            // Legacy admin system support - if user is marked as admin in the User model
            if (user.IsAdmin)
            {
                roles.Add(UserRole.Admin);
            }

            return roles.Distinct();
        }

        /// <summary>
        /// Gets the highest role level for a user
        /// </summary>
        public async Task<UserRole> GetHighestRoleAsync(int userId)
        {
            var roles = await GetUserRolesAsync(userId);
            return roles.Any() ? roles.Max() : UserRole.Everyone;
        }

        /// <summary>
        /// Gets all permissions assigned to a user
        /// </summary>
        public async Task<IEnumerable<UserPermission>> GetUserPermissionsAsync(int userId)
        {
            // Get the user
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return new List<UserPermission> { UserPermission.None };
            }

            var roles = await GetUserRolesAsync(userId);
            var permissions = new List<UserPermission>();

            // Grant permissions based on roles
            // In a real application, these would come from a database table
            if (roles.Contains(UserRole.Admin))
            {
                // Admins have all permissions
                permissions.Add(UserPermission.Full);
            }
            else if (roles.Contains(UserRole.Partner))
            {
                // Partners have read and write by default (but it can be customized)
                // In a real app, this would be fetched from a database table
                // For now, we'll simulate it based on username
                if (user.Username?.Contains("readonly", StringComparison.OrdinalIgnoreCase) == true)
                {
                    permissions.Add(UserPermission.Read);
                }
                else if (user.Username?.Contains("readwrite", StringComparison.OrdinalIgnoreCase) == true)
                {
                    permissions.Add(UserPermission.ReadWrite);
                }
                else
                {
                    permissions.Add(UserPermission.Read);
                    permissions.Add(UserPermission.Write);
                }
            }
            else if (roles.Contains(UserRole.CleaningStaff))
            {
                // Cleaning staff have read permission only
                permissions.Add(UserPermission.Read);
            }
            else if (roles.Contains(UserRole.Guest))
            {
                // Regular users have no special permissions
                permissions.Add(UserPermission.None);
            }

            return permissions.Distinct();
        }

        /// <summary>
        /// Checks if a user has a specific permission
        /// </summary>
        public async Task<bool> HasPermissionAsync(int userId, UserPermission permission)
        {
            if (permission == UserPermission.None)
            {
                return true; // Everyone has "None" permission
            }

            var userPermissions = await GetUserPermissionsAsync(userId);

            // Check if the user has Full permission (which includes all permissions)
            if (userPermissions.Contains(UserPermission.Full))
            {
                return true;
            }

            // Check if the user has the specific permission 
            // If permission is a combined flag (e.g., ReadWrite), check if the user has all required permissions
            return userPermissions.Any(p => (p & permission) == permission);
        }

        /// <summary>
        /// Checks if a user has at least the specified role
        /// </summary>
        public async Task<bool> HasRoleAsync(int userId, UserRole minimumRole)
        {
            var highestRole = await GetHighestRoleAsync(userId);
            return highestRole >= minimumRole;
        }

        /// <summary>
        /// Assigns a role to a user
        /// </summary>
        public async Task<bool> AssignRoleToUserAsync(int userId, UserRole role, int adminId)
        {
            // Validate user exists
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            // Special handling for Admin role - use the legacy system
            if (role == UserRole.Admin)
            {
                user.IsAdmin = true;
                _context.Entry(user).State = EntityState.Modified;

                // Log the action
                await LogSecurityEventAsync(
                    userId,
                    "RoleAssigned",
                    GetClientIpAddress(),
                    $"Admin role assigned by admin ID {adminId}");

                await _context.SaveChangesAsync();
                return true;
            }

            // For other roles, in a real implementation, you would store this in a database table
            // For now, we'll just log the action and return true
            await LogSecurityEventAsync(
                userId,
                "RoleAssigned",
                GetClientIpAddress(),
                $"Role {role} assigned by admin ID {adminId}");

            return true;
        }

        /// <summary>
        /// Removes a role from a user
        /// </summary>
        public async Task<bool> RemoveRoleFromUserAsync(int userId, UserRole role, int adminId)
        {
            // Validate user exists
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            // Special handling for Admin role - use the legacy system
            if (role == UserRole.Admin)
            {
                user.IsAdmin = false;
                _context.Entry(user).State = EntityState.Modified;

                // Log the action
                await LogSecurityEventAsync(
                    userId,
                    "RoleRemoved",
                    GetClientIpAddress(),
                    $"Admin role removed by admin ID {adminId}");

                await _context.SaveChangesAsync();
                return true;
            }

            // For other roles, in a real implementation, you would remove it from a database table
            // For now, we'll just log the action and return true
            await LogSecurityEventAsync(
                userId,
                "RoleRemoved",
                GetClientIpAddress(),
                $"Role {role} removed by admin ID {adminId}");

            return true;
        }

        /// <summary>
        /// Sets permissions for a user (primarily for Partner role)
        /// </summary>
        public async Task<bool> SetUserPermissionsAsync(int userId, UserPermission permissions, int adminId)
        {
            // Validate user exists
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            // In a real implementation, you would store this in a database table
            // For now, we'll just log the action and return true
            await LogSecurityEventAsync(
                userId,
                "PermissionsChanged",
                GetClientIpAddress(),
                $"Permissions set to {permissions} by admin ID {adminId}");

            return true;
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Logs a security-related event
        /// </summary>
        public async Task<SecurityEvent> LogSecurityEventAsync(int userId, string eventType, string ipAddress, string details = null)
        {
            try
            {
                var securityEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = eventType,
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = details,
                    UserAgent = _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString()
                };

                _context.UserSecurityEvents.Add(securityEvent);
                await _context.SaveChangesAsync();

                return new SecurityEvent
                {
                    UserId = userId,
                    EventType = eventType,
                    Timestamp = securityEvent.EventTime,
                    IpAddress = ipAddress,
                    Details = details
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error logging security event for user {userId}: {eventType}");
                return null;
            }
        }

        /// <summary>
        /// Generates a cryptographically secure random token
        /// </summary>
        public string GenerateSecureToken(int length = 32)
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var tokenBytes = new byte[length];
                rng.GetBytes(tokenBytes);
                return Convert.ToBase64String(tokenBytes);
            }
        }

        /// <summary>
        /// Gets the client IP address from the current request
        /// </summary>
        private string GetClientIpAddress()
        {
            // Try to get the forwarded IP if behind a proxy
            string ip = _httpContextAccessor.HttpContext?.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            // If no forwarded IP, use the remote IP
            if (string.IsNullOrEmpty(ip))
            {
                ip = _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();
            }

            // If still no IP, use a default
            if (string.IsNullOrEmpty(ip))
            {
                ip = "127.0.0.1";
            }

            // If multiple IPs (comma separated), take the first one
            if (ip.Contains(","))
            {
                ip = ip.Split(',')[0].Trim();
            }

            return ip;
        }

        #endregion
    }
}