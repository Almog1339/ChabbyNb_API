using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Auth;
using ChabbyNb_API.Services.Iterfaces;

namespace ChabbyNb_API.Services
{
    /// <summary>
    /// Interface for the user service
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// Gets a user by their ID
        /// </summary>
        Task<User> GetUserByIdAsync(int userId);

        /// <summary>
        /// Gets a user by their email
        /// </summary>
        Task<User> GetUserByEmailAsync(string email);

        /// <summary>
        /// Authenticates a user with email and password
        /// </summary>
        Task<LoginResultDto> AuthenticateAsync(LoginDto loginDto);

        /// <summary>
        /// Authenticates a user with a reservation number
        /// </summary>
        Task<LoginResultDto> AuthenticateWithReservationAsync(string email, string reservationNumber);

        /// <summary>
        /// Refreshes an authentication token
        /// </summary>
        Task<LoginResultDto> RefreshTokenAsync(RefreshTokenDto refreshDto);

        /// <summary>
        /// Logs out a user
        /// </summary>
        Task<bool> LogoutAsync(LogoutDto logoutDto);

        /// <summary>
        /// Registers a new user
        /// </summary>
        Task<User> RegisterAsync(RegisterDto registerDto);

        /// <summary>
        /// Updates a user's profile
        /// </summary>
        Task<User> UpdateProfileAsync(int userId, ProfileDto profileDto);

