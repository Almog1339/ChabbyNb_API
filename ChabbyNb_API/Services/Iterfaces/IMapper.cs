using System;
using System.Collections.Generic;

namespace ChabbyNb_API.Services
{
    public interface IMapper
    {
        TDestination Map<TSource, TDestination>(TSource source);
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
        IEnumerable<TDestination> MapCollection<TSource, TDestination>(IEnumerable<TSource> source);
        IEnumerable<TDestination> MapCollection<TSource, TDestination>(IEnumerable<TSource> source, IEnumerable<TDestination> destination);
    }
}