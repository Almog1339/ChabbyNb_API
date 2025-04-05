using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Repositories;
using ChabbyNb_API.Services.Auth;
using ChabbyNb_API.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ChabbyNb_API.Services
{
    /// <summary>
    /// Implementation of the user service
    /// </summary>
    public class UserService : IUserService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly JwtTokenService _jwtTokenService;
        private readonly IAccountLockoutService _lockoutService;
        private readonly IRoleService _roleService;
        private readonly IEmailService _emailService;
        private readonly ILogger<UserService> _logger;

        public UserService(
            IUnitOfWork unitOfWork,
            JwtTokenService jwtTokenService,
            IAccountLockoutService lockoutService,
            IRoleService roleService,
            IEmailService emailService,
            ILogger<UserService> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _jwtTokenService = jwtTokenService ?? throw new ArgumentNullException(nameof(jwtTokenService));
            _lockoutService = lockoutService ?? throw new ArgumentNullException(nameof(lockoutService));
            _roleService = roleService ?? throw new ArgumentNullException(nameof(roleService));
            _emailService = emailService ?? throw new ArgumentNullException(nameof(emailService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<User> GetUserByIdAsync(int userId)
        {
            return await _unitOfWork.Users.GetByIdAsync(userId);
        }

        public async Task<User> GetUserByEmailAsync(string email)
        {
            return await _unitOfWork.Users.GetByEmailAsync(email);
        }

        public async Task<LoginResultDto> AuthenticateAsync(LoginDto loginDto)
        {
            // Custom validation
            if (!loginDto.IsValid())
            {
                throw new ArgumentException("Either Password or Reservation Number is required.");
            }

            // Check if account is locked out
            if (await _lockoutService.IsAccountLockedOutAsync(loginDto.Email))
            {
                throw new UnauthorizedAccessException("This account is temporarily locked due to too many failed login attempts. Please try again later or contact support.");
            }

            User user = null;
            var ipAddress = GetClientIpAddress();

            // Check if user is trying to login with password
            if (!string.IsNullOrEmpty(loginDto.Password))
            {
                // Hash the password for comparison
                string hashedPassword = HashPassword(loginDto.Password);

                // Validate credentials
                user = await _unitOfWork.Users.ValidateCredentialsAsync(loginDto.Email, hashedPassword);

                if (user == null)
                {
                    // Record failed login attempt
                    await _lockoutService.RecordFailedLoginAttemptAsync(loginDto.Email, ipAddress);
                    throw new UnauthorizedAccessException("Invalid login credentials.");
                }

                if (!user.IsEmailVerified)
                {
                    throw new UnauthorizedAccessException("Your email address has not been verified. Please check your email for verification link.");
                }
            }
            // Check if user is trying to login with reservation number
            else if (!string.IsNullOrEmpty(loginDto.ReservationNumber))
            {
                // Find user by reservation
                user = await _unitOfWork.Users.FindByReservationAsync(loginDto.Email, loginDto.ReservationNumber);

                if (user == null)
                {
                    // Record failed login attempt
                    await _lockoutService.RecordFailedLoginAttemptAsync(loginDto.Email, ipAddress);
                    throw new UnauthorizedAccessException("Invalid reservation number or email address.");
                }
            }

            if (user == null)
            {
                // This should not happen based on validations above
                await _lockoutService.RecordFailedLoginAttemptAsync(loginDto.Email, ipAddress);
                throw new UnauthorizedAccessException("Invalid login attempt.");
            }

            // Record successful login
            await _lockoutService.RecordSuccessfulLoginAsync(user.UserID);

            // Generate tokens (JWT + refresh token)
            var tokenResult = await _jwtTokenService.GenerateTokensAsync(user);

            // Get user roles
            var roles = await _roleService.GetUserRolesAsync(user.UserID);

            return new LoginResultDto
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
                Roles = roles
            };
        }

        public async Task<LoginResultDto> RefreshTokenAsync(RefreshTokenDto refreshDto)
        {
            // Attempt to refresh the token
            var tokenResult = await _jwtTokenService.RefreshTokenAsync(
                refreshDto.RefreshToken,
                refreshDto.AccessToken);

            if (tokenResult == null)
            {
                throw new UnauthorizedAccessException("Invalid token");
            }

            // Extract user ID from the new token using JwtSecurityTokenHandler
            var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
            var jwtToken = handler.ReadJwtToken(tokenResult.AccessToken);
            var userIdClaim = jwtToken.Claims.First(claim => claim.Type == System.Security.Claims.ClaimTypes.NameIdentifier).Value;

            if (!int.TryParse(userIdClaim, out int userId))
            {
                throw new UnauthorizedAccessException("Invalid token format");
            }

            // Get user information
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                throw new UnauthorizedAccessException("User not found");
            }

            // Get user roles
            var roles = await _roleService.GetUserRolesAsync(userId);

            return new LoginResultDto
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
                Roles = roles
            };
        }

        public async Task<bool> LogoutAsync(LogoutDto logoutDto)
        {
            // With JWT, we don't need to do anything server-side for basic logout
            // But we can revoke the refresh token for better security
            if (!string.IsNullOrEmpty(logoutDto.RefreshToken))
            {
                await _jwtTokenService.RevokeTokenAsync(logoutDto.RefreshToken);
            }

            return true;
        }

        public async Task<User> RegisterAsync(RegisterDto registerDto)
        {
            // Validate email and username uniqueness
            bool emailExists = await _unitOfWork.Users.EmailExistsAsync(registerDto.Email);
            if (emailExists)
            {
                throw new InvalidOperationException("Email address is already registered.");
            }

            bool usernameExists = await _unitOfWork.Users.UsernameExistsAsync(registerDto.Username);
            if (usernameExists)
            {
                throw new InvalidOperationException("Username is already taken.");
            }

            // Create new user
            var user = new User
            {
                Username = registerDto.Username,
                Email = registerDto.Email,
                PasswordHash = HashPassword(registerDto.Password),
                FirstName = registerDto.FirstName,
                LastName = registerDto.LastName,
                PhoneNumber = registerDto.PhoneNumber,
                IsAdmin = false,
                CreatedDate = DateTime.Now,
                IsEmailVerified = false // Require email verification
            };

            // Use a transaction
            await _unitOfWork.BeginTransactionAsync();

            try
            {
                // Add user
                await _unitOfWork.Users.AddAsync(user);

                // Create email verification token
                var verification = new EmailVerification
                {
                    UserID = user.UserID,
                    Email = user.Email,
                    VerificationToken = GenerateToken(32),
                    ExpiryDate = DateTime.Now.AddDays(3),
                    IsVerified = false,
                    CreatedDate = DateTime.Now
                };

                // Here you would typically add the verification record,
                // but we don't have a verification repository defined yet
                // await _unitOfWork.EmailVerifications.AddAsync(verification);

                // Assign default role (Guest)
                await _unitOfWork.Roles.AssignRoleToUserAsync(user.UserID, UserRole.Guest);

                await _unitOfWork.CommitTransactionAsync();

                // Send verification email
                await SendVerificationEmailAsync(user, verification.VerificationToken);

                return user;
            }
            catch
            {
                await _unitOfWork.RollbackTransactionAsync();
                throw;
            }
        }

        public async Task<User> UpdateProfileAsync(int userId, ProfileDto profileDto)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                throw new ArgumentException("User not found");
            }

            // Check if username is changed and already exists
            if (user.Username != profileDto.Username)
            {
                bool usernameExists = await _unitOfWork.Users.UsernameExistsAsync(profileDto.Username);
                if (usernameExists)
                {
                    throw new InvalidOperationException("Username is already taken.");
                }
            }

            // Update properties
            user.Username = profileDto.Username;
            user.FirstName = profileDto.FirstName;
            user.LastName = profileDto.LastName;
            user.PhoneNumber = profileDto.PhoneNumber;

            // Save changes
            await _unitOfWork.Users.UpdateAsync(user);
            await _unitOfWork.SaveChangesAsync();

            return user;
        }

        public async Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto changePasswordDto)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                throw new ArgumentException("User not found");
            }

            // Verify current password
            string currentPasswordHash = HashPassword(changePasswordDto.CurrentPassword);
            if (user.PasswordHash != currentPasswordHash)
            {
                throw new UnauthorizedAccessException("Current password is incorrect");
            }

            // Update password
            user.PasswordHash = HashPassword(changePasswordDto.NewPassword);
            await _unitOfWork.Users.UpdateAsync(user);

            // Revoke all tokens to force re-login
            await _jwtTokenService.RevokeAllUserTokensAsync(userId);

            return true;
        }

        public async Task<bool> InitiatePasswordResetAsync(string email)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(email);
            if (user == null)
            {
                // Don't reveal that the user doesn't exist
                return false;
            }

            // Generate reset token
            string token = GenerateToken(32);
            DateTime expiryTime = DateTime.Now.AddHours(1);

            // Create or update reset entry
            var tempPwd = new Tempwd
            {
                UserID = user.UserID,
                Token = token,
                ExperationTime = expiryTime,
                IsUsed = false
            };

            // Here you would typically add or update the tempwd record,
            // but we don't have a tempwd repository defined yet
            // await _unitOfWork.TempPasswords.AddOrUpdateAsync(tempPwd);

            // Send reset email
            await SendPasswordResetEmailAsync(user, token);

            return true;
        }

        public async Task<bool> ResetPasswordAsync(ResetPasswordDto resetDto)
        {
            var user = await _unitOfWork.Users.GetByEmailAsync(resetDto.Email);
            if (user == null)
            {
                throw new UnauthorizedAccessException("Invalid reset information");
            }

            // Verify token
            // Here you would typically validate the token from the tempwd repository,
            // but we don't have a tempwd repository defined yet
            // var tempPwd = await _unitOfWork.TempPasswords.ValidateTokenAsync(user.UserID, resetDto.Token);
            // if (tempPwd == null || tempPwd.IsUsed || tempPwd.ExperationTime < DateTime.Now)
            // {
            //     throw new UnauthorizedAccessException("Invalid or expired reset token");
            // }

            // Update password
            user.PasswordHash = HashPassword(resetDto.Password);
            await _unitOfWork.Users.UpdateAsync(user);

            // Mark token as used
            // tempPwd.IsUsed = true;
            // await _unitOfWork.TempPasswords.UpdateAsync(tempPwd);

            // Revoke all tokens
            await _jwtTokenService.RevokeAllUserTokensAsync(user.UserID);

            return true;
        }

        public async Task<(IEnumerable<UserDto> Users, int TotalCount)> GetAllUsersAsync(int page = 1, int pageSize = 10)
        {
            // Calculate skip count
            int skip = (page - 1) * pageSize;

            // Get total count (excluding admins if not an admin)
            int totalCount = await _unitOfWork.Users.CountAsync(u => !u.IsAdmin);

            // Get paged results
            var users = await _unitOfWork.Users.GetAsync(u => !u.IsAdmin);
            var pagedUsers = users.Skip(skip).Take(pageSize);

            // Map to DTOs
            var userDtos = pagedUsers.Select(u => new UserDto
            {
                UserId = u.UserID,
                Username = u.Username,
                Email = u.Email,
                FirstName = u.FirstName,
                LastName = u.LastName,
                IsAdmin = u.IsAdmin
            });

            return (userDtos, totalCount);
        }

        public async Task<bool> LockUserAccountAsync(int userId, LockAccountDto lockAccountDto)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                throw new ArgumentException("User not found");
            }

            // Don't allow locking admin accounts unless you're a super admin
            if (user.IsAdmin)
            {
                throw new UnauthorizedAccessException("Cannot lock admin accounts");
            }

            // Lock the account
            bool success = await _lockoutService.LockoutAccountAsync(
                userId,
                lockAccountDto.Reason,
                GetClientIpAddress(),
                lockAccountDto.LockoutMinutes);

            if (success)
            {
                // Revoke all tokens for this user
                await _jwtTokenService.RevokeAllUserTokensAsync(userId);
            }

            return success;
        }

        public async Task<bool> UnlockUserAccountAsync(int userId, UnlockAccountDto unlockAccountDto, int adminId)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                throw new ArgumentException("User not found");
            }

            // Unlock the account
            return await _lockoutService.UnlockAccountAsync(userId, adminId, unlockAccountDto.Notes);
        }

        public async Task<object> GetUserLockoutStatusAsync(int userId)
        {
            var user = await _unitOfWork.Users.GetByIdAsync(userId);
            if (user == null)
            {
                throw new ArgumentException("User not found");
            }

            bool isLocked = await _lockoutService.IsAccountLockedOutAsync(userId);

            // Get active lockout details if locked
            // Here you would typically get the lockout details from the UserAccountLockout repository,
            // but we don't have that repository defined yet

            return new
            {
                isLocked
                // We would include other lockout details if the user is locked
            };
        }

        #region Helper Methods

        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var hash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
                return hash;
            }
        }

        private string GenerateToken(int length)
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        private string GetClientIpAddress()
        {
            // In a real implementation, this would get the IP from the HttpContext
            // Since this is a service, we don't have direct access to HttpContext
            // You would need to pass this from the controller
            return "127.0.0.1";
        }

        private async Task SendVerificationEmailAsync(User user, string token)
        {
            // Here you would send an email with the verification token
            // using the _emailService
        }

        private async Task SendPasswordResetEmailAsync(User user, string token)
        {
            // Here you would send an email with the reset token
            // using the _emailService
        }

        #endregion
    }
}