using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ChabbyNb_API.Models.DTOs
{
    #region Authentication and User Management DTOs

    public class LoginDto
    {
        [Required(ErrorMessage = "Email address is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; }

        [DataType(DataType.Password)]
        public string Password { get; set; }

        public string ReservationNumber { get; set; }

        public bool RememberMe { get; set; }

        // Custom validation method
        public bool IsValid()
        {
            return !string.IsNullOrEmpty(Password) || !string.IsNullOrEmpty(ReservationNumber);
        }
    }

    public class RegisterDto
    {
        [Required(ErrorMessage = "Username is required")]
        [StringLength(50, ErrorMessage = "Username cannot be longer than 50 characters")]
        public string Username { get; set; }

        [Required(ErrorMessage = "Email address is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; }

        [Required(ErrorMessage = "Password is required")]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }

        [StringLength(50, ErrorMessage = "First name cannot be longer than 50 characters")]
        public string FirstName { get; set; }

        [StringLength(50, ErrorMessage = "Last name cannot be longer than 50 characters")]
        public string LastName { get; set; }

        [StringLength(20, ErrorMessage = "Phone number cannot be longer than 20 characters")]
        [RegularExpression(@"^[0-9\+\-\(\) ]+$", ErrorMessage = "Invalid phone number format")]
        public string PhoneNumber { get; set; }

        [Range(typeof(bool), "true", "true", ErrorMessage = "You must agree to the terms and conditions")]
        public bool AgreeToTerms { get; set; }
    }

    public class ForgotPasswordDto
    {
        [Required(ErrorMessage = "Email address is required")]
        [EmailAddress(ErrorMessage = "Invalid email address")]
        public string Email { get; set; }
    }

    public class ResetPasswordDto
    {
        [Required]
        public string Token { get; set; }

        [Required]
        [EmailAddress]
        public string Email { get; set; }

        [Required]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string Password { get; set; }

        [DataType(DataType.Password)]
        [Compare("Password", ErrorMessage = "The password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }
    }

    public class ChangePasswordDto
    {
        [Required(ErrorMessage = "Current password is required")]
        [DataType(DataType.Password)]
        public string CurrentPassword { get; set; }

        [Required(ErrorMessage = "New password is required")]
        [StringLength(100, ErrorMessage = "The {0} must be at least {2} characters long.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        public string NewPassword { get; set; }

        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "The new password and confirmation password do not match.")]
        public string ConfirmPassword { get; set; }
    }

    public class ProfileDto
    {
        [Required(ErrorMessage = "Username is required")]
        [StringLength(50, ErrorMessage = "Username cannot be longer than 50 characters")]
        public string Username { get; set; }

        [EmailAddress(ErrorMessage = "Invalid email address")]
        [ReadOnly(true)]
        public string Email { get; set; }

        [StringLength(50, ErrorMessage = "First name cannot be longer than 50 characters")]
        public string FirstName { get; set; }

        [StringLength(50, ErrorMessage = "Last name cannot be longer than 50 characters")]
        public string LastName { get; set; }

        [StringLength(20, ErrorMessage = "Phone number cannot be longer than 20 characters")]
        [RegularExpression(@"^[0-9\+\-\(\) ]+$", ErrorMessage = "Invalid phone number format")]
        public string PhoneNumber { get; set; }
    }

    public class LoginResultDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string Token { get; set; }
        public string RefreshToken { get; set; }
        public DateTime TokenExpiration { get; set; }
        public int UserId { get; set; }
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool IsAdmin { get; set; }
        public List<string> Roles { get; set; } = new List<string>();
    }

    // DTO for refresh token requests
    public class RefreshTokenDto
    {
        [Required]
        public string AccessToken { get; set; }

        [Required]
        public string RefreshToken { get; set; }
    }

    public class LogoutDto
    {
        public string RefreshToken { get; set; }
    }

    // DTO for assigning roles
    public class AssignRoleDto
    {
        [Required]
        public string Role { get; set; }
    }

    // DTO for user information
    public class UserDto
    {
        public int UserId { get; set; }
        public string Username { get; set; }
        public string Email { get; set; }
        public string FirstName { get; set; }
        public string LastName { get; set; }
        public bool IsAdmin { get; set; }
    }

    #endregion

    #region Pet DTOs

    // DTO for pet profiles
    public class PetDto
    {
        public int PetID { get; set; }

        [Required(ErrorMessage = "Pet name is required")]
        [StringLength(50, ErrorMessage = "Pet name cannot be longer than 50 characters")]
        public string Name { get; set; }

        [Required(ErrorMessage = "Pet type is required")]
        [StringLength(30, ErrorMessage = "Pet type cannot be longer than 30 characters")]
        public string Type { get; set; } // Dog, Cat, etc.

        [StringLength(50, ErrorMessage = "Breed cannot be longer than 50 characters")]
        public string Breed { get; set; }

        [Range(0, 30, ErrorMessage = "Please enter a valid age between 0 and 30")]
        public int? Age { get; set; }

        [Range(0.1, 100, ErrorMessage = "Please enter a valid weight between 0.1 and 100 kg")]
        public decimal? Weight { get; set; }

        [StringLength(500, ErrorMessage = "Special needs cannot be longer than 500 characters")]
        public string SpecialNeeds { get; set; }

        [StringLength(1000, ErrorMessage = "Additional information cannot be longer than 1000 characters")]
        public string AdditionalInfo { get; set; }

        public IFormFile PetImage { get; set; }

        public string ImageUrl { get; set; }
    }

    #endregion

    #region Dashboard DTOs

    // DTO for member dashboard
    public class DashboardDto
    {
        public User User { get; set; }
        public ICollection<Booking> UpcomingBookings { get; set; }
        public ICollection<Booking> RecentBookings { get; set; }
    }

    #endregion

    #region Admin Communications

    public class AdminMessageDto
    {
        public string Subject { get; set; }
        public string Message { get; set; }
    }

    #endregion
}