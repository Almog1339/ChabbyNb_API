using System;
using System.Threading.Tasks;
using ChabbyNb_API.Data;
using ChabbyNb_API.Repositories.Interface;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace ChabbyNb_API.Repositories
{
    /// <summary>
    /// Implementation of the Unit of Work pattern
    /// </summary>
    public class UnitOfWork : IUnitOfWork
    {
        private readonly ChabbyNbDbContext _context;
        private IUserRepository _userRepository;
        private IRoleRepository _roleRepository;
        private IDbContextTransaction _transaction;
        private bool _disposed = false;

        public UnitOfWork(ChabbyNbDbContext context)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
        }

        public IUserRepository Users => _userRepository ??= new UserRepository(_context);

        public IRoleRepository Roles => _roleRepository ??= new RoleRepository(_context);

        public async Task<int> SaveChangesAsync()
        {
            return await _context.SaveChangesAsync();
        }

        public async Task BeginTransactionAsync()
        {
            if (_transaction != null)
            {
                throw new InvalidOperationException("A transaction is already in progress");
            }

            _transaction = await _context.Database.BeginTransactionAsync();
        }

        public async Task CommitTransactionAsync()
        {
            try
            {
                await _context.SaveChangesAsync();

                if (_transaction != null)
                {
                    await _transaction.CommitAsync();
                }
            }
            catch
            {
                await RollbackTransactionAsync();
                throw;
            }
            finally
            {
                if (_transaction != null)
                {
                    await _transaction.DisposeAsync();
                    _transaction = null;
                }
            }
        }

        public async Task RollbackTransactionAsync()
        {
            if (_transaction != null)
            {
                await _transaction.RollbackAsync();
                await _transaction.DisposeAsync();
                _transaction = null;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    if (_transaction != null)
                    {
                        _transaction.Dispose();
                        _transaction = null;
                    }
                    _context.Dispose();
                }

                _disposed = true;
            }
        }
    }
}