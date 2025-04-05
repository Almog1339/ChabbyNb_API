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
    public class RoleRepository : Repository<UserRoleAssignment>, IRoleRepository
    {
        public RoleRepository(ChabbyNbDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<UserRoleAssignment>> GetUserRoleAssignmentsAsync(int userId)
        {
            return await _dbSet
                .Where(r => r.UserId == userId)
                .ToListAsync();
        }

        public async Task<UserRoleAssignment> GetRoleAssignmentAsync(int userId, UserRole role)
        {
            return await _dbSet
                .FirstOrDefaultAsync(r => r.UserId == userId && r.Role == (int)role);
        }

        public async Task<UserRoleAssignment> AssignRoleToUserAsync(int userId, UserRole role)
        {
            // Check if user exists
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                throw new ArgumentException($"User with ID {userId} not found.");
            }

            // Check if the role assignment already exists
            var existingRole = await GetRoleAssignmentAsync(userId, role);
            if (existingRole != null)
            {
                return existingRole; // Role already assigned
            }

            // Create new role assignment
            var roleAssignment = new UserRoleAssignment
            {
                UserId = userId,
                Role = (int)role,
                AssignedDate = DateTime.UtcNow
            };

            await _dbSet.AddAsync(roleAssignment);
            await _context.SaveChangesAsync();

            return roleAssignment;
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