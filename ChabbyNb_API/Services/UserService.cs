using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace ChabbyNb_API.Services
{
    public interface IUserService
    {
        Task<User> GetUserByIdAsync(int userId);
        Task<User> GetUserByEmailAsync(string email);
        Task<bool> EmailExistsAsync(string email);
        Task<bool> UsernameExistsAsync(string username);
        Task<User> RegisterUserAsync(RegisterDto model);
        Task<bool> UpdateUserProfileAsync(int userId, ProfileDto model);
        Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto model);
        Task<bool> ResetPasswordAsync(string email, string token, string newPassword);
        Task<string> GeneratePasswordResetTokenAsync(User user);
        Task<bool> ValidatePasswordResetTokenAsync(int userId, string token);
        Task<bool> VerifyEmailAsync(string email, string token);
        Task<string> GenerateEmailVerificationTokenAsync(User user);
        Task<bool> ResendVerificationEmailAsync(string email);
    }

    public class UserService : IUserService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IEmailService _emailService;
        private readonly ILogger<UserService> _logger;
        private readonly IConfiguration _configuration;

        public UserService(
            ChabbyNbDbContext context,
            IEmailService emailService,
            ILogger<UserService> logger,
            IConfiguration configuration)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        public async Task<User> GetUserByIdAsync(int userId)
        {
            return await _context.Users.FindAsync(userId);
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _context.Users
                .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _context.Users
                .AnyAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<bool> UsernameExistsAsync(string username)
        {
            return await _context.Users
                .AnyAsync(u => u.Username.ToLower() == username.ToLower());
        }

        public async Task<User> RegisterUserAsync(RegisterDto model)
        {
            if (string.IsNullOrEmpty(model.Email) || string.IsNullOrEmpty(model.Password))
            {
                throw new ArgumentException("Email and password are required");
            }

            // Check if email already exists
            if (await EmailExistsAsync(model.Email))
            {
                throw new InvalidOperationException("A user with this email already exists");
            }

            // Check if username already exists
            if (!string.IsNullOrEmpty(model.Username) && await UsernameExistsAsync(model.Username))
            {
                throw new InvalidOperationException("This username is already taken");
            }

            // Create the user
            var newUser = new User
            {
                Email = model.Email,
                Username = model.Username ?? model.Email, // Use email as username if not provided
                PasswordHash = HashPassword(model.Password),
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
                string verificationToken = GenerateSecureToken(32);
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

                return newUser;
            }
            catch
            {
                // Roll back the transaction on error
                await transaction.RollbackAsync();
                throw;
            }
        }

        public async Task<bool> UpdateUserProfileAsync(int userId, ProfileDto model)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            // Check if username is changed and already exists
            if (!string.IsNullOrEmpty(model.Username) &&
                model.Username != user.Username &&
                await UsernameExistsAsync(model.Username))
            {
                throw new InvalidOperationException("This username is already taken");
            }

            // Update user properties
            user.Username = model.Username ?? user.Username;
            user.FirstName = model.FirstName ?? user.FirstName;
            user.LastName = model.LastName ?? user.LastName;
            user.PhoneNumber = model.PhoneNumber ?? user.PhoneNumber;

            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto model)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                return false;
            }

            // Verify current password
            if (HashPassword(model.CurrentPassword) != user.PasswordHash)
            {
                return false;
            }

            // Update password
            user.PasswordHash = HashPassword(model.NewPassword);
            _context.Users.Update(user);
            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<bool> ResetPasswordAsync(string email, string token, string newPassword)
        {
            // Find user by email
            var user = await GetUserByEmailAsync(email);
            if (user == null)
            {
                return false;
            }

            // Validate token
            if (!await ValidatePasswordResetTokenAsync(user.UserID, token))
            {
                return false;
            }

            // Update the password
            user.PasswordHash = HashPassword(newPassword);
            _context.Users.Update(user);

            // Mark the token as used
            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t =>
                    t.UserID == user.UserID &&
                    t.Token == token &&
                    !t.IsUsed);

            if (tempPassword != null)
            {
                tempPassword.IsUsed = true;
                _context.Tempwds.Update(tempPassword);
            }

            await _context.SaveChangesAsync();

            return true;
        }

        public async Task<string> GeneratePasswordResetTokenAsync(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            // Generate a secure random token
            string token = GenerateSecureToken(32);

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

            return token;
        }

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

        public async Task<bool> VerifyEmailAsync(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
            {
                return false;
            }

            // Find user by email
            var user = await GetUserByEmailAsync(email);
            if (user == null)
            {
                return false;
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

        public async Task<string> GenerateEmailVerificationTokenAsync(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            // Generate new token
            string token = GenerateSecureToken(32);

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

            return token;
        }

        public async Task<bool> ResendVerificationEmailAsync(string email)
        {
            // Find user by email
            var user = await GetUserByEmailAsync(email);

            // Don't reveal whether the user exists
            if (user == null)
            {
                return false;
            }

            // Check if already verified
            if (user.IsEmailVerified)
            {
                return true; // Already verified
            }

            // Generate new token
            string token = await GenerateEmailVerificationTokenAsync(user);

            // Send verification email
            await SendVerificationEmailAsync(user, token);

            return true;
        }

        #region Helper Methods

        private string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                throw new ArgumentException("Password cannot be null or empty");
            }

            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var hash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
                return hash;
            }
        }

        private string GenerateSecureToken(int length = 32)
        {
            using (var rng = RandomNumberGenerator.Create())
            {
                var tokenBytes = new byte[length];
                rng.GetBytes(tokenBytes);
                return Convert.ToBase64String(tokenBytes);
            }
        }

        private async Task SendVerificationEmailAsync(User user, string token)
        {
            try
            {
                // Build verification URL using configuration
                string baseUrl = string.IsNullOrEmpty(_configuration["ApplicationUrl"])
                    ? "https://www.chabbnb.com"
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

        private async Task SendPasswordResetEmailAsync(User user, string token)
        {
            try
            {
                // Build reset URL using configuration
                string baseUrl = string.IsNullOrEmpty(_configuration["ApplicationUrl"])
                    ? "https://www.chabbnb.com"
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
}