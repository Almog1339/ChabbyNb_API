using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;

namespace ChabbyNb_API.Services.Auth
{
    /// <summary>
    /// Interface for the account lockout service
    /// </summary>
    public interface IAccountLockoutService
    {
        /// <summary>
        /// Checks if a user account is locked out
        /// </summary>
        Task<bool> IsAccountLockedOutAsync(int userId);

        /// <summary>
        /// Checks if a user account is locked out by email
        /// </summary>
        Task<bool> IsAccountLockedOutAsync(string email);

        /// <summary>
        /// Records a failed login attempt and locks the account if necessary
        /// </summary>
        Task<bool> RecordFailedLoginAttemptAsync(string email, string ipAddress);

        /// <summary>
        /// Records a successful login and resets failed attempt counters
        /// </summary>
        Task<bool> RecordSuccessfulLoginAsync(int userId);

        /// <summary>
        /// Manually locks an account
        /// </summary>
        Task<bool> LockoutAccountAsync(int userId, string reason, string ipAddress, int? minutes = null);

        /// <summary>
        /// Unlocks a locked account
        /// </summary>
        Task<bool> UnlockAccountAsync(int userId, int adminId, string notes);

        /// <summary>
        /// Gets lockout details for a user
        /// </summary>
        Task<UserAccountLockout> GetLockoutDetailsAsync(int userId);
    }

    /// <summary>
    /// Implementation of the account lockout service
    /// </summary>
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
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            // Load settings from configuration or use defaults
            _maxFailedAttempts = _configuration.GetValue<int>("Security:MaxFailedLoginAttempts", 5);
            _defaultLockoutMinutes = _configuration.GetValue<int>("Security:DefaultLockoutMinutes", 15);
        }

        /// <summary>
        /// Checks if a user account is locked out
        /// </summary>
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

                // Check if lockout has expired
                if (lockout.LockoutEnd != null && lockout.LockoutEnd <= DateTime.UtcNow)
                {
                    // Lockout has expired, update the record
                    lockout.IsActive = false;
                    await _context.SaveChangesAsync();
                    return false;
                }

                // Account is locked out
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking account lockout for user ID {userId}");
                return false; // Default to allowing access in case of error
            }
        }

        /// <summary>
        /// Checks if a user account is locked out by email
        /// </summary>
        public async Task<bool> IsAccountLockedOutAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            try
            {
                // Find the user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

                if (user == null)
                {
                    return false; // User doesn't exist, so not locked out
                }

                // Check if the user's account is locked
                return await IsAccountLockedOutAsync(user.UserID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking account lockout for email {email}");
                return false; // Default to allowing access in case of error
            }
        }

        /// <summary>
        /// Records a failed login attempt and locks the account if necessary
        /// </summary>
        public async Task<bool> RecordFailedLoginAttemptAsync(string email, string ipAddress)
        {
            if (string.IsNullOrEmpty(email))
            {
                return false;
            }

            try
            {
                // Find the user by email
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

                // Record the event even if user doesn't exist (for security auditing)
                var securityEvent = new UserSecurityEvent
                {
                    UserId = user?.UserID ?? 0, // Use 0 for non-existent users
                    EventType = "FailedLogin",
                    EventTime = DateTime.UtcNow,
                    IpAddress = ipAddress,
                    AdditionalInfo = user == null
                        ? $"Failed login attempt for non-existent email: {email}"
                        : "Failed login attempt"
                };

                _context.UserSecurityEvents.Add(securityEvent);
                await _context.SaveChangesAsync();

                // If user doesn't exist, just return (we've logged the attempt)
                if (user == null)
                {
                    return true;
                }

                // Check if the account is already locked
                if (await IsAccountLockedOutAsync(user.UserID))
                {
                    // Log attempt on locked account but don't increment counters
                    var lockedEvent = new UserSecurityEvent
                    {
                        UserId = user.UserID,
                        EventType = "FailedLoginWhenLocked",
                        EventTime = DateTime.UtcNow,
                        IpAddress = ipAddress,
                        AdditionalInfo = "Failed login attempt on locked account"
                    };

                    _context.UserSecurityEvents.Add(lockedEvent);
                    await _context.SaveChangesAsync();
                    return true;
                }

                // Count recent failed attempts (last 30 minutes)
                var recentFailures = await _context.UserSecurityEvents
                    .CountAsync(e => e.UserId == user.UserID &&
                           e.EventType == "FailedLogin" &&
                           e.EventTime > DateTime.UtcNow.AddMinutes(-30));

                // If too many failures, lock the account
                if (recentFailures + 1 >= _maxFailedAttempts) // +1 to count the current failure
                {
                    await LockoutAccountAsync(
                        user.UserID,
                        $"Too many failed login attempts ({recentFailures + 1})",
                        ipAddress,
                        _defaultLockoutMinutes);
                }

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error recording failed login attempt for email {email}");
                return false;
            }
        }

        /// <summary>
        /// Records a successful login and resets failed attempt counters
        /// </summary>
        public async Task<bool> RecordSuccessfulLoginAsync(int userId)
        {
            try
            {
                // Log the successful login
                var successEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "SuccessfulLogin",
                    EventTime = DateTime.UtcNow,
                    IpAddress = "127.0.0.1", // In a real app, get from HttpContext
                    AdditionalInfo = "Successful login"
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

        /// <summary>
        /// Manually locks an account
        /// </summary>
        public async Task<bool> LockoutAccountAsync(int userId, string reason, string ipAddress, int? minutes = null)
        {
            try
            {
                // Validate user exists
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning($"Attempted to lock non-existent user with ID {userId}");
                    return false;
                }

                // Set lockout duration
                var lockoutMinutes = minutes ?? _defaultLockoutMinutes;
                var lockoutEnd = DateTime.UtcNow.AddMinutes(lockoutMinutes);

                // Get recent failed attempts count for context
                int failedAttempts = await _context.UserSecurityEvents
                    .CountAsync(e => e.UserId == userId &&
                           e.EventType == "FailedLogin" &&
                           e.EventTime > DateTime.UtcNow.AddMinutes(-30));

                // Create lockout record
                var lockout = new UserAccountLockout
                {
                    UserId = userId,
                    LockoutStart = DateTime.UtcNow,
                    LockoutEnd = lockoutEnd,
                    Reason = reason,
                    IpAddress = ipAddress,
                    FailedAttempts = failedAttempts,
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

        /// <summary>
        /// Unlocks a locked account
        /// </summary>
        public async Task<bool> UnlockAccountAsync(int userId, int adminId, string notes)
        {
            try
            {
                // Find the active lockout
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
                    IpAddress = "127.0.0.1", // In a real app, get from HttpContext
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

        /// <summary>
        /// Gets lockout details for a user
        /// </summary>
        public async Task<UserAccountLockout> GetLockoutDetailsAsync(int userId)
        {
            try
            {
                var lockout = await _context.UserAccountLockouts
                    .Where(l => l.UserId == userId && l.IsActive)
                    .OrderByDescending(l => l.LockoutStart)
                    .FirstOrDefaultAsync();

                // Check if lockout has expired
                if (lockout != null && lockout.LockoutEnd != null && lockout.LockoutEnd <= DateTime.UtcNow)
                {
                    // Lockout has expired, update the record
                    lockout.IsActive = false;
                    await _context.SaveChangesAsync();
                    return null; // No active lockout
                }

                return lockout;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting lockout details for user ID {userId}");
                return null;
            }
        }
    }
}