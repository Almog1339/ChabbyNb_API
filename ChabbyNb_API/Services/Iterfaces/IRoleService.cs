using System.Collections.Generic;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services.Auth;

namespace ChabbyNb_API.Services.Interfaces
{
    /// <summary>
    /// Service for role-related operations
    /// </summary>
    public interface IRoleService
    {
        /// <summary>
        /// Gets all available roles
        /// </summary>
        /// <returns>List of all role names</returns>
        IEnumerable<string> GetAllRoles();

        /// <summary>
        /// Gets all roles for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>List of role names</returns>
        Task<IEnumerable<string>> GetUserRolesAsync(int userId);

        /// <summary>
        /// Gets the highest role for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Highest role enum value</returns>
        Task<UserRole> GetUserHighestRoleAsync(int userId);

        /// <summary>
        /// Assigns a role to a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">Role to assign</param>
        /// <returns>Success flag</returns>
        Task<bool> AssignRoleToUserAsync(int userId, string role);

        /// <summary>
        /// Removes a role from a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">Role to remove</param>
        /// <returns>Success flag</returns>
        Task<bool> RemoveRoleFromUserAsync(int userId, string role);

        /// <summary>
        /// Gets all users with a specific role
        /// </summary>
        /// <param name="role">Role to search for</param>
        /// <returns>List of users with the role</returns>
        Task<IEnumerable<UserDto>> GetUsersInRoleAsync(string role);

        /// <summary>
        /// Checks if a user has a specific role
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">Role to check</param>
        /// <returns>True if the user has the role</returns>
        Task<bool> UserHasRoleAsync(int userId, string role);

        /// <summary>
        /// Generates claims for a user based on their roles
        /// </summary>
        /// <param name="user">User entity</param>
        /// <returns>List of claims</returns>
        Task<IEnumerable<System.Security.Claims.Claim>> GenerateUserRoleClaimsAsync(User user);
    }
}