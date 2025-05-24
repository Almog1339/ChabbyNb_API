using ChabbyNb_API.Data;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading.Tasks;

namespace ChabbyNb_API.Services.Core
{
    /// <summary>
    /// Base interface for all entity services providing common CRUD operations
    /// </summary>
    public interface IEntityService<TEntity, TDto, TCreateDto, TUpdateDto>
            where TEntity : class
            where TDto : class
            where TCreateDto : class
            where TUpdateDto : class
    {
        Task<TDto> GetByIdAsync(int id);
        Task<IEnumerable<TDto>> GetAllAsync();
        Task<IEnumerable<TDto>> GetPagedAsync(int page, int pageSize, Expression<Func<TEntity, bool>> filter = null);
        Task<TDto> CreateAsync(TCreateDto createDto);
        Task<TDto> UpdateAsync(int id, TUpdateDto updateDto);
        Task<bool> DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
        Task<int> CountAsync(Expression<Func<TEntity, bool>> filter = null);
    }

    public interface IEntityService<TEntity, TDto> : IEntityService<TEntity, TDto, TDto, TDto>
        where TEntity : class
        where TDto : class
    {
    }

    public interface ISearchableService<TDto> where TDto : class
    {
        Task<IEnumerable<TDto>> SearchAsync(string query);
    }

    public interface ICategorizedService<TDto> where TDto : class
    {
        Task<IEnumerable<string>> GetCategoriesAsync();
        Task<IEnumerable<TDto>> GetByCategoryAsync(string category);
    }

    public interface IBulkService<TEntity, TDto, TCreateDto>
        where TEntity : class
        where TDto : class
        where TCreateDto : class
    {
        Task<IEnumerable<TDto>> CreateBulkAsync(IEnumerable<TCreateDto> createDtos);
        Task<bool> DeleteBulkAsync(IEnumerable<int> ids);
    }
}