using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using ChabbyNb_API.Data;

namespace ChabbyNb_API.Services.Core
{
    /// <summary>
    /// Base implementation for entity services with common CRUD operations
    /// </summary>
    public abstract class BaseEntityService<TEntity, TDto, TCreateDto, TUpdateDto> :
        IEntityService<TEntity, TDto, TCreateDto, TUpdateDto>
        where TEntity : class
        where TDto : class
        where TCreateDto : class
        where TUpdateDto : class
    {
        protected readonly ChabbyNbDbContext _context;
        protected readonly IMapper _mapper;
        protected readonly ILogger _logger;
        protected readonly DbSet<TEntity> _dbSet;

        protected BaseEntityService(
            ChabbyNbDbContext context,
            IMapper mapper,
            ILogger logger)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _mapper = mapper ?? throw new ArgumentNullException(nameof(mapper));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _dbSet = context.Set<TEntity>();
        }

        public virtual async Task<TDto> GetByIdAsync(int id)
        {
            var entity = await GetEntityByIdAsync(id);
            return entity == null ? null : await MapToDto(entity);
        }

        public virtual async Task<IEnumerable<TDto>> GetAllAsync()
        {
            var query = GetBaseQuery();
            var entities = await query.ToListAsync();
            return await MapToDtos(entities);
        }

        public virtual async Task<IEnumerable<TDto>> GetPagedAsync(
            int page,
            int pageSize,
            Expression<Func<TEntity, bool>> filter = null)
        {
            var query = GetBaseQuery();

            if (filter != null)
                query = query.Where(filter);

            var entities = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return await MapToDtos(entities);
        }

        public virtual async Task<TDto> CreateAsync(TCreateDto createDto)
        {
            await ValidateCreateDto(createDto);

            var entity = await MapFromCreateDto(createDto);
            await BeforeCreate(entity, createDto);

            _dbSet.Add(entity);
            await _context.SaveChangesAsync();

            await AfterCreate(entity, createDto);

            return await MapToDto(entity);
        }

        public virtual async Task<TDto> UpdateAsync(int id, TUpdateDto updateDto)
        {
            var entity = await GetEntityByIdAsync(id);
            if (entity == null)
                return null;

            await ValidateUpdateDto(updateDto, entity);

            await MapFromUpdateDto(updateDto, entity);
            await BeforeUpdate(entity, updateDto);

            _context.Entry(entity).State = EntityState.Modified;
            await _context.SaveChangesAsync();

            await AfterUpdate(entity, updateDto);

            return await MapToDto(entity);
        }

        public virtual async Task<bool> DeleteAsync(int id)
        {
            var entity = await GetEntityByIdAsync(id);
            if (entity == null)
                return false;

            await BeforeDelete(entity);

            _dbSet.Remove(entity);
            await _context.SaveChangesAsync();

            await AfterDelete(entity);

            return true;
        }

        public virtual async Task<bool> ExistsAsync(int id)
        {
            return await _dbSet.FindAsync(id) != null;
        }

        public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>> filter = null)
        {
            var query = _dbSet.AsQueryable();

            if (filter != null)
                query = query.Where(filter);

            return await query.CountAsync();
        }

        // Abstract methods that must be implemented by derived classes
        protected abstract Task<TEntity> GetEntityByIdAsync(int id);
        protected abstract IQueryable<TEntity> GetBaseQuery();
        protected abstract Task<TDto> MapToDto(TEntity entity);
        protected abstract Task<IEnumerable<TDto>> MapToDtos(IEnumerable<TEntity> entities);
        protected abstract Task<TEntity> MapFromCreateDto(TCreateDto createDto);
        protected abstract Task MapFromUpdateDto(TUpdateDto updateDto, TEntity entity);

        // Virtual methods that can be overridden for custom logic
        protected virtual Task ValidateCreateDto(TCreateDto createDto) => Task.CompletedTask;
        protected virtual Task ValidateUpdateDto(TUpdateDto updateDto, TEntity entity) => Task.CompletedTask;
        protected virtual Task BeforeCreate(TEntity entity, TCreateDto createDto) => Task.CompletedTask;
        protected virtual Task AfterCreate(TEntity entity, TCreateDto createDto) => Task.CompletedTask;
        protected virtual Task BeforeUpdate(TEntity entity, TUpdateDto updateDto) => Task.CompletedTask;
        protected virtual Task AfterUpdate(TEntity entity, TUpdateDto updateDto) => Task.CompletedTask;
        protected virtual Task BeforeDelete(TEntity entity) => Task.CompletedTask;
        protected virtual Task AfterDelete(TEntity entity) => Task.CompletedTask;
    }

    /// <summary>
    /// Base service for entities with same DTO for create/update operations
    /// </summary>
    public abstract class BaseEntityService<TEntity, TDto> : BaseEntityService<TEntity, TDto, TDto, TDto>
        where TEntity : class
        where TDto : class
    {
        protected BaseEntityService(ChabbyNbDbContext context, IMapper mapper, ILogger logger)
            : base(context, mapper, logger)
        {
        }

        protected override async Task MapFromUpdateDto(TDto updateDto, TEntity entity)
        {
            await MapFromCreateDto(updateDto, entity);
        }

        protected virtual async Task MapFromCreateDto(TDto dto, TEntity entity)
        {
            // Default implementation using mapper
            _mapper.Map(dto, entity);
            await Task.CompletedTask;
        }
    }
}

// Core/ServiceResult.cs
namespace ChabbyNb_API.Services.Core
{
    /// <summary>
    /// Generic result wrapper for service operations
    /// </summary>
    public class ServiceResult<T>
    {
        public bool Success { get; set; }
        public T Data { get; set; }
        public string Message { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public static ServiceResult<T> SuccessResult(T data, string message = null)
        {
            return new ServiceResult<T>
            {
                Success = true,
                Data = data,
                Message = message
            };
        }

        public static ServiceResult<T> ErrorResult(string error)
        {
            return new ServiceResult<T>
            {
                Success = false,
                Errors = new List<string> { error }
            };
        }

        public static ServiceResult<T> ErrorResult(List<string> errors)
        {
            return new ServiceResult<T>
            {
                Success = false,
                Errors = errors
            };
        }
    }

    /// <summary>
    /// Non-generic result for operations that don't return data
    /// </summary>
    public class ServiceResult
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public List<string> Errors { get; set; } = new List<string>();

        public static ServiceResult SuccessResult(string message = null)
        {
            return new ServiceResult
            {
                Success = true,
                Message = message
            };
        }

        public static ServiceResult ErrorResult(string error)
        {
            return new ServiceResult
            {
                Success = false,
                Errors = new List<string> { error }
            };
        }

        public static ServiceResult ErrorResult(List<string> errors)
        {
            return new ServiceResult
            {
                Success = false,
                Errors = errors
            };
        }
    }
}