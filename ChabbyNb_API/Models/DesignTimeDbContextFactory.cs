using ChabbyNb_API.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using System.IO;

namespace ChabbyNb_API.Models
{
    public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<ChabbyNbDbContext>
    {
        public ChabbyNbDbContext CreateDbContext(string[] args)
        {
            // Build configuration
            IConfigurationRoot configuration = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile("appsettings.json")
                .Build();

            // Build options
            var builder = new DbContextOptionsBuilder<ChabbyNbDbContext>();
            var connectionString = configuration.GetConnectionString("DefaultConnection");

            builder.UseSqlServer(connectionString);

            return new ChabbyNbDbContext(builder.Options);
        }
    }
}