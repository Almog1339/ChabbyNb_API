using ChabbyNb.Models;
using ChabbyNb_API.Models;
using Microsoft.EntityFrameworkCore;

namespace ChabbyNb_API.Data
{
    public class ChabbyNbDbContext : DbContext
    {
        public ChabbyNbDbContext(DbContextOptions<ChabbyNbDbContext> options)
            : base(options)
        {
        }
        public DbSet<AdminLog> AdminLogs { get; set; }
        public DbSet<Amenity> Amenities { get; set; }
        public DbSet<Apartment> Apartments { get; set; }
        public DbSet<ApartmentAmenity> ApartmentAmenities { get; set; }
        public DbSet<ApartmentImage> ApartmentImages { get; set; }
        public DbSet<Booking> Bookings { get; set; }
        public DbSet<Review> Reviews { get; set; }
        public DbSet<User> Users { get; set; }
        public DbSet<EmailVerification> EmailVerifications { get; set; }
        public DbSet<Tempwd> Tempwds { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Decimal properties with precision
            modelBuilder.Entity<Apartment>()
                .Property(a => a.PricePerNight)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<Apartment>()
                .Property(a => a.PetFee)
                .HasColumnType("decimal(10, 2)");

            modelBuilder.Entity<Apartment>()
                .Property(a => a.Latitude)
                .HasPrecision(9, 6);

            modelBuilder.Entity<Apartment>()
                .Property(a => a.Longitude)
                .HasPrecision(9, 6);

            // Also configure the decimal property in the Booking entity
            modelBuilder.Entity<Booking>()
                .Property(b => b.TotalPrice)
                .HasColumnType("decimal(10, 2)");


            // Configure Review relationships with NO CASCADE DELETE
            modelBuilder.Entity<Review>()
                .HasOne(r => r.Booking)
                .WithMany(b => b.Reviews)
                .HasForeignKey(r => r.BookingID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Review>()
                .HasOne(r => r.User)
                .WithMany(u => u.Reviews)
                .HasForeignKey(r => r.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Review>()
                .HasOne(r => r.Apartment)
                .WithMany(a => a.Reviews)
                .HasForeignKey(r => r.ApartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // Configure other relationships
            modelBuilder.Entity<Booking>()
                .HasOne(b => b.User)
                .WithMany(u => u.Bookings)
                .HasForeignKey(b => b.UserID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Booking>()
                .HasOne(b => b.Apartment)
                .WithMany(a => a.Bookings)
                .HasForeignKey(b => b.ApartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // Ensure ApartmentImage relationships are configured
            modelBuilder.Entity<ApartmentImage>()
                .HasOne(ai => ai.Apartment)
                .WithMany(a => a.ApartmentImages)
                .HasForeignKey(ai => ai.ApartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            // Ensure ApartmentAmenity relationships are configured
            modelBuilder.Entity<ApartmentAmenity>()
                .HasOne(aa => aa.Apartment)
                .WithMany(a => a.ApartmentAmenities)
                .HasForeignKey(aa => aa.ApartmentID)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ApartmentAmenity>()
                .HasOne(aa => aa.Amenity)
                .WithMany(a => a.ApartmentAmenities)
                .HasForeignKey(aa => aa.AmenityID)
                .OnDelete(DeleteBehavior.Restrict);

            // Inside ChabbyNbDbContext.OnModelCreating method
            modelBuilder.Entity<Tempwd>()
                .HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserID)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}