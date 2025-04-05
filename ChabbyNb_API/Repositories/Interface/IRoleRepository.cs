using System.Collections.Generic;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Auth;

namespace ChabbyNb_API.Repositories.Interface
{
    /// <summary>
    /// Repository interface for role-specific operations
    /// </summary>
    public interface IRoleRepository : IRepository<UserRoleAssignment>
    {
        /// <summary>
        /// Gets all roles assigned to a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>Collection of role assignments</returns>
        Task<IEnumerable<UserRoleAssignment>> GetUserRoleAssignmentsAsync(int userId);

        /// <summary>
        /// Gets a specific role assignment
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">Role enum value</param>
        /// <returns>Role assignment or null if not found</returns>
        Task<UserRoleAssignment> GetRoleAssignmentAsync(int userId, UserRole role);

        /// <summary>
        /// Adds a role to a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">Role to assign</param>
        /// <returns>The created role assignment</returns>
        Task<UserRoleAssignment> AssignRoleToUserAsync(int userId, UserRole role);

        /// <summary>
        /// Removes a role from a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">Role to remove</param>
        /// <returns>True if the role was removed</returns>
        Task<bool> RemoveRoleFromUserAsync(int userId, UserRole role);

        /// <summary>
        /// Gets users who have a specific role
        /// </summary>
        /// <param name="role">Role to search for</param>
        /// <returns>Collection of users with the role</returns>
        Task<IEnumerable<User>> GetUsersInRoleAsync(UserRole role);

        /// <summary>
        /// Checks if a user has a specific role
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <param name="role">Role to check</param>
        /// <returns>True if the user has the role</returns>
        Task<bool> UserHasRoleAsync(int userId, UserRole role);

        /// <summary>
        /// Gets the highest role level for a user
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>The highest role level</returns>
        Task<UserRole> GetUserHighestRoleAsync(int userId);
    }
}