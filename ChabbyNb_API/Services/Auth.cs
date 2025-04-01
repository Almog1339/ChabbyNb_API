using System.Security.Claims;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using Microsoft.EntityFrameworkCore;

namespace ChabbyNb_API.Services.Auth
{
    /// <summary>
    /// Represents the available roles in the system
    /// </summary>
    public enum UserRole
    {
        Guest = 0,
        ReadOnlyStaff = 10,
        HousekeepingStaff = 20,
        Admin = 100
    }

    /// <summary>
    /// Provides services for managing roles and authorizations
    /// </summary>
    public interface IRoleService
    {
        /// <summary>
        /// Assigns a role to a user
        /// </summary>
        Task<bool> AssignRoleToUserAsync(int userId, UserRole role);

        /// <summary>
        /// Removes a role from a user
        /// </summary>
        Task<bool> RemoveRoleFromUserAsync(int userId, UserRole role);

        /// <summary>
        /// Gets a user's highest role level
        /// </summary>
        Task<UserRole> GetUserHighestRoleAsync(int userId);

        /// <summary>
        /// Gets all roles for a user
        /// </summary>
        Task<IEnumerable<UserRole>> GetUserRolesAsync(int userId);

        /// <summary>
        /// Checks if a user has a specific role
        /// </summary>
        Task<bool> UserHasRoleAsync(int userId, UserRole role);

        /// <summary>
        /// Gets all users with a specific role
        /// </summary>
        Task<IEnumerable<User>> GetUsersInRoleAsync(UserRole role);
    }

    public class RoleService : IRoleService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly ILogger<RoleService> _logger;

        public RoleService(ChabbyNbDbContext context, ILogger<RoleService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<bool> AssignRoleToUserAsync(int userId, UserRole role)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"Failed to assign role {role} to user ID {userId}. User not found.");
                    return false;
                }

                var userRole = await _context.UserRoleAssignments
                    .FirstOrDefaultAsync(r => r.UserId == userId && r.Role == (int)role);

                if (userRole != null)
                {
                    // User already has this role
                    return true;
                }

                // Add the new role
                userRole = new UserRoleAssignment
                {
                    UserId = userId,
                    Role = (int)role,
                    AssignedDate = DateTime.UtcNow
                };

                _context.UserRoleAssignments.Add(userRole);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Role {role} assigned to user ID {userId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning role {role} to user ID {userId}");
                return false;
            }
        }

        public async Task<bool> RemoveRoleFromUserAsync(int userId, UserRole role)
        {
            try
            {
                var userRole = await _context.UserRoleAssignments
                    .FirstOrDefaultAsync(r => r.UserId == userId && r.Role == (int)role);

                if (userRole == null)
                {
                    // User doesn't have this role
                    return true;
                }

                _context.UserRoleAssignments.Remove(userRole);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Role {role} removed from user ID {userId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing role {role} from user ID {userId}");
                return false;
            }
        }

        public async Task<UserRole> GetUserHighestRoleAsync(int userId)
        {
            try
            {
                var roles = await _context.UserRoleAssignments
                    .Where(r => r.UserId == userId)
                    .ToListAsync();

                if (!roles.Any())
                {
                    return UserRole.Guest; // Default role
                }

                return (UserRole)roles.Max(r => r.Role);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting highest role for user ID {userId}");
                return UserRole.Guest; // Default to lowest access in case of error
            }
        }

        public async Task<IEnumerable<UserRole>> GetUserRolesAsync(int userId)
        {
            try
            {
                // First check if the user is an admin in the legacy system
                var user = await _context.Users.FindAsync(userId);
                var roles = new List<UserRole>();

                // If user is an admin in the legacy system, add Admin role
                if (user != null && user.IsAdmin)
                {
                    roles.Add(UserRole.Admin);
                }

                // Get roles from the new role assignments system
                var assignedRoles = await _context.UserRoleAssignments
                    .Where(r => r.UserId == userId)
                    .Select(r => (UserRole)r.Role)
                    .ToListAsync();

                // Add assigned roles to the list
                roles.AddRange(assignedRoles);

                // If no roles assigned, use Guest as default
                if (!roles.Any())
                {
                    roles.Add(UserRole.Guest);
                }

                // Return distinct roles (in case Admin was added twice)
                return roles.Distinct();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting roles for user ID {userId}");
                return new List<UserRole> { UserRole.Guest };
            }
        }

        public async Task<bool> UserHasRoleAsync(int userId, UserRole role)
        {
            // Special case for Guest role - everyone has at least Guest access
            if (role == UserRole.Guest)
            {
                return true;
            }

            try
            {
                var userRole = await _context.UserRoleAssignments
                    .AnyAsync(r => r.UserId == userId && r.Role == (int)role);

                return userRole;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if user ID {userId} has role {role}");
                return false;
            }
        }

        public async Task<IEnumerable<User>> GetUsersInRoleAsync(UserRole role)
        {
            try
            {
                var users = await _context.UserRoleAssignments
                    .Where(r => r.Role == (int)role)
                    .Join(_context.Users,
                        userRole => userRole.UserId,
                        user => user.UserID,
                        (userRole, user) => user)
                    .ToListAsync();

                return users;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting users in role {role}");
                return new List<User>();
            }
        }
    }
}