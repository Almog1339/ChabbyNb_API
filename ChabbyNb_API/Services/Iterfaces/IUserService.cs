using System.Collections.Generic;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Auth;

namespace ChabbyNb_API.Services.Interfaces
{
    /// <summary>
    /// Service for user-related operations
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// Gets a user by their ID
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>User if found, null otherwise</returns>
        Task<User> GetUserByIdAsync(int userId);

        /// <summary>
        /// Gets a user by their email
        /// </summary>
        /// <param name="email">Email address</param>
        /// <returns>User if found, null otherwise</returns>
        Task<User> GetUserByEmailAsync(string email);

        /// <summary>
        /// Authenticates a user with email/password
        /// </summary>
        /// <param name="loginDto">Login credentials</param>
        /// <returns>Authentication result</returns>
        Task<LoginResultDto> AuthenticateAsync(LoginDto loginDto);

        /// <summary>
        /// Authenticates a user with refresh token
        /// </summary>
        /// <param name="refreshDto">Refresh token data</param>
        /// <returns>New authentication result</returns>
        Task<LoginResultDto> RefreshTokenAsync(RefreshTokenDto refreshDto);

        /// <summary>
        /// Logs out a user
        /// </summary>
        /// <param name="logoutDto">Logout data</param>
        /// <returns>Success flag</returns>
        Task<bool> LogoutAsync(LogoutDto logoutDto);

        /// <summary>
        /// Registers a new user
        /// </summary>
        /// <param name="registerDto">Registration data</param>
        /// <returns>Registered user</returns>
        Task<User> RegisterAsync(RegisterDto registerDto);

        /// <summary>
        /// Updates a user's profile
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="profileDto">Profile data</param>
        /// <returns>Updated user</returns>
        Task<User> UpdateProfileAsync(int userId, ProfileDto profileDto);

        /// <summary>
        /// Changes a user's password
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="changePasswordDto">Password change data</param>
        /// <returns>Success flag</returns>
        Task<bool> ChangePasswordAsync(int userId, ChangePasswordDto changePasswordDto);

        /// <summary>
        /// Initiates password reset
        /// </summary>
        /// <param name="email">User email</param>
        /// <returns>Success flag</returns>
        Task<bool> InitiatePasswordResetAsync(string email);

        /// <summary>
        /// Completes password reset
        /// </summary>
        /// <param name="resetDto">Password reset data</param>
        /// <returns>Success flag</returns>
        Task<bool> ResetPasswordAsync(ResetPasswordDto resetDto);

        /// <summary>
        /// Gets all users with pagination
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <returns>Paged list of users</returns>
        Task<(IEnumerable<UserDto> Users, int TotalCount)> GetAllUsersAsync(int page = 1, int pageSize = 10);

        /// <summary>
        /// Locks a user account
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="lockAccountDto">Lock account data</param>
        /// <returns>Success flag</returns>
        Task<bool> LockUserAccountAsync(int userId, LockAccountDto lockAccountDto);

        /// <summary>
        /// Unlocks a user account
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="unlockAccountDto">Unlock account data</param>
        /// <param name="adminId">Admin ID performing the action</param>
        /// <returns>Success flag</returns>
        Task<bool> UnlockUserAccountAsync(int userId, UnlockAccountDto unlockAccountDto, int adminId);

        /// <summary>
        /// Gets user account lockout status
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Lockout information</returns>
        Task<object> GetUserLockoutStatusAsync(int userId);
    }
}