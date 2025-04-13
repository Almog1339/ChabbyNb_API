using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Repositories.Interface;
using ChabbyNb_API.Services.Auth;
using Microsoft.EntityFrameworkCore;

namespace ChabbyNb_API.Repositories
{
    /// <summary>
    /// Implementation of the role repository
    /// </summary>
    public class RoleRepository : Repository<User>, IRoleRepository
    {
        public RoleRepository(ChabbyNbDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<string>> GetUserRolesAsync(int userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return Enumerable.Empty<string>();

            var roles = new List<string>();

            // Legacy admin system support
            if (user.IsAdmin)
                roles.Add(UserRole.Admin.ToString());

            // Always add Guest role to registered users
            roles.Add(UserRole.Guest.ToString());

            return roles.Distinct();
        }

        public async Task<bool> HasRoleAsync(int userId, UserRole role)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return false;

            // For Admin role, check IsAdmin flag
            if (role == UserRole.Admin)
                return user.IsAdmin;

            // Guest role is always available to registered users
            if (role == UserRole.Guest)
                return true;

            // All other roles are not available by default
            return false;
        }

        public async Task<bool> AssignRoleToUserAsync(int userId, UserRole role)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return false;

            // Special handling for Admin role
            if (role == UserRole.Admin)
            {
                user.IsAdmin = true;
                await _context.SaveChangesAsync();
                return true;
            }

            // Other roles are handled implicitly (Guest is automatic)
            return true;
        }

        public async Task<bool> RemoveRoleFromUserAsync(int userId, UserRole role)
        {
            var roleAssignment = await GetRoleAssignmentAsync(userId, role);
            if (roleAssignment == null)
            {
                return false; // Role not assigned
            }

            _dbSet.Remove(roleAssignment);
            return await _context.SaveChangesAsync() > 0;
        }

        public async Task<IEnumerable<User>> GetUsersInRoleAsync(UserRole role)
        {
            return await _dbSet
                .Where(r => r.Role == (int)role)
                .Join(_context.Users,
                    userRole => userRole.UserId,
                    user => user.UserID,
                    (userRole, user) => user)
                .ToListAsync();
        }

        public async Task<bool> UserHasRoleAsync(int userId, UserRole role)
        {
            // Special case for Guest role
            if (role == UserRole.Guest)
            {
                return true;
            }

            return await _dbSet.AnyAsync(r => r.UserId == userId && r.Role == (int)role);
        }

        public async Task<UserRole> GetUserHighestRoleAsync(int userId)
        {
            // Check if user is an admin in the legacy system
            var user = await _context.Users.FindAsync(userId);
            if (user?.IsAdmin == true)
            {
                return UserRole.Admin;
            }

            // Get highest role from assignments
            var roles = await GetUserRoleAssignmentsAsync(userId);
            if (!roles.Any())
            {
                return UserRole.Guest; // Default role
            }

            return (UserRole)roles.Max(r => r.Role);
        }
    }
}