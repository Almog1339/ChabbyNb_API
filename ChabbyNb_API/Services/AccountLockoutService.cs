using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;

namespace ChabbyNb_API.Services.Auth
{
    public interface IAccountLockoutService
    {
        Task<bool> IsAccountLockedOutAsync(int userId);
        Task<bool> IsAccountLockedOutAsync(string email);
        Task<bool> RecordFailedLoginAttemptAsync(string email, string ipAddress);
        Task<bool> RecordSuccessfulLoginAsync(int userId);
        Task<bool> LockoutAccountAsync(int userId, string reason, string ipAddress, int? minutes = null);
        Task<bool> UnlockAccountAsync(int userId, int adminId, string notes);
    }

    public class AccountLockoutService : IAccountLockoutService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AccountLockoutService> _logger;

        // Default settings
        private readonly int _maxFailedAttempts;
        private readonly int _defaultLockoutMinutes;

        public AccountLockoutService(
            ChabbyNbDbContext context,
            IConfiguration configuration,
            ILogger<AccountLockoutService> logger)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;

            // Load settings from configuration
            _maxFailedAttempts = _configuration.GetValue<int>("Security:MaxFailedLoginAttempts", 5);
            _defaultLockoutMinutes = _configuration.GetValue<int>("Security:DefaultLockoutMinutes", 15);
        }

        public async Task<bool> IsAccountLockedOutAsync(int userId)
        {
            try
            {
                var lockout = await _context.UserAccountLockouts
                    .Where(l => l.UserId == userId && l.IsActive)
                    .OrderByDescending(l => l.LockoutStart)
                    .FirstOrDefaultAsync();

                if (lockout == null)
                {
                    return false; // No active lockout
                }

                // If there's no end date or the end date is in the future, the account is locked
                if (lockout.LockoutEnd == null || lockout.LockoutEnd > DateTime.UtcNow)
                {
                    return true;
                }

                // If the lockout has expired, mark it as inactive
                if (lockout.LockoutEnd <= DateTime.UtcNow)
                {
                    lockout.IsActive = false;
                    await _context.SaveChangesAsync();
                    return false;
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking account lockout for user ID {userId}");
                return false; // Default to allowing access in case of error
            }
        }

        public async Task<bool> IsAccountLockedOutAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == email);

                if (user == null)
                {
                    return false; // User doesn't exist, so not locked out
                }

                return await IsAccountLockedOutAsync(user.UserID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking account lockout for email {email}");
                return false; // Default to allowing access in case of error
            }
        }

        public async Task<bool> RecordFailedLoginAttemptAsync(string email, string ipAddress)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email == email);

                if (user == null)
                {
                    // Record failed attempt even for non-existent users to prevent user enumeration
                    var securityEvent = new UserSecurityEvent
                    {
                        UserId = 0, // Special value for non-existent users
                        EventType = "FailedLogin",
                        EventTime = DateTime.UtcNow,
                        IpAddress = ipAddress,
                        AdditionalInfo = $"Failed login attempt for non-existent email: {email}"
                    };

                    _context.UserSecurityEvents.Add(securityEvent);
                    await _context.SaveChangesAsync();
                    return true;
                }

                // Check if the account is already locked
                if (await IsAccountLockedOutAsync(user.UserID))
                {
                    // Just record the event but don't increment counters
                    var lockedEvent = new UserSecurityEvent
                    {
                        UserId = user.UserID,
                        EventType = "FailedLoginWhenLocked",
                        EventTime = DateTime.UtcNow,
                        IpAddress = ipAddress,
                        AdditionalInfo = $"Failed login attempt when account already locked"
                    };

                    _context.UserSecurityEvents.Add(lockedEvent);
                    await _context.SaveChangesAsync();
                    return true;
                }

                // Record the failed attempt
                var failedEvent = new UserSecurityEvent
                {
                    UserId = user.UserID,
                    EventType = "FailedLogin",
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = $"Failed login attempt"
                };

                _context.UserSecurityEvents.Add(failedEvent);

                // Get recent failed attempts
                var recentFailures = await _context.UserSecurityEvents
                    .Where(e => e.UserId == user.UserID &&
                           e.EventType == "FailedLogin" &&
                           e.EventTime > DateTime.UtcNow.AddMinutes(-30))
                    .CountAsync();

                // If the number of recent failures exceeds the threshold, lock the account
                if (recentFailures + 1 >= _maxFailedAttempts)
                {
                    await LockoutAccountAsync(
                        user.UserID,
                        $"Too many failed login attempts ({recentFailures + 1})",
                        ipAddress,
                        _defaultLockoutMinutes);
                }

                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording failed login attempt for email {email}");
                return false;
            }
        }

        public async Task<bool> RecordSuccessfulLoginAsync(int userId)
        {
            try
            {
                // Clear failed login attempts history
                var successEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "SuccessfulLogin",
                    EventTime = DateTime.UtcNow,
                    IpAddress = GetIpAddress(),
                    AdditionalInfo = $"Successful login"
                };

                _context.UserSecurityEvents.Add(successEvent);
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording successful login for user ID {userId}");
                return false;
            }
        }

        public async Task<bool> LockoutAccountAsync(int userId, string reason, string ipAddress, int? minutes = null)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"Attempted to lock non-existent user with ID {userId}");
                    return false;
                }

                // Calculate lockout end time
                var lockoutMinutes = minutes ?? _defaultLockoutMinutes;
                var lockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);

                // Create lockout record
                var lockout = new UserAccountLockout
                {
                    UserId = userId,
                    LockoutStart = DateTime.UtcNow,
                    LockoutEnd = lockoutEnd,
                    Reason = reason,
                    IpAddress = ipAddress,
                    FailedAttempts = await _context.UserSecurityEvents
                        .Where(e => e.UserId == userId &&
                               e.EventType == "FailedLogin" &&
                               e.EventTime > DateTime.UtcNow.AddMinutes(-30))
                        .CountAsync(),
                    IsActive = true
                };

                _context.UserAccountLockouts.Add(lockout);

                // Record security event
                var lockoutEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "AccountLockout",
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = $"Account locked for {lockoutMinutes} minutes. Reason: {reason}"
                };

                _context.UserSecurityEvents.Add(lockoutEvent);
                await _context.SaveChangesAsync();

                _logger.LogWarning($"Account locked for user ID {userId}. Reason: {reason}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error locking account for user ID {userId}");
                return false;
            }
        }

        public async Task<bool> UnlockAccountAsync(int userId, int adminId, string notes)
        {
            try
            {
                var activeLockout = await _context.UserAccountLockouts
                    .Where(l => l.UserId == userId && l.IsActive)
                    .OrderByDescending(l => l.LockoutStart)
                    .FirstOrDefaultAsync();

                if (activeLockout == null)
                {
                    _logger.LogWarning($"Attempted to unlock account for user ID {userId}, but no active lockout found");
                    return false;
                }

                // Update lockout record
                activeLockout.IsActive = false;
                activeLockout.UnlockedAt = DateTime.UtcNow;
                activeLockout.UnlockedByAdminId = adminId.ToString();
                activeLockout.Notes = notes;

                // Record security event
                var unlockEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "AccountUnlock",
                    EventTime = DateTime.UtcNow,
                    IpAddress = GetIpAddress(),
                    AdditionalInfo = $"Account unlocked by admin ID {adminId}. Notes: {notes}"
                };

                _context.UserSecurityEvents.Add(unlockEvent);
                await _context.SaveChangesAsync();

                _logger.LogInformation($"Account unlocked for user ID {userId} by admin ID {adminId}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error unlocking account for user ID {userId}");
                return false;
            }
        }

        private string GetIpAddress()
        {
            // In a real application, you would get this from the HttpContext
            // This is a simplified version
            return "127.0.0.1";
        }
    }
}