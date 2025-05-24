using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Core;

namespace ChabbyNb_API.Services
{
    public interface IUserService : IEntityService<User, UserDto, RegisterDto, ProfileDto>
    {
        // Authentication methods
        Task<User> GetUserByEmailAsync(string email);
        Task<User> GetUserByUsernameAsync(string username);
        Task<bool> EmailExistsAsync(string email);
        Task<bool> UsernameExistsAsync(string username);
        Task<bool> ValidatePasswordAsync(User user, string password);

        // Password management
        Task<ServiceResult> ChangePasswordAsync(int userId, ChangePasswordDto model);
        Task<ServiceResult> ResetPasswordAsync(string email, string token, string newPassword);
        Task<ServiceResult<string>> GeneratePasswordResetTokenAsync(User user);
        Task<bool> ValidatePasswordResetTokenAsync(int userId, string token);

        // Email verification
        Task<ServiceResult> VerifyEmailAsync(string email, string token);
        Task<ServiceResult<string>> GenerateEmailVerificationTokenAsync(User user);
        Task<ServiceResult> ResendVerificationEmailAsync(string email);

        // User relationships
        Task<User> GetUserWithBookingsAsync(int userId);
        Task<User> GetUserWithReviewsAsync(int userId);
        Task<User> FindByReservationAsync(string email, string reservationNumber);
    }

    public class UserService : BaseEntityService<User, UserDto, RegisterDto, ProfileDto>, IUserService
    {
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;

        public UserService(
            ChabbyNbDbContext context,
            IMapper mapper,
            ILogger<UserService> logger,
            IEmailService emailService,
            IConfiguration configuration)
            : base(context, mapper, logger)
        {
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        }

        #region Base Service Implementation

        protected override async Task<User> GetEntityByIdAsync(int id)
        {
            return await _dbSet.FindAsync(id);
        }

        protected override IQueryable<User> GetBaseQuery()
        {
            return _dbSet.AsQueryable();
        }

        protected override async Task<UserDto> MapToDto(User entity)
        {
            return new UserDto
            {
                UserId = entity.UserID,
                Username = entity.Username,
                Email = entity.Email,
                FirstName = entity.FirstName,
                LastName = entity.LastName,
                IsAdmin = entity.IsAdmin
            };
        }

        protected override async Task<IEnumerable<UserDto>> MapToDtos(IEnumerable<User> entities)
        {
            return entities.Select(u => new UserDto
            {
                UserId = u.UserID,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                IsAdmin = u.IsAdmin
            });
        }

        protected override async Task<User> MapFromCreateDto(RegisterDto createDto)
        {
            return new User
            {
                Email = createDto.Email,
                Username = createDto.Username ?? createDto.Email,
                PasswordHash = HashPassword(createDto.Password),
                FirstName = createDto.FirstName,
                LastName = createDto.LastName,
                PhoneNumber = createDto.PhoneNumber,
                IsAdmin = false,
                CreatedDate = DateTime.UtcNow,
                IsEmailVerified = false
            };
        }

        protected override async Task MapFromUpdateDto(ProfileDto updateDto, User entity)
        {
            entity.Username = updateDto.Username ?? entity.Username;
            entity.FirstName = updateDto.FirstName ?? entity.FirstName;
            entity.LastName = updateDto.LastName ?? entity.LastName;
            entity.PhoneNumber = updateDto.PhoneNumber ?? entity.PhoneNumber;
        }

        protected override async Task ValidateCreateDto(RegisterDto createDto)
        {
            if (await EmailExistsAsync(createDto.Email))
                throw new InvalidOperationException("A user with this email already exists");

            if (!string.IsNullOrEmpty(createDto.Username) && await UsernameExistsAsync(createDto.Username))
                throw new InvalidOperationException("This username is already taken");
        }

