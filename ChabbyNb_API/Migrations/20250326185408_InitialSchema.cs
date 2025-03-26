using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChabbyNb_API.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Check if AdminLogs table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'AdminLogs' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [AdminLogs] (
                        [LogID] int NOT NULL IDENTITY,
                        [AdminID] int NOT NULL,
                        [AdminEmail] nvarchar(max) NOT NULL,
                        [Action] nvarchar(max) NOT NULL,
                        [Timestamp] datetime2 NOT NULL,
                        [IPAddress] nvarchar(max) NOT NULL,
                        CONSTRAINT [PK_AdminLogs] PRIMARY KEY ([LogID])
                    );
                END");

            // Check if Amenities table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Amenities' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [Amenities] (
                        [AmenityID] int NOT NULL IDENTITY,
                        [Name] nvarchar(100) NOT NULL,
                        [Icon] varbinary(max) NULL,
                        [IconContentType] nvarchar(50) NULL,
                        [Category] nvarchar(50) NULL,
                        CONSTRAINT [PK_Amenities] PRIMARY KEY ([AmenityID])
                    );
                END");

            // Check if Apartments table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Apartments' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [Apartments] (
                        [ApartmentID] int NOT NULL IDENTITY,
                        [Title] nvarchar(100) NOT NULL,
                        [Description] nvarchar(max) NOT NULL,
                        [Address] nvarchar(200) NOT NULL,
                        [Neighborhood] nvarchar(100) NULL,
                        [PricePerNight] decimal(10,2) NOT NULL,
                        [Bedrooms] int NOT NULL,
                        [Bathrooms] int NOT NULL,
                        [MaxOccupancy] int NOT NULL,
                        [SquareMeters] int NULL,
                        [PetFriendly] bit NOT NULL,
                        [PetFee] decimal(10,2) NULL,
                        [Latitude] decimal(9,6) NULL,
                        [Longitude] decimal(9,6) NULL,
                        [CreatedDate] datetime2 NOT NULL,
                        [IsActive] bit NOT NULL,
                        CONSTRAINT [PK_Apartments] PRIMARY KEY ([ApartmentID])
                    );
                END
                ELSE
                BEGIN
                    -- Update precision of decimal columns if table exists
                    IF EXISTS (SELECT * FROM sys.columns WHERE name = 'PricePerNight' AND object_id = OBJECT_ID('[Apartments]'))
                    BEGIN
                        ALTER TABLE [Apartments] ALTER COLUMN [PricePerNight] decimal(10,2) NOT NULL;
                    END
                    
                    IF EXISTS (SELECT * FROM sys.columns WHERE name = 'PetFee' AND object_id = OBJECT_ID('[Apartments]'))
                    BEGIN
                        ALTER TABLE [Apartments] ALTER COLUMN [PetFee] decimal(10,2) NULL;
                    END
                    
                    IF EXISTS (SELECT * FROM sys.columns WHERE name = 'Latitude' AND object_id = OBJECT_ID('[Apartments]'))
                    BEGIN
                        ALTER TABLE [Apartments] ALTER COLUMN [Latitude] decimal(9,6) NULL;
                    END
                    
                    IF EXISTS (SELECT * FROM sys.columns WHERE name = 'Longitude' AND object_id = OBJECT_ID('[Apartments]'))
                    BEGIN
                        ALTER TABLE [Apartments] ALTER COLUMN [Longitude] decimal(9,6) NULL;
                    END
                END");

            // Check if Users table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Users' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [Users] (
                        [UserID] int NOT NULL IDENTITY,
                        [Username] nvarchar(50) NOT NULL,
                        [Email] nvarchar(100) NOT NULL,
                        [PasswordHash] nvarchar(128) NOT NULL,
                        [FirstName] nvarchar(50) NULL,
                        [LastName] nvarchar(50) NULL,
                        [PhoneNumber] nvarchar(20) NULL,
                        [IsAdmin] bit NOT NULL,
                        [CreatedDate] datetime2 NOT NULL,
                        [IsEmailVerified] bit NOT NULL,
                        CONSTRAINT [PK_Users] PRIMARY KEY ([UserID])
                    );
                END");

            // Check if ApartmentAmenities table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApartmentAmenities' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [ApartmentAmenities] (
                        [ApartmentAmenityID] int NOT NULL IDENTITY,
                        [ApartmentID] int NOT NULL,
                        [AmenityID] int NOT NULL,
                        CONSTRAINT [PK_ApartmentAmenities] PRIMARY KEY ([ApartmentAmenityID]),
                        CONSTRAINT [FK_ApartmentAmenities_Amenities_AmenityID] FOREIGN KEY ([AmenityID]) REFERENCES [Amenities] ([AmenityID]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_ApartmentAmenities_Apartments_ApartmentID] FOREIGN KEY ([ApartmentID]) REFERENCES [Apartments] ([ApartmentID]) ON DELETE NO ACTION
                    );
                    
                    CREATE INDEX [IX_ApartmentAmenities_AmenityID] ON [ApartmentAmenities] ([AmenityID]);
                    CREATE INDEX [IX_ApartmentAmenities_ApartmentID] ON [ApartmentAmenities] ([ApartmentID]);
                END");

            // Check if ApartmentImages table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ApartmentImages' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [ApartmentImages] (
                        [ImageID] int NOT NULL IDENTITY,
                        [ApartmentID] int NOT NULL,
                        [ImageUrl] nvarchar(255) NOT NULL,
                        [IsPrimary] bit NOT NULL,
                        [Caption] nvarchar(200) NULL,
                        [SortOrder] int NOT NULL,
                        CONSTRAINT [PK_ApartmentImages] PRIMARY KEY ([ImageID]),
                        CONSTRAINT [FK_ApartmentImages_Apartments_ApartmentID] FOREIGN KEY ([ApartmentID]) REFERENCES [Apartments] ([ApartmentID]) ON DELETE NO ACTION
                    );
                    
                    CREATE INDEX [IX_ApartmentImages_ApartmentID] ON [ApartmentImages] ([ApartmentID]);
                END");

            // Check if Bookings table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Bookings' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [Bookings] (
                        [BookingID] int NOT NULL IDENTITY,
                        [UserID] int NOT NULL,
                        [ApartmentID] int NOT NULL,
                        [CheckInDate] datetime2 NOT NULL,
                        [CheckOutDate] datetime2 NOT NULL,
                        [GuestCount] int NOT NULL,
                        [PetCount] int NOT NULL,
                        [TotalPrice] decimal(10,2) NOT NULL,
                        [BookingStatus] nvarchar(20) NOT NULL,
                        [PaymentStatus] nvarchar(20) NOT NULL,
                        [SpecialRequests] nvarchar(max) NULL,
                        [ReservationNumber] nvarchar(20) NOT NULL,
                        [CreatedDate] datetime2 NOT NULL,
                        CONSTRAINT [PK_Bookings] PRIMARY KEY ([BookingID]),
                        CONSTRAINT [FK_Bookings_Apartments_ApartmentID] FOREIGN KEY ([ApartmentID]) REFERENCES [Apartments] ([ApartmentID]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_Bookings_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
                    );
                    
                    CREATE INDEX [IX_Bookings_ApartmentID] ON [Bookings] ([ApartmentID]);
                    CREATE INDEX [IX_Bookings_UserID] ON [Bookings] ([UserID]);
                END
                ELSE
                BEGIN
                    -- Update TotalPrice precision if table exists
                    IF EXISTS (SELECT * FROM sys.columns WHERE name = 'TotalPrice' AND object_id = OBJECT_ID('[Bookings]'))
                    BEGIN
                        ALTER TABLE [Bookings] ALTER COLUMN [TotalPrice] decimal(10,2) NOT NULL;
                    END
                END");

            // Check if EmailVerifications table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'EmailVerifications' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [EmailVerifications] (
                        [VerificationID] int NOT NULL IDENTITY,
                        [UserID] int NOT NULL,
                        [Email] nvarchar(100) NOT NULL,
                        [VerificationToken] nvarchar(128) NOT NULL,
                        [ExpiryDate] datetime2 NOT NULL,
                        [VerifiedDate] datetime2 NULL,
                        [IsVerified] bit NOT NULL,
                        [CreatedDate] datetime2 NOT NULL,
                        CONSTRAINT [PK_EmailVerifications] PRIMARY KEY ([VerificationID]),
                        CONSTRAINT [FK_EmailVerifications_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE CASCADE
                    );
                    
                    CREATE INDEX [IX_EmailVerifications_UserID] ON [EmailVerifications] ([UserID]);
                END");

            // Check if Tempwds table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Tempwds' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [Tempwds] (
                        [TempwdID] int NOT NULL IDENTITY,
                        [UserID] int NOT NULL,
                        [ExperationTime] datetime2 NOT NULL,
                        [Token] nvarchar(50) NOT NULL,
                        [IsUsed] bit NOT NULL,
                        CONSTRAINT [PK_Tempwds] PRIMARY KEY ([TempwdID]),
                        CONSTRAINT [FK_Tempwds_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
                    );
                    
                    CREATE INDEX [IX_Tempwds_UserID] ON [Tempwds] ([UserID]);
                END");

            // Check if Reviews table exists before creating
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'Reviews' AND schema_id = SCHEMA_ID('dbo'))
                BEGIN
                    CREATE TABLE [Reviews] (
                        [ReviewID] int NOT NULL IDENTITY,
                        [BookingID] int NOT NULL,
                        [UserID] int NOT NULL,
                        [ApartmentID] int NOT NULL,
                        [Rating] int NOT NULL,
                        [Comment] nvarchar(max) NULL,
                        [CreatedDate] datetime2 NOT NULL,
                        CONSTRAINT [PK_Reviews] PRIMARY KEY ([ReviewID]),
                        CONSTRAINT [FK_Reviews_Apartments_ApartmentID] FOREIGN KEY ([ApartmentID]) REFERENCES [Apartments] ([ApartmentID]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_Reviews_Bookings_BookingID] FOREIGN KEY ([BookingID]) REFERENCES [Bookings] ([BookingID]) ON DELETE NO ACTION,
                        CONSTRAINT [FK_Reviews_Users_UserID] FOREIGN KEY ([UserID]) REFERENCES [Users] ([UserID]) ON DELETE NO ACTION
                    );
                    
                    CREATE INDEX [IX_Reviews_ApartmentID] ON [Reviews] ([ApartmentID]);
                    CREATE INDEX [IX_Reviews_BookingID] ON [Reviews] ([BookingID]);
                    CREATE INDEX [IX_Reviews_UserID] ON [Reviews] ([UserID]);
                END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // No need to include drop statements in the Down method since we want to preserve the data
            // and we're using IF NOT EXISTS checks in the Up method.
            // If you run a migration rollback, it won't drop the tables, which is what we want.

            // Instead, log a message
            migrationBuilder.Sql("PRINT 'Rollback requested but tables are preserved to maintain data integrity.'");
        }
    }
}