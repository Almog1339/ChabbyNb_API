using System;
using System.ComponentModel.DataAnnotations;

namespace ChabbyNb_API.Models
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
    public class AgeValidationAttribute : ValidationAttribute
    {
        private readonly int _minimumAge;

        public AgeValidationAttribute(int minimumAge)
        {
            _minimumAge = minimumAge;
        }

        protected override ValidationResult IsValid(object value, ValidationContext validationContext)
        {
            if (value == null)
            {
                return new ValidationResult("Birthday is required.");
            }

            if (!(value is DateTime dateOfBirth))
            {
                return new ValidationResult("Invalid date format.");
            }

            // Check if provided date is a future date
            if (dateOfBirth > DateTime.Today)
            {
                return new ValidationResult("Birthday cannot be a future date.");
            }

            // Calculate age
            int age = CalculateAge(dateOfBirth, DateTime.Today);

            if (age < _minimumAge)
            {
                return new ValidationResult($"You must be at least {_minimumAge} years old to register.");
            }

            return ValidationResult.Success;
        }

        private static int CalculateAge(DateTime dateOfBirth, DateTime currentDate)
        {
            int age = currentDate.Year - dateOfBirth.Year;

            // Check if the birthday has occurred this year
            if (currentDate.Month < dateOfBirth.Month || currentDate.Month == dateOfBirth.Month && currentDate.Day < dateOfBirth.Day)
            {
                age--;
            }

            return age;
        }
    }
}
