using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;

namespace ChabbyNb_API.Services.Auth
{
    /// <summary>
    /// Enum representing all available roles in the system
    /// </summary>
    public enum UserRole
    {
        Guest = 0,
        CleaningStaff = 10,
        Admin = 100
    }

    /// <summary>
    /// Interface for the role service
    /// </summary>
    public interface IRoleService
    {
        /// <summary>
        /// Gets all available user roles
        /// </summary>
        IEnumerable<string> GetAllRoles();

        /// <summary>
        /// Gets all roles assigned to a user
        /// </summary>
        Task<IEnumerable<string>> GetUserRolesAsync(int userId);

        /// <summary>
        /// Gets the highest role level for a user
        /// </summary>
        Task<UserRole> GetUserHighestRoleAsync(int userId);

        /// <summary>
        /// Assigns a role to a user
        /// </summary>
        Task<bool> AssignRoleToUserAsync(int userId, string role);

        /// <summary>
        /// Removes a role from a user
        /// </summary>
        Task<bool> RemoveRoleFromUserAsync(int userId, string role);

        /// <summary>
        /// Gets all users assigned to a specific role
        /// </summary>
        Task<IEnumerable<UserDto>> GetUsersInRoleAsync(string role);

        /// <summary>
        /// Checks if a user has a specific role
        /// </summary>
        Task<bool> UserHasRoleAsync(int userId, string role);

        /// <summary>
        /// Generates role claims for a user that can be included in their JWT token
        /// </summary>
        Task<IEnumerable<Claim>> GenerateUserRoleClaimsAsync(User user);
    }

    /// <summary>
    /// Implementation of the role service
    /// </summary>
    public class RoleService : IRoleService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly ILogger<RoleService> _logger;

        public RoleService(
            ChabbyNbDbContext context,
            ILogger<RoleService> logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Gets all available roles in the system
        /// </summary>
        public IEnumerable<string> GetAllRoles()
        {
            // Return all roles defined in the UserRole enum
            return Enum.GetNames(typeof(UserRole));
        }

        /// <summary>
        /// Gets all roles assigned to a user
        /// </summary>
        public async Task<IEnumerable<string>> GetUserRolesAsync(int userId)
        {
            try
            {
                // Get the user to check legacy IsAdmin flag
                var user = await _context.Users.FindAsync(userId);

                if (user == null)
                {
                    _logger.LogWarning($"Attempted to get roles for non-existent user ID {userId}");
                    return new List<string> { UserRole.Guest.ToString() };
                }

                var roles = new List<string>();

                // Legacy admin system support - if user is marked as admin in the User model
                if (user.IsAdmin)
                {
                    roles.Add(UserRole.Admin.ToString());
                }

                // If no roles found and not an admin, use Guest as default
                if (!roles.Any())
                {
                    // Always add Guest role to registered users
                    roles.Add(UserRole.Guest.ToString());

                    // Also add Guest role as a default
                    roles.Add(UserRole.Guest.ToString());
                }

                // Return distinct roles to avoid duplicates
                return roles.Distinct();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting roles for user ID {userId}");
                return new List<string> { UserRole.Guest.ToString() };
            }
        }

        /// <summary>
        /// Gets the highest role level for a user
        /// </summary>
        public async Task<UserRole> GetUserHighestRoleAsync(int userId)
        {
            try
            {
                // Get the user to check legacy IsAdmin flag
                var user = await _context.Users.FindAsync(userId);

                if (user == null)
                {
                    _logger.LogWarning($"Attempted to get highest role for non-existent user ID {userId}");
                    return UserRole.Guest;
                }

                // If user is admin in legacy system, that's the highest role
                if (user.IsAdmin)
                {
                    return UserRole.Admin;
                }

                // If no assigned roles, return Guest for registered users
                return UserRole.Guest;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting highest role for user ID {userId}");
                return UserRole.Guest;
            }
        }

        /// <summary>
        /// Assigns a role to a user
        /// </summary>
        public async Task<bool> AssignRoleToUserAsync(int userId, string role)
        {
            try
            {
                // Validate user
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"Attempted to assign role to non-existent user ID {userId}");
                    return false;
                }

                // Validate role
                if (!Enum.TryParse<UserRole>(role, out var roleEnum))
                {
                    _logger.LogWarning($"Attempted to assign invalid role '{role}' to user ID {userId}");
                    return false;
                }

                // Special handling for Admin role - use the legacy system
                if (roleEnum == UserRole.Admin)
                {
                    user.IsAdmin = true;
                    _context.Entry(user).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"User ID {userId} marked as admin in legacy system");
                    return true;
                }