        /// <summary>
        /// Changes a user's password
        /// </summary>
        Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto changePasswordDto);

        /// <summary>
        /// Initiates a password reset
        /// </summary>
        Task<bool> InitiatePasswordResetAsync(string email);

        /// <summary>
        /// Completes a password reset
        /// </summary>
        Task<bool> ResetPasswordAsync(ResetPasswordDto resetDto);

        /// <summary>
        /// Gets all users with pagination
        /// </summary>
        Task<(IEnumerable<UserDto> Users, int TotalCount)> GetAllUsersAsync(int page = 1, int pageSize = 10);

        /// <summary>
        /// Gets users by role
        /// </summary>
        Task<IEnumerable<UserDto>> GetUsersByRoleAsync(string role);

        /// <summary>
        /// Verifies a user's email
        /// </summary>
        Task<bool> VerifyEmailAsync(string email, string token);

        /// <summary>
        /// Re-sends the verification email
        /// </summary>
        Task<bool> ResendVerificationEmailAsync(string email);
    }

    /// <summary>
    /// Implementation of the user service
    /// </summary>
    public class UserService : IUserService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly ITokenService _tokenService;
        private readonly IRoleService _roleService;
        private readonly IEmailService _emailService;
        private readonly IAccountLockoutService _lockoutService;
        private readonly ILogger<UserService> _logger;

        public UserService(
            ChabbyNbDbContext context,
            ITokenService tokenService,
            IRoleService roleService,
            IEmailService emailService,
            IAccountLockoutService lockoutService,
            ILogger<UserService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _tokenService = tokenService ?? throw new ArgumentNullException(nameof(tokenService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _lockoutService = lockoutService ?? throw new ArgumentNullException(nameof(lockoutService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets a user by their ID
        /// </summary>
        public async Task<User> GetUserByIdAsync(int userId)
        {
            return await _context.Users.FindAsync(userId);
        }

        /// <summary>
        /// Gets a user by their email
        /// </summary>
        public async Task<User> GetUserByEmailAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return null;
            }

            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        /// <summary>
        /// Authenticates a user with email and password
        /// </summary>
        public async Task<LoginResultDto> AuthenticateAsync(LoginDto loginDto)
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
            if (await _lockoutService.IsAccountLockedOutAsync(loginDto.Email))
            {
                throw new UnauthorizedAccessException("This account is temporarily locked. Please try again later or contact support.");
            }

            string ipAddress = "127.0.0.1"; // In a real implementation, get from HttpContext

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
                    await _lockoutService.RecordFailedLoginAttemptAsync(loginDto.Email, ipAddress);
                    throw new UnauthorizedAccessException("Invalid email or password");
                }

                if (!user.IsEmailVerified)
                {
                    throw new UnauthorizedAccessException("Please verify your email before logging in");
                }

                // Record successful login
                await _lockoutService.RecordSuccessfulLoginAsync(user.UserID);

                // Get user roles
                var roles = await _roleService.GetUserRolesAsync(user.UserID);

                // Generate tokens
                var tokenResult = await _tokenService.GenerateTokensAsync(user, roles);

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
                    IsAdmin = user.IsAdmin,
                    Roles = roles.ToList()
                };
            }
            else
            {
                // Handle login with reservation number
                return await AuthenticateWithReservationAsync(loginDto.Email, loginDto.ReservationNumber);
            }
        }

        /// <summary>
        /// Authenticates a user with a reservation number
        /// </summary>
        public async Task<LoginResultDto> AuthenticateWithReservationAsync(string email, string reservationNumber)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(reservationNumber))
            {
                throw new ArgumentException("Email and reservation number are required");
            }

            string ipAddress = "127.0.0.1"; // In a real implementation, get from HttpContext

            try
            {
                // First find the user by email
                var user = await GetUserByEmailAsync(email);
                if (user == null)
                {
                    // Record failed login attempt
                    await _lockoutService.RecordFailedLoginAttemptAsync(email, ipAddress);
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
                    await _lockoutService.RecordFailedLoginAttemptAsync(email, ipAddress);
                    throw new UnauthorizedAccessException("Invalid email or reservation number");
                }

                // Record successful login
                await _lockoutService.RecordSuccessfulLoginAsync(user.UserID);

                // Get user roles
                var roles = await _roleService.GetUserRolesAsync(user.UserID);

                // Generate tokens
                var tokenResult = await _tokenService.GenerateTokensAsync(user, roles);

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
                    IsAdmin = user.IsAdmin,
                    Roles = roles.ToList()
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

        /// <summary>
        /// Refreshes an authentication token
        /// </summary>
        public async Task<LoginResultDto> RefreshTokenAsync(RefreshTokenDto refreshDto)
        {
            if (refreshDto == null || string.IsNullOrEmpty(refreshDto.AccessToken) || string.IsNullOrEmpty(refreshDto.RefreshToken))
            {
                throw new ArgumentException("Access token and refresh token are required");
            }

            try
            {
                // Use the token service to refresh the tokens
                var tokenResult = await _tokenService.RefreshTokenAsync(refreshDto.RefreshToken, refreshDto.AccessToken);

                // Extract user ID from the claims in the new token
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(tokenResult.AccessToken);
                var userIdClaim = jwtToken.Claims.First(claim => claim.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value;

                if (!int.TryParse(userIdClaim, out int userId))
                {
                    throw new InvalidOperationException("Invalid user ID in token");
                }

                // Get the user
                var user = await GetUserByIdAsync(userId);
                if (user == null)
                {
                    throw new InvalidOperationException("User not found");
                }

                // Get roles from the claims in the token
                var roles = jwtToken.Claims
                    .Where(c => c.Type == System.Security.Claims.ClaimTypes.Role)
                    .Select(c => c.Value)
                    .ToList();

                // Create login result
                return new LoginResultDto
                {
                    Success = true,
                    Message = "Token refreshed successfully",
                    Token = tokenResult.AccessToken,
                    RefreshToken = tokenResult.RefreshToken,
                    TokenExpiration = tokenResult.AccessTokenExpiration,
                    UserId = user.UserID,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    IsAdmin = user.IsAdmin,
                    Roles = roles
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error refreshing token: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Logs out a user
        /// </summary>
        public async Task<bool> LogoutAsync(LogoutDto logoutDto)
        {
            if (logoutDto == null)
            {
                return false;
            }

            // If a refresh token was provided, revoke it
            if (!string.IsNullOrEmpty(logoutDto.RefreshToken))
            {
                return await _tokenService.RevokeTokenAsync(logoutDto.RefreshToken);
            }

            // Nothing to do if no refresh token was provided
            return true;
        }

        /// <summary>
        /// Registers a new user
        /// </summary>
        public async Task<User> RegisterAsync(RegisterDto registerDto)
        {
            if (registerDto == null)
            {
                throw new ArgumentNullException(nameof(registerDto));
            }

            // Validate required fields
            if (string.IsNullOrEmpty(registerDto.Email) || string.IsNullOrEmpty(registerDto.Password))
            {
                throw new ArgumentException("Email and password are required");
            }

            // Check if email already exists
            var existingUser = await GetUserByEmailAsync(registerDto.Email);
            if (existingUser != null)
            {
                throw new InvalidOperationException("A user with this email already exists");
            }

            // Check if username already exists
            if (!string.IsNullOrEmpty(registerDto.Username))
            {
                var existingUsername = await _context.Users
                    .AnyAsync(u => u.Username.ToLower() == registerDto.Username.ToLower());

                if (existingUsername)
                {
                    throw new InvalidOperationException("This username is already taken");
                }
            }

            // Create the user
            var newUser = new User
            {
                Email = registerDto.Email,
                Username = registerDto.Username ?? registerDto.Email, // Use email as username if not provided
                PasswordHash = HashPassword(registerDto.Password),
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                PhoneNumber = registerDto.PhoneNumber,
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
                string verificationToken = GenerateToken(32);
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

                // Assign default customer role
                await _roleService.AssignRoleToUserAsync(newUser.UserID, UserRole.Guest.ToString());

                // Commit the transaction
                await transaction.CommitAsync();

                // Send verification email
                await SendVerificationEmailAsync(newUser, verificationToken);

                return newUser;
            }
            catch (Exception ex)
            {
                // Roll back the transaction on error
                await transaction.RollbackAsync();
                _logger.LogError(ex, $"Error registering user: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Updates a user's profile
        /// </summary>
        public async Task<User> UpdateProfileAsync(int userId, ProfileDto profileDto)
        {
            if (profileDto == null)
            {
                throw new ArgumentNullException(nameof(profileDto));
            }

            var user = await GetUserByIdAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException("User not found");
            }

            // Check if username is changed and already exists
            if (!string.IsNullOrEmpty(profileDto.Username) &&
                profileDto.Username != user.Username)
            {
                var existingUsername = await _context.Users
                    .AnyAsync(u => u.Username.ToLower() == profileDto.Username.ToLower() && u.UserID != userId);

                if (existingUsername)
                {
                    throw new InvalidOperationException("This username is already taken");
                }
            }

            // Update user properties
            user.Username = profileDto.Username ?? user.Username;
            user.FirstName = profileDto.FirstName ?? user.FirstName;
            user.LastName = profileDto.LastName ?? user.LastName;
            user.PhoneNumber = profileDto.PhoneNumber ?? user.PhoneNumber;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return user;
        }

        /// <summary>
        /// Changes a user's password
        /// </summary>
        public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto changePasswordDto)
        {
            if (changePasswordDto == null)
            {
                throw new ArgumentNullException(nameof(changePasswordDto));
            }

            var user = await GetUserByIdAsync(userId);
            if (user == null)
            {
                throw new InvalidOperationException("User not found");
            }

            // Verify current password
            string hashedCurrentPassword = HashPassword(changePasswordDto.CurrentPassword);
            if (user.PasswordHash != hashedCurrentPassword)
            {
                throw new UnauthorizedAccessException("Current password is incorrect");
            }

            // Update password
            user.PasswordHash = HashPassword(changePasswordDto.NewPassword);
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            // Revoke all refresh tokens for security
            await _tokenService.RevokeAllUserTokensAsync(userId);

            return true;
        }

        /// <summary>
        /// Initiates a password reset
        /// </summary>
        public async Task<bool> InitiatePasswordResetAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            var user = await GetUserByEmailAsync(email);
            if (user == null)
            {
                // Don't reveal that the user doesn't exist
                _logger.LogWarning($"Password reset attempted for non-existent email: {email}");
                return true;
            }

            // Generate reset token
            string token = GenerateToken(32);

            // Create or update temporary password entry
            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t => t.UserID == user.UserID && !t.IsUsed);

            if (tempPassword == null)
            {
                // Create new entry
                tempPassword = new Tempwd
                {
                    UserID = user.UserID,
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

            // Send password reset email
            await SendPasswordResetEmailAsync(user, token);

            return true;
        }

        /// <summary>
        /// Completes a password reset
        /// </summary>
        public async Task<bool> ResetPasswordAsync(ResetPasswordDto resetDto)
        {
            if (resetDto == null || string.IsNullOrEmpty(resetDto.Email) ||
                string.IsNullOrEmpty(resetDto.Token) || string.IsNullOrEmpty(resetDto.Password))
            {
                throw new ArgumentException("Email, token, and new password are required");
            }

            var user = await GetUserByEmailAsync(resetDto.Email);
            if (user == null)
            {
                throw new InvalidOperationException("Invalid email or token");
            }

            // Verify token
            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t =>
                    t.UserID == user.UserID &&
                    t.Token == resetDto.Token &&
                    !t.IsUsed &&
                    t.ExperationTime > DateTime.UtcNow);

            if (tempPassword == null)
            {
                throw new InvalidOperationException("Invalid or expired password reset token");
            }

            // Update password
            user.PasswordHash = HashPassword(resetDto.Password);
            _context.Users.Update(user);

            // Mark token as used
            tempPassword.IsUsed = true;
            _context.Tempwds.Update(tempPassword);

            await _context.SaveChangesAsync();

            // Revoke all refresh tokens for security
            await _tokenService.RevokeAllUserTokensAsync(user.UserID);

            return true;
        }

        /// <summary>
        /// Gets all users with pagination
        /// </summary>
        public async Task<(IEnumerable<UserDto> Users, int TotalCount)> GetAllUsersAsync(int page = 1, int pageSize = 10)
        {
            // Ensure valid pagination parameters
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 10;
            if (pageSize > 100) pageSize = 100;

            // Calculate skip count
            int skip = (page - 1) * pageSize;

            // Get total count
            int totalCount = await _context.Users.CountAsync();

            // Get paged users
            var users = await _context.Users
                .OrderBy(u => u.UserID)
                .Skip(skip)
                .Take(pageSize)
                .Select(u => new UserDto
                {
                    UserId = u.UserID,
                    Username = u.Username,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    IsAdmin = u.IsAdmin
                })
                .ToListAsync();

            return (users, totalCount);
        }

        /// <summary>
        /// Gets users by role
        /// </summary>
        public async Task<IEnumerable<UserDto>> GetUsersByRoleAsync(string role)
        {
            return await _roleService.GetUsersInRoleAsync(role);
        }

        /// <summary>
        /// Verifies a user's email
        /// </summary>
        public async Task<bool> VerifyEmailAsync(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                return false;
            }

            var user = await GetUserByEmailAsync(email);
            if (user == null)
            {
                return false;
            }

            var verification = await _context.EmailVerifications
                .FirstOrDefaultAsync(v =>
                    v.UserID == user.UserID &&
                    v.VerificationToken == token &&
                    !v.IsVerified &&
                    v.ExpiryDate > DateTime.UtcNow);

            if (verification == null)
            {
                return false;
            }

            // Mark as verified
            verification.IsVerified = true;
            verification.VerifiedDate = DateTime.UtcNow;
            _context.EmailVerifications.Update(verification);

            // Update user's email verification status
            user.IsEmailVerified = true;
            _context.Users.Update(user);

            await _context.SaveChangesAsync();
            return true;
        }

        /// <summary>
        /// Re-sends the verification email
        /// </summary>
        public async Task<bool> ResendVerificationEmailAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            var user = await GetUserByEmailAsync(email);
            if (user == null)
            {
                // Don't reveal that the user doesn't exist
                return true;
            }

            if (user.IsEmailVerified)
            {
                // Already verified
                return true;
            }

            // Generate new token
            string token = GenerateToken(32);

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

            return true;
        }

        #region Helper Methods

        /// <summary>
        /// Hashes a password using SHA256
        /// </summary>
        private string HashPassword(string password)
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
        /// Generates a random token
        /// </summary>
        private string GenerateToken(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var random = new Random();
            var token = new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
            return token;
        }

        /// <summary>
        /// Sends a verification email
        /// </summary>
        private async Task SendVerificationEmailAsync(User user, string token)
        {
            if (user == null || string.IsNullOrEmpty(token))
            {
                return;
            }

            try
            {
                // Build verification URL
                string baseUrl = "https://chabby.com"; // Get from configuration in real app
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
            if (user == null || string.IsNullOrEmpty(token))
            {
                return;
            }

            try
            {
                // Build reset URL
                string baseUrl = "https://chabby.com"; // Get from configuration in real app
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
}