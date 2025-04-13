using System.Collections.Generic;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Auth;

namespace ChabbyNb_API.Repositories.Interface
{
    /// <summary>
    /// Repository interface for role-specific operations
    /// </summary>
    public interface IRoleRepository 
    {
        Task<IEnumerable<string>> GetUserRolesAsync(int userId);
        Task<bool> HasRoleAsync(int userId, UserRole role);
        Task<bool> AssignRoleToUserAsync(int userId, UserRole role);
        Task<bool> RemoveRoleFromUserAsync(int userId, UserRole role);
    }
}