        protected override async Task ValidateUpdateDto(ProfileDto updateDto, User entity)
        {
            if (!string.IsNullOrEmpty(updateDto.Username) &&
                updateDto.Username != entity.Username &&
                await UsernameExistsAsync(updateDto.Username))
            {
                throw new InvalidOperationException("This username is already taken");
            }
        }

        protected override async Task AfterCreate(User entity, RegisterDto createDto)
        {
            // Create email verification token
            string verificationToken = GenerateSecureToken(32);
            var verification = new EmailVerification
            {
                UserID = entity.UserID,
                Email = entity.Email,
                VerificationToken = verificationToken,
                ExpiryDate = DateTime.UtcNow.AddDays(3),
                IsVerified = false,
                CreatedDate = DateTime.UtcNow
            };

            _context.EmailVerifications.Add(verification);
            await _context.SaveChangesAsync();

            // Send verification email
            await SendVerificationEmailAsync(entity, verificationToken);
        }

        #endregion

        #region IUserService Implementation

        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _dbSet.FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<User> GetUserByUsernameAsync(string username)
        {
            return await _dbSet.FirstOrDefaultAsync(u => u.Username.ToLower() == username.ToLower());
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            return await _dbSet.AnyAsync(u => u.Email.ToLower() == email.ToLower());
        }

        public async Task<bool> UsernameExistsAsync(string username)
        {
            return await _dbSet.AnyAsync(u => u.Username.ToLower() == username.ToLower());
        }

        public async Task<bool> ValidatePasswordAsync(User user, string password)
        {
            return user != null && HashPassword(password) == user.PasswordHash;
        }

        public async Task<ServiceResult> ChangePasswordAsync(int userId, ChangePasswordDto model)
        {
            var user = await GetEntityByIdAsync(userId);
            if (user == null)
                return ServiceResult.ErrorResult("User not found");

            if (HashPassword(model.CurrentPassword) != user.PasswordHash)
                return ServiceResult.ErrorResult("Current password is incorrect");

            user.PasswordHash = HashPassword(model.NewPassword);
            await _context.SaveChangesAsync();

            return ServiceResult.SuccessResult("Password changed successfully");
        }

        public async Task<ServiceResult> ResetPasswordAsync(string email, string token, string newPassword)
        {
            var user = await GetUserByEmailAsync(email);
            if (user == null)
                return ServiceResult.ErrorResult("User not found");

            if (!await ValidatePasswordResetTokenAsync(user.UserID, token))
                return ServiceResult.ErrorResult("Invalid or expired reset token");

            user.PasswordHash = HashPassword(newPassword);

            // Mark token as used
            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t => t.UserID == user.UserID && t.Token == token && !t.IsUsed);

            if (tempPassword != null)
            {
                tempPassword.IsUsed = true;
            }

            await _context.SaveChangesAsync();
            return ServiceResult.SuccessResult("Password reset successfully");
        }

        public async Task<ServiceResult<string>> GeneratePasswordResetTokenAsync(User user)
        {
            if (user == null)
                return ServiceResult<string>.ErrorResult("User not found");

            string token = GenerateSecureToken(32);

            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t => t.UserID == user.UserID && !t.IsUsed);

            if (tempPassword == null)
            {
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
                tempPassword.Token = token;
                tempPassword.ExperationTime = DateTime.UtcNow.AddHours(24);
                tempPassword.IsUsed = false;
            }

            await _context.SaveChangesAsync();
            await SendPasswordResetEmailAsync(user, token);

