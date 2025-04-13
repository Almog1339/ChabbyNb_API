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
    public class RoleRepository : IRoleRepository
    {
        private readonly ChabbyNbDbContext _context;

        public RoleRepository(ChabbyNbDbContext context)
        {
            _context = context;
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

            // Other roles are not available by default
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
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
                return false;

            // Special handling for Admin role
            if (role == UserRole.Admin)
            {
                user.IsAdmin = false;
                await _context.SaveChangesAsync();
                return true;
            }

            // Other roles cannot be removed (Guest is automatic)
            return true;
        }
    }
}