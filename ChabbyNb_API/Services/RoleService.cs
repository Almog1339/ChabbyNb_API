using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Repositories;
using ChabbyNb_API.Services.Auth;
using ChabbyNb_API.Services.Interfaces;
using Microsoft.Extensions.Logging;

namespace ChabbyNb_API.Services
{
    /// <summary>
    /// Implementation of the role service
    /// </summary>
    public class RoleService : IRoleService
    {
        private readonly IUnitOfWork _unitOfWork;
        private readonly ILogger<RoleService> _logger;

        public RoleService(
            IUnitOfWork unitOfWork,
            ILogger<RoleService> logger)
        {
            _unitOfWork = unitOfWork ?? throw new ArgumentNullException(nameof(unitOfWork));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public IEnumerable<string> GetAllRoles()
        {
            // Return all available roles from the UserRole enum
            return Enum.GetNames(typeof(UserRole));
        }

        public async Task<IEnumerable<string>> GetUserRolesAsync(int userId)
        {
            try
            {
                // First check if the user is an admin in the legacy system
                var user = await _unitOfWork.Users.GetByIdAsync(userId);
                var roles = new List<string>();

                // If user is an admin in the legacy system, add Admin role
                if (user != null && user.IsAdmin)
                {
                    roles.Add(UserRole.Admin.ToString());
                }

                // Get roles from the new role assignments system
                var assignedRoles = await _unitOfWork.Roles.GetUserRoleAssignmentsAsync(userId);

                foreach (var role in assignedRoles)
                {
                    if (Enum.IsDefined(typeof(UserRole), role.Role))
                    {
                        roles.Add(((UserRole)role.Role).ToString());
                    }
                }

                // If no roles assigned, use Guest as default
                if (!roles.Any())
                {
                    roles.Add(UserRole.Guest.ToString());
                }

                // Return distinct roles (in case Admin was added twice)
                return roles.Distinct();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting roles for user ID {userId}");
                return new List<string> { UserRole.Guest.ToString() };
            }
        }

        public async Task<UserRole> GetUserHighestRoleAsync(int userId)
        {
            return await _unitOfWork.Roles.GetUserHighestRoleAsync(userId);
        }

        public async Task<bool> AssignRoleToUserAsync(int userId, string role)
        {
            // Convert string role to enum
            if (!Enum.TryParse<UserRole>(role, out var roleEnum))
            {
                throw new ArgumentException($"Invalid role: {role}");
            }

            try
            {
                await _unitOfWork.Roles.AssignRoleToUserAsync(userId, roleEnum);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error assigning role {role} to user {userId}");
                return false;
            }
        }

        public async Task<bool> RemoveRoleFromUserAsync(int userId, string role)
        {
            // Convert string role to enum
            if (!Enum.TryParse<UserRole>(role, out var roleEnum))
            {
                throw new ArgumentException($"Invalid role: {role}");
            }

            try
            {
                return await _unitOfWork.Roles.RemoveRoleFromUserAsync(userId, roleEnum);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error removing role {role} from user {userId}");
                return false;
            }
        }

        public async Task<IEnumerable<UserDto>> GetUsersInRoleAsync(string role)
        {
            // Convert string role to enum
            if (!Enum.TryParse<UserRole>(role, out var roleEnum))
            {
                throw new ArgumentException($"Invalid role: {role}");
            }

            try
            {
                var users = await _unitOfWork.Roles.GetUsersInRoleAsync(roleEnum);

                // Convert to DTOs
                return users.Select(u => new UserDto
                {
                    UserId = u.UserID,
                    Username = u.Username,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    IsAdmin = u.IsAdmin
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting users in role {role}");
                return Enumerable.Empty<UserDto>();
            }
        }

        public async Task<bool> UserHasRoleAsync(int userId, string role)
        {
            // Convert string role to enum
            if (!Enum.TryParse<UserRole>(role, out var roleEnum))
            {
                throw new ArgumentException($"Invalid role: {role}");
            }

            try
            {
                return await _unitOfWork.Roles.UserHasRoleAsync(userId, roleEnum);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking if user {userId} has role {role}");
                return false;
            }
        }

        public async Task<IEnumerable<Claim>> GenerateUserRoleClaimsAsync(User user)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            var claims = new List<Claim>();

            // Add IsAdmin claim for backward compatibility
            claims.Add(new Claim("IsAdmin", user.IsAdmin.ToString()));

            // Get all roles
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