            return ServiceResult<string>.SuccessResult(token, "Password reset token generated");
        }

        public async Task<bool> ValidatePasswordResetTokenAsync(int userId, string token)
        {
            if (string.IsNullOrEmpty(token))
                return false;

            var tempPassword = await _context.Tempwds
                .FirstOrDefaultAsync(t =>
                    t.UserID == userId &&
                    t.Token == token &&
                    !t.IsUsed &&
                    t.ExperationTime > DateTime.UtcNow);

            return tempPassword != null;
        }

        public async Task<ServiceResult> VerifyEmailAsync(string email, string token)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(token))
                return ServiceResult.ErrorResult("Invalid verification data");

            var user = await GetUserByEmailAsync(email);
            if (user == null)
                return ServiceResult.ErrorResult("User not found");

            var verification = await _context.EmailVerifications
                .FirstOrDefaultAsync(v =>
                    v.UserID == user.UserID &&
                    v.VerificationToken == token &&
                    !v.IsVerified &&
                    v.ExpiryDate > DateTime.UtcNow);

            if (verification == null)
                return ServiceResult.ErrorResult("Invalid or expired verification token");

            verification.IsVerified = true;
            verification.VerifiedDate = DateTime.UtcNow;
            user.IsEmailVerified = true;

            await _context.SaveChangesAsync();
            return ServiceResult.SuccessResult("Email verified successfully");
        }

        public async Task<ServiceResult<string>> GenerateEmailVerificationTokenAsync(User user)
        {
            if (user == null)
                return ServiceResult<string>.ErrorResult("User not found");

            string token = GenerateSecureToken(32);

            var verification = await _context.EmailVerifications
                .FirstOrDefaultAsync(v => v.UserID == user.UserID && !v.IsVerified);

            if (verification == null)
            {
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
                verification.VerificationToken = token;
                verification.ExpiryDate = DateTime.UtcNow.AddDays(3);
            }

            await _context.SaveChangesAsync();
            return ServiceResult<string>.SuccessResult(token, "Verification token generated");
        }

        public async Task<ServiceResult> ResendVerificationEmailAsync(string email)
        {
            var user = await GetUserByEmailAsync(email);
            if (user == null)
                return ServiceResult.ErrorResult("User not found");

            if (user.IsEmailVerified)
                return ServiceResult.SuccessResult("Email is already verified");

            var result = await GenerateEmailVerificationTokenAsync(user);
            if (!result.Success)
                return ServiceResult.ErrorResult(result.Errors);

            await SendVerificationEmailAsync(user, result.Data);
            return ServiceResult.SuccessResult("Verification email sent");
        }

        public async Task<User> GetUserWithBookingsAsync(int userId)
        {
            return await _dbSet
                .Include(u => u.Bookings)
                .FirstOrDefaultAsync(u => u.UserID == userId);
        }

        public async Task<User> GetUserWithReviewsAsync(int userId)
        {
            return await _dbSet
                .Include(u => u.Reviews)
                .FirstOrDefaultAsync(u => u.UserID == userId);
        }

        public async Task<User> FindByReservationAsync(string email, string reservationNumber)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(reservationNumber))
                return null;

            return await _context.Bookings
                .Include(b => b.User)
                .Where(b =>
                    b.ReservationNumber == reservationNumber &&
                    b.User.Email == email)
                .Select(b => b.User)
                .FirstOrDefaultAsync();
        }

        #endregion

        #region Private Helper Methods

        private string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password))
                throw new ArgumentException("Password cannot be null or empty");

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
                string baseUrl = _configuration["ApplicationUrl"] ?? "https://www.chabbnb.com";
                string verifyUrl = $"{baseUrl}/verify-email?email={Uri.EscapeDataString(user.Email)}&token={token}";

                var model = new
                {
                    UserName = !string.IsNullOrEmpty(user.FirstName) ? user.FirstName : user.Username,
                    VerificationLink = verifyUrl,
                    ExpiryHours = 72
                };

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
            }
        }

        private async Task SendPasswordResetEmailAsync(User user, string token)
        {
            try
            {
                string baseUrl = _configuration["ApplicationUrl"] ?? "https://www.chabbnb.com";
                string resetUrl = $"{baseUrl}/reset-password?email={Uri.EscapeDataString(user.Email)}&token={token}";

                var model = new
                {
                    UserName = !string.IsNullOrEmpty(user.FirstName) ? user.FirstName : user.Username,
                    ResetLink = resetUrl,
                    ExpiryHours = 24
                };

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
            }
        }

        #endregion
    }
}