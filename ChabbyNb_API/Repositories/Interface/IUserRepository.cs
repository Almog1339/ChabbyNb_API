using System.Collections.Generic;
using System.Threading.Tasks;
using ChabbyNb_API.Models;

namespace ChabbyNb_API.Repositories.Interface
{
    /// <summary>
    /// Repository interface for user-specific operations
    /// </summary>
    public interface IUserRepository : IRepository<User>
    {
        /// <summary>
        /// Gets a user by their email address
        /// </summary>
        /// <param name="email">Email address to search for</param>
        /// <returns>User or null if not found</returns>
        Task<User> GetByEmailAsync(string email);

        /// <summary>
        /// Gets a user by their username
        /// </summary>
        /// <param name="username">Username to search for</param>
        /// <returns>User or null if not found</returns>
        Task<User> GetByUsernameAsync(string username);

        /// <summary>
        /// Gets a user with their bookings
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>User with bookings</returns>
        Task<User> GetWithBookingsAsync(int userId);

        /// <summary>
        /// Gets a user with their reviews
        /// </summary>
        /// <param name="userId">User ID</param>
        /// <returns>User with reviews</returns>
        Task<User> GetWithReviewsAsync(int userId);

        /// <summary>
        /// Validates user credentials and returns the user if valid
        /// </summary>
        /// <param name="email">User email</param>
        /// <param name="passwordHash">Hashed password</param>
        /// <returns>User if valid, null otherwise</returns>
        Task<User> ValidateCredentialsAsync(string email, string passwordHash);

        /// <summary>
        /// Finds a user by their reservation number
        /// </summary>
        /// <param name="email">User email</param>
        /// <param name="reservationNumber">Reservation number</param>
        /// <returns>User if found, null otherwise</returns>
        Task<User> FindByReservationAsync(string email, string reservationNumber);

        /// <summary>
        /// Checks if an email is already registered
        /// </summary>
        /// <param name="email">Email to check</param>
        /// <returns>True if email exists</returns>
        Task<bool> EmailExistsAsync(string email);

        /// <summary>
        /// Checks if a username is already taken
        /// </summary>
        /// <param name="username">Username to check</param>
        /// <returns>True if username exists</returns>
        Task<bool> UsernameExistsAsync(string username);
    }
}