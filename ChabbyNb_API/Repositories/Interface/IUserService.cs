using System.Collections.Generic;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Auth;

namespace ChabbyNb_API.Repositories.Interface
{
    /// <summary>
    /// Interface for user-related business logic
    /// </summary>
    public interface IUserService
    {
        // Authentication
        Task<(User User, string ErrorMessage)> AuthenticateAsync(string email, string password);
        Task<(User User, string ErrorMessage)> AuthenticateWithReservationAsync(string email, string reservationNumber);

        // User operations
        Task<(bool Success, string ErrorMessage)> RegisterUserAsync(RegisterDto model);
        Task<(bool Success, string ErrorMessage)> VerifyEmailAsync(string token);
        Task<(bool Success, string ErrorMessage)> ResendVerificationEmailAsync(string email);
        Task<(bool Success, string ErrorMessage)> RequestPasswordResetAsync(string email);
        Task<(bool Success, string ErrorMessage)> ResetPasswordAsync(ResetPasswordDto model);
        Task<(bool Success, string ErrorMessage)> ChangePasswordAsync(int userId, string currentPassword, string newPassword);
        Task<(User User, string ErrorMessage)> GetUserProfileAsync(int userId);
        Task<(bool Success, string ErrorMessage)> UpdateUserProfileAsync(int userId, ProfileDto model);

        // Role management
        Task<List<UserRole>> GetUserRolesAsync(int userId);
        Task<(bool Success, string ErrorMessage)> AssignRoleToUserAsync(int userId, UserRole role, bool isSuperAdmin);
        Task<(bool Success, string ErrorMessage)> RemoveRoleFromUserAsync(int userId, UserRole role, bool isSuperAdmin);
        Task<List<User>> GetUsersInRoleAsync(UserRole role);

        // Account security
        Task<(bool IsLocked, object LockoutDetails, string ErrorMessage)> GetUserLockoutStatusAsync(int userId);
        Task<(bool Success, string ErrorMessage)> LockUserAccountAsync(int userId, string reason, string ipAddress, int? lockoutMinutes, bool isSuperAdmin);
        Task<(bool Success, string ErrorMessage)> UnlockUserAccountAsync(int userId, int adminId, string notes);
    }
}