                // For other roles, they're either built-in or non-existent in new system
                // Just log and return true since we don't need to store them anymore
                _logger.LogInformation($"Role assignment for {role} to user ID {userId} is handled by the system");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning role {role} to user ID {userId}");
                return false;
            }
        }

        /// <summary>
        /// Removes a role from a user
        /// </summary>
        public async Task<bool> RemoveRoleFromUserAsync(int userId, string role)
        {
            try
            {
                // Validate user
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"Attempted to remove role from non-existent user ID {userId}");
                    return false;
                }

                // Validate role
                if (!Enum.TryParse<UserRole>(role, out var roleEnum))
                {
                    _logger.LogWarning($"Attempted to remove invalid role '{role}' from user ID {userId}");
                    return false;
                }

                // Special handling for Admin role - use the legacy system
                if (roleEnum == UserRole.Admin)
                {
                    user.IsAdmin = false;
                    _context.Entry(user).State = EntityState.Modified;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation($"User ID {userId} unmarked as admin in legacy system");
                    return true;
                }

                // For other roles, they're built-in - no need to remove anything
                _logger.LogInformation($"Role removal for {role} from user ID {userId} is handled by the system");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing role {role} from user ID {userId}");
                return false;
            }
        }

        /// <summary>
        /// Gets all users assigned to a specific role
        /// </summary>
        public async Task<IEnumerable<UserDto>> GetUsersInRoleAsync(string role)
        {
            try
            {
                // Validate role
                if (!Enum.TryParse<UserRole>(role, out var roleEnum))
                {
                    _logger.LogWarning($"Attempted to get users for invalid role '{role}'");
                    return Enumerable.Empty<UserDto>();
                }

                List<User> users = new List<User>();

                // Special handling for Admin role - use the legacy system
                if (roleEnum == UserRole.Admin)
                {
                    users = await _context.Users
                        .Where(u => u.IsAdmin)
                        .ToListAsync();
                }
                else
                {
                    // For other roles, return empty list for now
                    users = new List<User>();
                }

                // Convert to DTOs
                var userDtos = users.Select(u => new UserDto
                {
                    UserId = u.UserID,
                    Username = u.Username,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    IsAdmin = u.IsAdmin
                }).ToList();

                return userDtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting users in role {role}");
                return Enumerable.Empty<UserDto>();
            }
        }

        /// <summary>
        /// Checks if a user has a specific role
        /// </summary>
        public async Task<bool> UserHasRoleAsync(int userId, string role)
        {
            try
            {
                // Guest role is always available to everyone
                if (role == UserRole.Guest.ToString())
                {
                    return true;
                }

                // Validate role
                if (!Enum.TryParse<UserRole>(role, out var roleEnum))
                {
                    _logger.LogWarning($"Checked for invalid role '{role}' on user ID {userId}");
                    return false;
                }

                // Get the user to check legacy IsAdmin flag
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return false;
                }

                // Special handling for Admin role - check legacy system
                if (roleEnum == UserRole.Admin && user.IsAdmin)
                {
                    return true;
                }

                // Guest role is always available to registered users
                if (roleEnum == UserRole.Guest)
                {
                    return true;
                }

                // All other roles are not available by default
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if user ID {userId} has role {role}");
                return false;
            }
        }

        /// <summary>
        /// Generates role claims for a user that can be included in their JWT token
        /// </summary>
        public async Task<IEnumerable<Claim>> GenerateUserRoleClaimsAsync(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var claims = new List<Claim>();

            // Legacy IsAdmin claim for backward compatibility
            claims.Add(new Claim("IsAdmin", user.IsAdmin.ToString()));

            // Get all roles for the user
            var roles = await GetUserRolesAsync(user.UserID);

            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            return claims;
        }
    }
}