using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Services.Auth;

namespace ChabbyNb_API.Authorization
{
    // Role requirements
    public class RoleAuthorizationRequirement : IAuthorizationRequirement
    {
        public UserRole MinimumRole { get; }

        public RoleAuthorizationRequirement(UserRole minimumRole)
        {
            MinimumRole = minimumRole;
        }
    }

    // Handler for role authorization
    public class RoleAuthorizationHandler : AuthorizationHandler<RoleAuthorizationRequirement>
    {
        private readonly ILogger<RoleAuthorizationHandler> _logger;

        public RoleAuthorizationHandler(ILogger<RoleAuthorizationHandler> logger)
        {
            _logger = logger;
        }

        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            RoleAuthorizationRequirement requirement)
        {
            if (context.User == null || !context.User.Identity.IsAuthenticated)
            {
                _logger.LogDebug("Authorization failed: user is not authenticated");
                return Task.CompletedTask;
            }

            // Check if the user has the required role claim
            var userRoleClaims = context.User.Claims
                .Where(c => c.Type == ClaimTypes.Role)
                .Select(c =>
                {
                    if (Enum.TryParse<UserRole>(c.Value, out var role))
                        return role;
                    return UserRole.Guest;
                })
                .ToList();

            // Get the user's highest role
            var highestRole = userRoleClaims.Any() ? userRoleClaims.Max() : UserRole.Guest;

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

    // Resource-based authorization for Housekeeping staff
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
                if (requirement is HousekeepingRequirement housekeepingReq)
                {
                    if (context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        (c.Value == UserRole.CleaningStaff.ToString() || c.Value == UserRole.Admin.ToString())))
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

    // Read-only authorization handler
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
                if (requirement is ReadOnlyRequirement readOnlyReq)
                {
                    if (context.User.HasClaim(c => c.Type == ClaimTypes.Role &&
                        (c.Value == UserRole.CleaningStaff.ToString() ||
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

    // Define requirements
    public class HousekeepingRequirement : IAuthorizationRequirement { }
    public class ReadOnlyRequirement : IAuthorizationRequirement { }
}