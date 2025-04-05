using System;
using System.Threading.Tasks;
using ChabbyNb_API.Repositories.Interface;

namespace ChabbyNb_API.Repositories
{
    /// <summary>
    /// Unit of Work interface that coordinates the work of multiple repositories
    /// </summary>
    public interface IUnitOfWork : IDisposable
    {
        /// <summary>
        /// Repository for user operations
        /// </summary>
        IUserRepository Users { get; }

        /// <summary>
        /// Repository for role operations
        /// </summary>
        IRoleRepository Roles { get; }

        /// <summary>
        /// Saves all changes made in this unit of work to the database
        /// </summary>
        /// <returns>Number of affected rows</returns>
        Task<int> SaveChangesAsync();

        /// <summary>
        /// Begins a transaction
        /// </summary>
        Task BeginTransactionAsync();

        /// <summary>
        /// Commits all changes made in the current transaction
        /// </summary>
        Task CommitTransactionAsync();

        /// <summary>
        /// Rolls back all changes made in the current transaction
        /// </summary>
        Task RollbackTransactionAsync();
    }
}