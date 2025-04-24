using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace ChabbyNb_API.Services.Auth
{
    #region Role and Permission Requirements

    /// <summary>
    /// Authorization requirement for minimum role level
    /// </summary>
    public class RoleRequirement : IAuthorizationRequirement
    {
        public UserRole MinimumRole { get; }

        public RoleRequirement(UserRole minimumRole)
        {
            MinimumRole = minimumRole;
        }
    }

    /// <summary>
    /// Authorization requirement for specific permissions
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public UserPermission RequiredPermission { get; }

        public PermissionRequirement(UserPermission requiredPermission)
        {
            RequiredPermission = requiredPermission;
        }
    }

    /// <summary>
    /// Combined role and permission requirement
    /// </summary>
    public class RoleAndPermissionRequirement : IAuthorizationRequirement
    {
        public UserRole MinimumRole { get; }
        public UserPermission RequiredPermission { get; }

        public RoleAndPermissionRequirement(UserRole minimumRole, UserPermission requiredPermission)
        {
            MinimumRole = minimumRole;
            RequiredPermission = requiredPermission;
        }
    }

    #endregion

    #region Authorization Handlers

    /// <summary>
    /// Authorization handler for role-based requirements
    /// </summary>
    public class RoleAuthorizationHandler : AuthorizationHandler<RoleRequirement>
    {
        private readonly ILogger<RoleAuthorizationHandler> _logger;

        public RoleAuthorizationHandler(ILogger<RoleAuthorizationHandler> logger)
        {
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RoleRequirement requirement)
        {
            if (context.User == null || !context.User.Identity.IsAuthenticated)
            {
                _logger.LogDebug("Authorization failed: user is not authenticated");
                return Task.CompletedTask;
            }

            // Check if the user has the IsAdmin claim for backward compatibility
            if (requirement.MinimumRole == UserRole.Admin &&
                context.User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True"))
            {
                _logger.LogDebug("Authorization succeeded: user has IsAdmin claim");
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Check for role claims
            var userRoleClaims = context.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c =>
                {
                    if (Enum.TryParse<UserRole>(c.Value, out var role))
                        return role;
                    return UserRole.Everyone;
                })
                .ToList();

            // Get the user's highest role
            var highestRole = userRoleClaims.Any() ? userRoleClaims.Max() : UserRole.Everyone;

            // Check if the user's highest role is sufficient
            if (highestRole >= requirement.MinimumRole)
            {
                _logger.LogDebug($"Authorization succeeded: user has role {highestRole} which meets minimum requirement of {requirement.MinimumRole}");
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogDebug($"Authorization failed: user's highest role {highestRole} does not meet minimum requirement of {requirement.MinimumRole}");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Authorization handler for permission-based requirements
    /// </summary>
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(ILogger<PermissionAuthorizationHandler> logger)
        {
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            if (context.User == null || !context.User.Identity.IsAuthenticated)
            {
                _logger.LogDebug("Authorization failed: user is not authenticated");
                return Task.CompletedTask;
            }

            // Check for admin (admins have all permissions)
            if (context.User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True"))
            {
                _logger.LogDebug("Authorization succeeded: user is admin and has all permissions");
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Check for permission claim
            var permissionsClaim = context.User.FindFirst("Permissions");
            if (permissionsClaim != null && int.TryParse(permissionsClaim.Value, out int permissionsValue))
            {
                // Check if the user has the required permission
                if ((permissionsValue & (int)requirement.RequiredPermission) == (int)requirement.RequiredPermission)
                {
                    _logger.LogDebug($"Authorization succeeded: user has permission {requirement.RequiredPermission}");
                    context.Succeed(requirement);
                }
                else
                {
                    _logger.LogDebug($"Authorization failed: user does not have permission {requirement.RequiredPermission}");
                }
            }
            else
            {
                _logger.LogDebug("Authorization failed: user has no permissions claim");
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Authorization handler for combined role and permission requirements
    /// </summary>
    public class RoleAndPermissionAuthorizationHandler : AuthorizationHandler<RoleAndPermissionRequirement>
    {
        private readonly ILogger<RoleAndPermissionAuthorizationHandler> _logger;

        public RoleAndPermissionAuthorizationHandler(ILogger<RoleAndPermissionAuthorizationHandler> logger)
        {
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RoleAndPermissionRequirement requirement)
        {
            if (context.User == null || !context.User.Identity.IsAuthenticated)
            {
                _logger.LogDebug("Authorization failed: user is not authenticated");
                return Task.CompletedTask;
            }

            // Check for admin (admins have all roles and permissions)
            if (context.User.HasClaim(c => c.Type == "IsAdmin" && c.Value == "True"))
            {
                _logger.LogDebug("Authorization succeeded: user is admin");
                context.Succeed(requirement);
                return Task.CompletedTask;
            }

            // Check for role claims
            var userRoleClaims = context.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c =>
                {
                    if (Enum.TryParse<UserRole>(c.Value, out var role))
                        return role;
                    return UserRole.Everyone;
                })
                .ToList();

            // Get the user's highest role
            var highestRole = userRoleClaims.Any() ? userRoleClaims.Max() : UserRole.Everyone;

            // Check if the user's highest role is sufficient
            bool hasRequiredRole = highestRole >= requirement.MinimumRole;

            if (!hasRequiredRole)
            {
                _logger.LogDebug($"Authorization failed: user's highest role {highestRole} does not meet minimum requirement of {requirement.MinimumRole}");
                return Task.CompletedTask;
            }

            // Check for permission claim
            var permissionsClaim = context.User.FindFirst("Permissions");
            bool hasRequiredPermission = false;

            if (permissionsClaim != null && int.TryParse(permissionsClaim.Value, out int permissionsValue))
            {
                // Check if the user has the required permission
                hasRequiredPermission = (permissionsValue & (int)requirement.RequiredPermission) == (int)requirement.RequiredPermission;
            }

            if (hasRequiredPermission)
            {
                _logger.LogDebug($"Authorization succeeded: user has role {highestRole} and permission {requirement.RequiredPermission}");
                context.Succeed(requirement);
            }
            else
            {
                _logger.LogDebug($"Authorization failed: user has role {highestRole} but lacks permission {requirement.RequiredPermission}");
            }

            return Task.CompletedTask;
        }
    }

    #endregion

    #region Legacy Authorization Handlers

    // Legacy handler for the CleaningStaff role
    public class HousekeepingAuthorizationHandler : IAuthorizationHandler
    {
        private readonly ILogger<HousekeepingAuthorizationHandler> _logger;

        public HousekeepingAuthorizationHandler(ILogger<HousekeepingAuthorizationHandler> logger)
        {
            _logger = logger;
        }

        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            var pendingRequirements = context.PendingRequirements.ToList();

            foreach (var requirement in pendingRequirements)
            {
                if (requirement is HousekeepingRequirement)
                {
                    if (context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        (c.Value == UserRole.CleaningStaff.ToString() ||
                         c.Value == UserRole.Partner.ToString() ||
                         c.Value == UserRole.Admin.ToString())))
                    {
                        context.Succeed(requirement);
                    }
                    else
                    {
                        _logger.LogDebug("User does not have housekeeping access");
                    }
                }
            }

            return Task.CompletedTask;
        }
    }

    // Legacy handler for read-only permissions
    public class ReadOnlyAuthorizationHandler : IAuthorizationHandler
    {
        private readonly ILogger<ReadOnlyAuthorizationHandler> _logger;

        public ReadOnlyAuthorizationHandler(ILogger<ReadOnlyAuthorizationHandler> logger)
        {
            _logger = logger;
        }

        public Task HandleAsync(AuthorizationHandlerContext context)
        {
            var pendingRequirements = context.PendingRequirements.ToList();

            foreach (var requirement in pendingRequirements)
            {
                if (requirement is ReadOnlyRequirement)
                {
                    if (context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        (c.Value == UserRole.CleaningStaff.ToString() ||
                         c.Value == UserRole.Partner.ToString() ||
                         c.Value == UserRole.Admin.ToString())))
                    {
                        context.Succeed(requirement);
                    }
                    else
                    {
                        _logger.LogDebug("User does not have read access");
                    }
                }
            }

            return Task.CompletedTask;
        }
    }

    // Legacy requirements
    public class HousekeepingRequirement : IAuthorizationRequirement { }
    public class ReadOnlyRequirement : IAuthorizationRequirement { }

    #endregion
}