using FluentValidation;
using ChabbyNb_API.Models.DTOs;
using System;

namespace ChabbyNb_API.Validation
{
    public class ApartmentCreateDtoValidator : AbstractValidator<ApartmentCreateDto>
    {
        public ApartmentCreateDtoValidator()
        {
            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required")
                .MaximumLength(100).WithMessage("Title cannot be longer than 100 characters");

            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Description is required");

            RuleFor(x => x.Address)
                .NotEmpty().WithMessage("Address is required")
                .MaximumLength(200).WithMessage("Address cannot be longer than 200 characters");

            RuleFor(x => x.PricePerNight)
                .GreaterThan(0).WithMessage("Price per night must be greater than 0")
                .LessThanOrEqualTo(10000).WithMessage("Price per night cannot exceed 10,000");

            RuleFor(x => x.Bedrooms)
                .GreaterThanOrEqualTo(0).WithMessage("Bedrooms cannot be negative")
                .LessThanOrEqualTo(10).WithMessage("Bedrooms cannot exceed 10");

            RuleFor(x => x.Bathrooms)
                .GreaterThanOrEqualTo(0).WithMessage("Bathrooms cannot be negative")
                .LessThanOrEqualTo(10).WithMessage("Bathrooms cannot exceed 10");

            RuleFor(x => x.MaxOccupancy)
                .GreaterThanOrEqualTo(1).WithMessage("Maximum occupancy must be at least 1")
                .LessThanOrEqualTo(30).WithMessage("Maximum occupancy cannot exceed 30");
        }
    }

    public class ApartmentUpdateDtoValidator : AbstractValidator<ApartmentUpdateDto>
    {
        public ApartmentUpdateDtoValidator()
        {
            RuleFor(x => x.ApartmentID).GreaterThan(0);

            RuleFor(x => x.Title)
                .NotEmpty().WithMessage("Title is required")
                .MaximumLength(100).WithMessage("Title cannot be longer than 100 characters");

            RuleFor(x => x.Description)
                .NotEmpty().WithMessage("Description is required");

            RuleFor(x => x.Address)
                .NotEmpty().WithMessage("Address is required")
                .MaximumLength(200).WithMessage("Address cannot be longer than 200 characters");

            RuleFor(x => x.PricePerNight)
                .GreaterThan(0).WithMessage("Price per night must be greater than 0")
                .LessThanOrEqualTo(10000).WithMessage("Price per night cannot exceed 10,000");

            RuleFor(x => x.Bedrooms)
                .GreaterThanOrEqualTo(0).WithMessage("Bedrooms cannot be negative")
                .LessThanOrEqualTo(10).WithMessage("Bedrooms cannot exceed 10");

            RuleFor(x => x.Bathrooms)
                .GreaterThanOrEqualTo(0).WithMessage("Bathrooms cannot be negative")
                .LessThanOrEqualTo(10).WithMessage("Bathrooms cannot exceed 10");

            RuleFor(x => x.MaxOccupancy)
                .GreaterThanOrEqualTo(1).WithMessage("Maximum occupancy must be at least 1")
                .LessThanOrEqualTo(30).WithMessage("Maximum occupancy cannot exceed 30");
        }
    }

    public class AmenityCreateDtoValidator : AbstractValidator<AmenityCreateDto>
    {
        public AmenityCreateDtoValidator()
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Name is required")
                .MaximumLength(100).WithMessage("Name cannot be longer than 100 characters");

            RuleFor(x => x.IconFile)
                .NotNull().WithMessage("Icon file is required");

            RuleFor(x => x.Category)
                .MaximumLength(50).WithMessage("Category cannot be longer than 50 characters");
        }
    }

    public class BookingCreateDtoValidator : AbstractValidator<BookingCreateDto>
    {
        public BookingCreateDtoValidator()
        {
            RuleFor(x => x.ApartmentID).GreaterThan(0);

            RuleFor(x => x.CheckInDate)
                .NotEmpty().WithMessage("Check-in date is required")
                .GreaterThanOrEqualTo(DateTime.Today).WithMessage("Check-in date must be today or in the future");

            RuleFor(x => x.CheckOutDate)
                .NotEmpty().WithMessage("Check-out date is required")
                .GreaterThan(x => x.CheckInDate).WithMessage("Check-out date must be after check-in date");

            RuleFor(x => x.GuestCount)
                .GreaterThanOrEqualTo(1).WithMessage("Guest count must be at least 1")
                .LessThanOrEqualTo(20).WithMessage("Guest count cannot exceed 20");

            RuleFor(x => x.PetCount)
                .GreaterThanOrEqualTo(0).WithMessage("Pet count cannot be negative")
                .LessThanOrEqualTo(5).WithMessage("Pet count cannot exceed 5");

            RuleFor(x => x.SpecialRequests)
                .MaximumLength(500).WithMessage("Special requests cannot be longer than 500 characters");
        }
    }

    public class LoginDtoValidator : AbstractValidator<LoginDto>
    {
        public LoginDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("Invalid email format");

            RuleFor(x => x)
                .Must(x => !string.IsNullOrEmpty(x.Password) || !string.IsNullOrEmpty(x.ReservationNumber))
                .WithMessage("Either password or reservation number is required");
        }
    }

    public class RegisterDtoValidator : AbstractValidator<RegisterDto>
    {
        public RegisterDtoValidator()
        {
            RuleFor(x => x.Email)
                .NotEmpty().WithMessage("Email is required")
                .EmailAddress().WithMessage("Invalid email format");

            RuleFor(x => x.Password)
                .NotEmpty().WithMessage("Password is required")
                .MinimumLength(6).WithMessage("Password must be at least 6 characters long");

            RuleFor(x => x.ConfirmPassword)
                .Equal(x => x.Password).WithMessage("Passwords do not match");

            RuleFor(x => x.Username)
                .MaximumLength(50).WithMessage("Username cannot be longer than 50 characters");

            RuleFor(x => x.FirstName)
                .MaximumLength(50).WithMessage("First name cannot be longer than 50 characters");

            RuleFor(x => x.LastName)
                .MaximumLength(50).WithMessage("Last name cannot be longer than 50 characters");

            RuleFor(x => x.PhoneNumber)
                .MaximumLength(20).WithMessage("Phone number cannot be longer than 20 characters")
                .Matches(@"^[0-9\+\-\(\) ]+$").WithMessage("Invalid phone number format");

            RuleFor(x => x.AgreeToTerms)
                .Equal(true).WithMessage("You must agree to the terms and conditions");
        }
    }

    public class ReviewDtoValidator : AbstractValidator<ReviewDto>
    {
        public ReviewDtoValidator()
        {
            RuleFor(x => x.BookingID).GreaterThan(0);
            RuleFor(x => x.ApartmentID).GreaterThan(0);

            RuleFor(x => x.Rating)
                .InclusiveBetween(1, 5).WithMessage("Rating must be between 1 and 5");

            RuleFor(x => x.Comment)
                .NotEmpty().WithMessage("Comment is required");
        }
    }
}