using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Repositories.Interface;
using Microsoft.EntityFrameworkCore;

namespace ChabbyNb_API.Repositories
{
    /// <summary>
    /// Implementation of the user repository
    /// </summary>
    public class UserRepository : Repository<User>, IUserRepository
    {
        public UserRepository(ChabbyNbDbContext context) : base(context)
        {
        }

        public async Task<User> GetByEmailAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
                return null;

            return await _dbSet.FirstOrDefaultAsync(u => u.Email == email);
        }

        public async Task<User> GetByUsernameAsync(string username)
        {
            if (string.IsNullOrEmpty(username))
                return null;

            return await _dbSet.FirstOrDefaultAsync(u => u.Username == username);
        }

        public async Task<User> GetWithBookingsAsync(int userId)
        {
            return await _dbSet
                .Include(u => u.Bookings)
                .FirstOrDefaultAsync(u => u.UserID == userId);
        }

        public async Task<User> GetWithReviewsAsync(int userId)
        {
            return await _dbSet
                .Include(u => u.Reviews)
                .FirstOrDefaultAsync(u => u.UserID == userId);
        }

        public async Task<User> ValidateCredentialsAsync(string email, string passwordHash)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(passwordHash))
                return null;

            return await _dbSet.FirstOrDefaultAsync(u =>
                u.Email == email &&
                u.PasswordHash == passwordHash);
        }

        public async Task<User> FindByReservationAsync(string email, string reservationNumber)
        {
            if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(reservationNumber))
                return null;

            return await _context.Bookings
                .Include(b => b.User)
                .Where(b =>
                    b.ReservationNumber == reservationNumber &&
                    b.User.Email == email)
                .Select(b => b.User)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> EmailExistsAsync(string email)
        {
            if (string.IsNullOrEmpty(email))
                return false;

            return await _dbSet.AnyAsync(u => u.Email == email);
        }

        public async Task<bool> UsernameExistsAsync(string username)
        {
            if (string.IsNullOrEmpty(username))
                return false;

            return await _dbSet.AnyAsync(u => u.Username == username);
        }
    }
}