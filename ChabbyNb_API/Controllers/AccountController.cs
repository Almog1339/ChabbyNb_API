using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System.Security.Cryptography;
using System.Text;
using System.Net.Mail;
using System.Net;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Models.DTOs;
using ChabbyNb_API.Services;

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly JwtTokenService _jwtTokenService;

        public AccountController(
            ChabbyNbDbContext context,
            IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
            _jwtTokenService = new JwtTokenService(configuration);
        }

        // POST: api/Account/Login
        [HttpPost("Login")]
        public async Task<ActionResult<LoginResultDto>> Login([FromBody] LoginDto model)
        {
            // Custom validation to ensure either password or reservation number is provided
            if (!model.IsValid())
            {
                return BadRequest(new { error = "Either Password or Reservation Number is required." });
            }

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            User user = null;

            // Check if user is trying to login with password
            if (!string.IsNullOrEmpty(model.Password))
            {
                // Hash the password for comparison
                string hashedPassword = HashPassword(model.Password);

                // Check if user exists and credentials are valid
                user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email && u.PasswordHash == hashedPassword);

                if (user != null && !user.IsEmailVerified)
                {
                    return BadRequest(new { error = "Your email address has not been verified. Please check your email for verification link." });
                }
            }
            // Check if user is trying to login with reservation number
            else if (!string.IsNullOrEmpty(model.ReservationNumber))
            {
                // Find booking with the given reservation number and email
                var booking = await _context.Bookings
                    .Include(b => b.User)
                    .FirstOrDefaultAsync(b =>
                        b.ReservationNumber == model.ReservationNumber &&
                        b.User.Email == model.Email);

                if (booking != null)
                {
                    user = booking.User;
                }
            }

            if (user != null)
            {
                // Generate JWT Token
                string token = _jwtTokenService.GenerateJwtToken(user);

                // For backward compatibility, still store some basic information in session
                if (model.RememberMe)
                {
                    HttpContext.Session.SetInt32("UserID", user.UserID);
                    HttpContext.Session.SetString("FirstName", user.FirstName ?? "");
                    HttpContext.Session.SetString("LastName", user.LastName ?? "");
                    HttpContext.Session.SetString("IsAdmin", user.IsAdmin.ToString());
                }

                return Ok(new LoginResultDto
                {
                    Success = true,
                    Token = token,
                    UserId = user.UserID,
                    Email = user.Email,
                    FirstName = user.FirstName,
                    LastName = user.LastName,
                    IsAdmin = user.IsAdmin
                });
            }
            else
            {
                return BadRequest(new { error = "Invalid login attempt. Please check your credentials." });
            }
        }

        // POST: api/Account/Register
        [HttpPost("Register")]
        public async Task<IActionResult> Register([FromBody] RegisterDto model)
        {
            if (ModelState.IsValid)
            {
                // Check if email already exists
                if (await _context.Users.AnyAsync(u => u.Email == model.Email))
                {
                    return BadRequest(new { error = "This email is already registered." });
                }

                // Create new user
                var user = new User
                {
                    Email = model.Email,
                    PasswordHash = HashPassword(model.Password),
                    FirstName = model.FirstName,
                    LastName = model.LastName,
                    PhoneNumber = model.PhoneNumber,
                    IsAdmin = false,
                    CreatedDate = DateTime.Now,
                    IsEmailVerified = false,
                    Username = model.Username ?? model.Email.Split('@')[0] // Default username from email
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Generate verification token and send email
                if (await SendVerificationEmailAsync(user))
                {
                    return Ok(new { success = true, message = "Registration successful! Please check your email to verify your account before logging in." });
                }
                else
                {
                    _context.Users.Remove(user);
                    await _context.SaveChangesAsync();
                    return StatusCode(500, new { error = "Failed to send verification email. Please try again later." });
                }
            }

            return BadRequest(ModelState);
        }

        // Method to send verification email
        private async Task<bool> SendVerificationEmailAsync(User user)
        {
            try
            {
                // Generate a random token
                string token = Guid.NewGuid().ToString();

                // Create verification record
                var verification = new EmailVerification
                {
                    UserID = user.UserID,
                    Email = user.Email,
                    VerificationToken = token,
                    ExpiryDate = DateTime.Now.AddDays(2), // Token valid for 2 days
                    IsVerified = false,
                    CreatedDate = DateTime.Now
                };

                _context.EmailVerifications.Add(verification);
                await _context.SaveChangesAsync();

                // Build verification link
                string verificationLink = $"{Request.Scheme}://{Request.Host}/api/Account/VerifyEmail/{token}";

                // Prepare email message
                string subject = "Verify Your ChabbyNb Account";
                string body = $@"
                    <html>
                    <head>
                        <style>
                            body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                            .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                            .header {{ background-color: #ff5a5f; padding: 20px; color: white; text-align: center; }}
                            .content {{ padding: 20px; }}
                            .button {{ display: inline-block; background-color: #ff5a5f; color: white; padding: 10px 20px; 
                                      text-decoration: none; border-radius: 5px; margin-top: 20px; }}
                            .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <div class='header'>
                                <h1>Welcome to ChabbyNb!</h1>
                            </div>
                            <div class='content'>
                                <p>Hello {user.FirstName},</p>
                                <p>Thank you for registering with ChabbyNb. To complete your registration and verify your email address, please click the button below:</p>
                                <p style='text-align: center;'>
                                    <a href='{verificationLink}' class='button'>Verify Email Address</a>
                                </p>
                                <p>This link will expire in 48 hours.</p>
                                <p>If you did not create an account, you can safely ignore this email.</p>
                                <p>Best regards,<br>The ChabbyNb Team</p>
                            </div>
                            <div class='footer'>
                                <p>© 2025 ChabbyNb. All rights reserved.</p>
                                <p>25 Adrianou St, Athens, Greece</p>
                            </div>
                        </div>
                    </body>
                    </html>";

                // Get SMTP settings from configuration
                var smtpSettings = _configuration.GetSection("SmtpSettings");

                // Check if we should send real emails
                if (!_configuration.GetValue<bool>("SendRealEmails", false))
                {
                    // For development, just log the email
                    Console.WriteLine($"Email would be sent to: {user.Email}");
                    Console.WriteLine($"Subject: {subject}");
                    Console.WriteLine($"Verification Link: {verificationLink}");
                    return true;
                }

                // Configure and send email
                using (var client = new SmtpClient())
                {
                    // Set up the SMTP client
                    client.Host = smtpSettings["Host"];
                    client.Port = int.Parse(smtpSettings["Port"] ?? "587");
                    client.EnableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    client.UseDefaultCredentials = false;

                    // Make sure credentials are correctly set
                    string username = smtpSettings["Username"];
                    string password = smtpSettings["Password"];

                    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                    {
                        throw new InvalidOperationException("SMTP username or password is not configured.");
                    }

                    client.Credentials = new NetworkCredential(username, password);

                    // Create the email message
                    using (var message = new MailMessage())
                    {
                        message.From = new MailAddress(smtpSettings["FromEmail"], "ChabbyNb");
                        message.Subject = subject;
                        message.Body = body;
                        message.IsBodyHtml = true;
                        message.To.Add(new MailAddress(user.Email));

                        try
                        {
                            await client.SendMailAsync(message);
                            Console.WriteLine($"Email sent successfully to {user.Email}");
                            return true;
                        }
                        catch (Exception ex)
                        {
                            Console.Error.WriteLine($"Failed to send email: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                            }
                            throw;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.Error.WriteLine("Error sending verification email: " + ex.Message);
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine("Inner exception: " + ex.InnerException.Message);
                }
                return false;
            }
        }

        // GET: api/Account/VerifyEmail/{token}
        [HttpGet("VerifyEmail/{token}")]
        public async Task<IActionResult> VerifyEmail(string token)
        {
            if (string.IsNullOrEmpty(token))
            {
                return BadRequest(new { error = "Invalid token" });
            }

            var verification = await _context.EmailVerifications
                .Include(ev => ev.User)
                .FirstOrDefaultAsync(ev => ev.VerificationToken == token && !ev.IsVerified && ev.ExpiryDate > DateTime.Now);

            if (verification != null)
            {
                // Update verification record
                verification.IsVerified = true;
                verification.VerifiedDate = DateTime.Now;

                // Update user record
                verification.User.IsEmailVerified = true;

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Your email has been successfully verified. You can now log in to your account." });
            }

            return BadRequest(new { error = "Invalid or expired verification link. Please request a new verification email." });
        }

        // POST: api/Account/ResendVerification
        [HttpPost("ResendVerification")]
        public async Task<IActionResult> ResendVerification([FromBody] ForgotPasswordDto model)
        {
            if (ModelState.IsValid)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email && !u.IsEmailVerified);

                if (user != null)
                {
                    // Send new verification email
                    if (await SendVerificationEmailAsync(user))
                    {
                        return Ok(new { success = true, message = "A new verification email has been sent. Please check your inbox." });
                    }
                    else
                    {
                        return StatusCode(500, new { error = "Failed to send verification email. Please try again later." });
                    }
                }
                else
                {
                    // Don't reveal that the email doesn't exist or is already verified
                    return Ok(new { success = true, message = "If your email is registered and not verified, you will receive a new verification email shortly." });
                }
            }

            return BadRequest(ModelState);
        }

        // POST: api/Account/Logout
        [HttpPost("Logout")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public IActionResult Logout()
        {
            // With JWT, we don't need to do anything server-side for logout
            // The client should discard the token

            // For backward compatibility, clear session
            HttpContext.Session.Clear();

            return Ok(new { success = true, message = "You have been logged out successfully." });
        }

        // POST: api/Account/ForgotPassword
        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);

                if (user != null)
                {
                    // Generate a random token
                    string token = Guid.NewGuid().ToString("N").Substring(0, 20);

                    // Check if there's an existing token for this user and delete it
                    var existingTokens = await _context.Tempwds
                        .Where(t => t.UserID == user.UserID && !t.IsUsed)
                        .ToListAsync();

                    if (existingTokens.Any())
                    {
                        _context.Tempwds.RemoveRange(existingTokens);
                        await _context.SaveChangesAsync();
                    }

                    // Create a new temporary password reset record
                    var tempwd = new Tempwd
                    {
                        UserID = user.UserID,
                        Token = token,
                        ExperationTime = DateTime.Now.AddHours(24), // Token valid for 24 hours
                        IsUsed = false
                    };

                    try
                    {
                        // Add to context and save
                        _context.Tempwds.Add(tempwd);
                        await _context.SaveChangesAsync();

                        // Build reset password link
                        string resetLink = $"{Request.Scheme}://{Request.Host}/reset-password?token={token}&email={Uri.EscapeDataString(user.Email)}";

                        // Prepare email message
                        string subject = "Reset Your ChabbyNb Password";
                        string body = $@"
                    <html>
                    <head>
                        <style>
                            body {{ font-family: Arial, sans-serif; line-height: 1.6; color: #333; }}
                            .container {{ max-width: 600px; margin: 0 auto; padding: 20px; }}
                            .header {{ background-color: #ff5a5f; padding: 20px; color: white; text-align: center; }}
                            .content {{ padding: 20px; }}
                            .button {{ display: inline-block; background-color: #ff5a5f; color: white; padding: 10px 20px; 
                                      text-decoration: none; border-radius: 5px; margin-top: 20px; }}
                            .footer {{ text-align: center; margin-top: 20px; font-size: 12px; color: #666; }}
                        </style>
                    </head>
                    <body>
                        <div class='container'>
                            <div class='header'>
                                <h1>ChabbyNb Password Reset</h1>
                            </div>
                            <div class='content'>
                                <p>Hello {user.FirstName ?? user.Username},</p>
                                <p>We received a request to reset your password. To complete the process, please click the button below:</p>
                                <p style='text-align: center;'>
                                    <a href='{resetLink}' class='button'>Reset Password</a>
                                </p>
                                <p>This link will expire in 24 hours.</p>
                                <p>If you did not request a password reset, you can safely ignore this email.</p>
                                <p>Best regards,<br>The ChabbyNb Team</p>
                            </div>
                            <div class='footer'>
                                <p>© 2025 ChabbyNb. All rights reserved.</p>
                                <p>25 Adrianou St, Athens, Greece</p>
                            </div>
                        </div>
                    </body>
                    </html>";

                        // Get SMTP settings from configuration
                        var smtpSettings = _configuration.GetSection("SmtpSettings");

                        // Check if we should send real emails
                        if (!_configuration.GetValue<bool>("SendRealEmails", false))
                        {
                            // For development, just log the email
                            Console.WriteLine($"Password reset email would be sent to: {user.Email}");
                            Console.WriteLine($"Subject: {subject}");
                            Console.WriteLine($"Reset Link: {resetLink}");
                            return Ok(new { success = true, message = "If your email is registered in our system, you will receive password reset instructions shortly." });
                        }

                        try
                        {
                            // Configure and send email
                            using (var client = new SmtpClient())
                            {
                                // Set up the SMTP client
                                client.Host = smtpSettings["Host"];
                                client.Port = int.Parse(smtpSettings["Port"] ?? "587");
                                client.EnableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                                client.DeliveryMethod = SmtpDeliveryMethod.Network;
                                client.UseDefaultCredentials = false;

                                // Make sure credentials are correctly set
                                string username = smtpSettings["Username"];
                                string password = smtpSettings["Password"];

                                if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
                                {
                                    throw new InvalidOperationException("SMTP username or password is not configured.");
                                }

                                client.Credentials = new NetworkCredential(username, password);

                                // Create the email message
                                using (var message = new MailMessage())
                                {
                                    message.From = new MailAddress(smtpSettings["FromEmail"], "ChabbyNb");
                                    message.Subject = subject;
                                    message.Body = body;
                                    message.IsBodyHtml = true;
                                    message.To.Add(new MailAddress(user.Email));

                                    await client.SendMailAsync(message);
                                    Console.WriteLine($"Email sent successfully to {user.Email}");
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log the exception but don't reveal it to the user
                            Console.Error.WriteLine($"Error sending password reset email: {ex.Message}");
                            if (ex.InnerException != null)
                            {
                                Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                            }
                        }
                    }
                    catch (DbUpdateException ex)
                    {
                        // Log the specific database error
                        Console.Error.WriteLine($"Database error saving reset token: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                        }

                        // Don't expose the error details to the user
                        return StatusCode(500, new { error = "An error occurred while processing your request." });
                    }
                }

                // Don't reveal that the user does not exist
                return Ok(new { success = true, message = "If your email is registered in our system, you will receive password reset instructions shortly." });
            }
            catch (Exception ex)
            {
                // Log the general exception
                Console.Error.WriteLine($"Error in ForgotPassword: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, new { error = "An error occurred while processing your request." });
            }
        }

        // POST: api/Account/ResetPassword
        [HttpPost("ResetPassword")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            try
            {
                // Find the user by email
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
                if (user == null)
                {
                    // Don't reveal that the user does not exist
                    return BadRequest(new { error = "Invalid or expired password reset token." });
                }

                // Find the reset token
                var tempwd = await _context.Tempwds.FirstOrDefaultAsync(t =>
                    t.Token == model.Token &&
                    t.UserID == user.UserID &&
                    !t.IsUsed &&
                    t.ExperationTime > DateTime.Now);

                if (tempwd == null)
                {
                    return BadRequest(new { error = "Invalid or expired password reset token." });
                }

                // Reset the password
                user.PasswordHash = HashPassword(model.Password);

                // Mark the token as used
                tempwd.IsUsed = true;

                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Your password has been reset successfully. You can now log in with your new password." });
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.Error.WriteLine($"Error in ResetPassword: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.Error.WriteLine($"Inner exception: {ex.InnerException.Message}");
                }
                return StatusCode(500, new { error = "An error occurred while processing your request." });
            }
        }

        // POST: api/Account/ChangePassword
        [HttpPost("ChangePassword")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            string hashedCurrentPassword = HashPassword(model.CurrentPassword);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId && u.PasswordHash == hashedCurrentPassword);

            if (user == null)
            {
                return BadRequest(new { error = "Current password is incorrect." });
            }

            // Update the password
            user.PasswordHash = HashPassword(model.NewPassword);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Your password has been changed successfully." });
        }

        // GET: api/Account/Profile
        [HttpGet("Profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<ActionResult<ProfileDto>> GetProfile()
        {
            int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);

            if (user == null)
            {
                return NotFound(new { error = "User not found" });
            }

            var profile = new ProfileDto
            {
                Username = user.Username,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                PhoneNumber = user.PhoneNumber
            };

            return profile;
        }

        // PUT: api/Account/Profile
        [HttpPut("Profile")]
        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto model)
        {
            if (ModelState.IsValid)
            {
                int userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier));
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserID == userId);

                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Check if the username is being changed and if it's already taken
                if (model.Username != user.Username && await _context.Users.AnyAsync(u => u.Username == model.Username))
                {
                    return BadRequest(new { error = "This username is already taken." });
                }

                // Update user information
                user.Username = model.Username;
                user.FirstName = model.FirstName;
                user.LastName = model.LastName;
                user.PhoneNumber = model.PhoneNumber;

                await _context.SaveChangesAsync();

                // Update session
                HttpContext.Session.SetString("Username", user.Username);

                // Generate a new token with updated user information
                string token = _jwtTokenService.GenerateJwtToken(user);

                return Ok(new
                {
                    success = true,
                    message = "Your profile has been updated successfully.",
                    token = token
                });
            }

            return BadRequest(ModelState);
        }

        // Helper method to hash passwords
        private string HashPassword(string password)
        {
            using (var sha256 = SHA256.Create())
            {
                var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
                var hash = BitConverter.ToString(hashedBytes).Replace("-", "").ToLower();
                return hash;
            }
        }
    }
}