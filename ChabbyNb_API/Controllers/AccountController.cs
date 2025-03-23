using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
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

namespace ChabbyNb_API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AccountController : ControllerBase
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;

        public AccountController(ChabbyNbDbContext context, IConfiguration configuration)
        {
            _context = context;
            _configuration = configuration;
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
                // Create authentication using ASP.NET Core Identity
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.Name, user.Email),
                    new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                    new Claim("IsAdmin", user.IsAdmin.ToString())
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var authProperties = new AuthenticationProperties
                {
                    IsPersistent = model.RememberMe,
                    ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
                };

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    new ClaimsPrincipal(claimsIdentity),
                    authProperties);

                // Store user data in session
                HttpContext.Session.SetInt32("UserID", user.UserID);
                HttpContext.Session.SetString("FirstName", user.FirstName ?? "");
                HttpContext.Session.SetString("LastName", user.LastName ?? "");
                HttpContext.Session.SetString("IsAdmin", user.IsAdmin.ToString());

                return Ok(new LoginResultDto
                {
                    Success = true,
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
                if (await _context.Users.AnyAsync(u => u.Email == model.Email) || await _context.Users.AnyAsync(u => u.PhoneNumber == model.PhoneNumber))
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
                    Username = model.Email.Split('@')[0] // Default username from email
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

                // Configure and send email
                using (var client = new SmtpClient())
                {
                    client.Host = smtpSettings["Host"] ?? "smtp.example.com";
                    client.Port = int.Parse(smtpSettings["Port"] ?? "587");
                    client.EnableSsl = bool.Parse(smtpSettings["EnableSsl"] ?? "true");
                    client.DeliveryMethod = SmtpDeliveryMethod.Network;
                    client.UseDefaultCredentials = false;
                    client.Credentials = new NetworkCredential(
                        smtpSettings["Username"] ?? "noreply@chabbnb.com",
                        smtpSettings["Password"] ?? "password");

                    using (var message = new MailMessage())
                    {
                        message.From = new MailAddress(smtpSettings["FromEmail"] ?? "noreply@chabbnb.com", "ChabbyNb");
                        message.Subject = subject;
                        message.Body = body;
                        message.IsBodyHtml = true;
                        message.To.Add(new MailAddress(user.Email));

                        // In a development environment, we'll log instead of sending
                        if (_configuration.GetValue<bool>("SendRealEmails", true))
                        {
                            
                        }
                        else
                        {
                            await client.SendMailAsync(message);
                            // For development, just log the email
                            Console.WriteLine($"Email would be sent to: {user.Email}");
                            Console.WriteLine($"Subject: {subject}");
                            Console.WriteLine($"Verification Link: {verificationLink}");
                        }
                        _context.EmailVerifications.Add(verification);
                        await _context.SaveChangesAsync();
                        return true;
                    }
                }
            }
            catch (Exception ex)
            {
                // Log the exception
                Console.Error.WriteLine("Error sending verification email: " + ex.Message);
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
        [Authorize]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Clear session
            HttpContext.Session.Clear();

            return Ok(new { success = true, message = "You have been logged out successfully." });
        }

        // POST: api/Account/ForgotPassword
        [HttpPost("ForgotPassword")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordDto model)
        {
            if (ModelState.IsValid)
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);

                if (user != null)
                {
                    // In a real application, you would:
                    // 1. Generate a password reset token
                    // 2. Store it with an expiration time
                    // 3. Send an email with a reset link

                    // For this demo, we'll just show a success message
                    return Ok(new { success = true, message = "If your email is registered in our system, you will receive password reset instructions shortly." });
                }

                // Don't reveal that the user does not exist
                return Ok(new { success = true, message = "If your email is registered in our system, you will receive password reset instructions shortly." });
            }

            return BadRequest(ModelState);
        }

        // POST: api/Account/ChangePassword
        [HttpPost("ChangePassword")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            string userEmail = User.Identity.Name;
            string hashedCurrentPassword = HashPassword(model.CurrentPassword);

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail && u.PasswordHash == hashedCurrentPassword);

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
        [Authorize]
        public async Task<ActionResult<ProfileDto>> GetProfile()
        {
            string userEmail = User.Identity.Name;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);

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
        [Authorize]
        public async Task<IActionResult> UpdateProfile([FromBody] ProfileDto model)
        {
            if (ModelState.IsValid)
            {
                string userEmail = User.Identity.Name;
                var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);

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

                return Ok(new { success = true, message = "Your profile has been updated successfully." });
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