using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ChabbyNb_API.Services
{
    public class SimpleMapper : IMapper
    {
        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (source == null)
                return default;

            var destination = Activator.CreateInstance<TDestination>();
            return Map(source, destination);
        }

        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (source == null)
                return destination;

            var sourceProperties = typeof(TSource).GetProperties();
            var destinationProperties = typeof(TDestination).GetProperties();

            foreach (var sourceProperty in sourceProperties)
            {
                var destinationProperty = destinationProperties
                    .FirstOrDefault(x => x.Name == sourceProperty.Name && x.CanWrite);

                if (destinationProperty != null && destinationProperty.PropertyType.IsAssignableFrom(sourceProperty.PropertyType))
                {
                    var value = sourceProperty.GetValue(source);
                    destinationProperty.SetValue(destination, value);
                }
            }

            return destination;
        }

        public IEnumerable<TDestination> MapCollection<TSource, TDestination>(IEnumerable<TSource> source)
        {
            if (source == null)
                return Enumerable.Empty<TDestination>();

            return source.Select(item => Map<TSource, TDestination>(item));
        }

        public IEnumerable<TDestination> MapCollection<TSource, TDestination>(IEnumerable<TSource> source, IEnumerable<TDestination> destination)
        {
            if (source == null)
                return destination;

            var sourceList = source.ToList();
            var destinationList = destination.ToList();

            for (int i = 0; i < sourceList.Count; i++)
            {
                if (i < destinationList.Count)
                {
                    Map(sourceList[i], destinationList[i]);
                }
                else
                {
                    destinationList.Add(Map<TSource, TDestination>(sourceList[i]));
                }
            }

            return destinationList;
        }
    }
}