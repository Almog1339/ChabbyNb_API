using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using ChabbyNb_API.Data;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Iterfaces;

namespace ChabbyNb_API.Services.Auth
{
    /// <summary>
    /// Implementation of the ITokenService for JWT and refresh token management
    /// </summary>
    public class TokenService : ITokenService
    {
        private readonly ChabbyNbDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<TokenService> _logger;
        private readonly IHttpContextAccessor _httpContextAccessor;

        public TokenService(
            ChabbyNbDbContext context,
            IConfiguration configuration,
            ILogger<TokenService> logger,
            IHttpContextAccessor httpContextAccessor)
        {
            _context = context ?? throw new ArgumentNullException(nameof(context));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        /// <summary>
        /// Generates both access token and refresh token for a user
        /// </summary>
        public async Task<TokenResult> GenerateTokensAsync(User user, IEnumerable<string> roles)
        {
            if (user == null)
            {
                throw new ArgumentNullException(nameof(user));
            }

            // Get settings from configuration
            var jwtKey = _configuration["Jwt:Key"];
            var jwtIssuer = _configuration["Jwt:Issuer"];
            var jwtAudience = _configuration["Jwt:Audience"];
            var accessTokenExpiryMinutes = _configuration.GetValue<int>("Jwt:AccessTokenExpiryInMinutes", 60);
            var refreshTokenExpiryDays = _configuration.GetValue<int>("Jwt:RefreshTokenExpiryInDays", 7);

            if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience))
            {
                throw new InvalidOperationException("JWT configuration is incomplete. Please check your appsettings.json file.");
            }

            var accessTokenExpiration = DateTime.UtcNow.AddMinutes(accessTokenExpiryMinutes);
            var refreshTokenExpiration = DateTime.UtcNow.AddDays(refreshTokenExpiryDays);

            // Create claims for the token
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Username ?? user.Email),
                new Claim("IsAdmin", user.IsAdmin.ToString())
            };

            // Add role claims
            foreach (var role in roles)
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            // Add name claims if available
            if (!string.IsNullOrEmpty(user.FirstName))
                claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));

            if (!string.IsNullOrEmpty(user.LastName))
                claims.Add(new Claim(ClaimTypes.Surname, user.LastName));

            // Add a unique token ID (jti) to prevent token reuse after invalidation
            var tokenId = Guid.NewGuid().ToString();
            claims.Add(new Claim(JwtRegisteredClaimNames.Jti, tokenId));

            // Create the JWT security token
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var jwtToken = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: accessTokenExpiration,
                signingCredentials: creds
            );

            // Generate the access token
            var accessToken = new JwtSecurityTokenHandler().WriteToken(jwtToken);

            // Generate refresh token
            var refreshToken = GenerateRefreshToken();

            // Save the refresh token to the database
            var tokenEntity = new RefreshToken
            {
                Token = refreshToken,
                JwtId = tokenId,
                UserId = user.UserID,
                IssuedAt = DateTime.UtcNow,
                ExpiresAt = refreshTokenExpiration,
                IsRevoked = false,
                CreatedByIp = GetClientIpAddress()
            };

            _context.RefreshTokens.Add(tokenEntity);
            await _context.SaveChangesAsync();

            // Track user login for security monitoring
            await TrackUserLogin(user.UserID, GetClientIpAddress(), tokenEntity.Token);

            return new TokenResult
            {
                AccessToken = accessToken,
                RefreshToken = refreshToken,
                AccessTokenExpiration = accessTokenExpiration,
                RefreshTokenExpiration = refreshTokenExpiration
            };
        }

        /// <summary>
        /// Refreshes an access token using a refresh token
        /// </summary>
        public async Task<TokenResult> RefreshTokenAsync(string refreshToken, string accessToken)
        {
            if (string.IsNullOrEmpty(refreshToken) || string.IsNullOrEmpty(accessToken))
            {
                throw new ArgumentException("Refresh token and access token are required");
            }

            // Validate the access token (even if expired)
            ClaimsPrincipal principal;
            var validatedToken = ValidateAccessTokenNoLifetime(accessToken, out principal);

            if (validatedToken == null)
            {
                throw new SecurityTokenException("Invalid access token");
            }

            // Extract claims from the token
            var jti = principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
            var userId = int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier));

            // Check if the refresh token exists and is valid
            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken && t.UserId == userId && t.JwtId == jti);

            if (storedToken == null)
            {
                _logger.LogWarning($"Invalid refresh token attempt for user {userId}");
                throw new SecurityTokenException("Invalid refresh token");
            }

            if (storedToken.IsRevoked)
            {
                _logger.LogWarning($"Attempt to use revoked refresh token for user {userId}");
                throw new SecurityTokenException("Refresh token has been revoked");
            }

            if (storedToken.ExpiresAt < DateTime.UtcNow)
            {
                _logger.LogWarning($"Attempt to use expired refresh token for user {userId}");
                throw new SecurityTokenException("Refresh token has expired");
            }

            // Get the user
            var user = await _context.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning($"User {userId} not found for refresh token");
                throw new SecurityTokenException("User not found");
            }

            // Extract roles from the existing token
            var roles = principal.FindAll(c => c.Type == ClaimTypes.Role).Select(c => c.Value).ToList();

            // Revoke the current refresh token
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = GetClientIpAddress();
            storedToken.ReplacedByToken = "Pending generation";

            _context.RefreshTokens.Update(storedToken);
            await _context.SaveChangesAsync();

            // Generate new tokens
            var tokenResult = await GenerateTokensAsync(user, roles);

            // Update the replaced by token reference
            storedToken.ReplacedByToken = tokenResult.RefreshToken;
            await _context.SaveChangesAsync();

            // Track token refresh for security monitoring
            await TrackTokenRefresh(userId, storedToken.Token, tokenResult.RefreshToken, GetClientIpAddress());

            return tokenResult;
        }

        /// <summary>
        /// Revokes a refresh token
        /// </summary>
        public async Task<bool> RevokeTokenAsync(string refreshToken)
        {
            if (string.IsNullOrEmpty(refreshToken))
            {
                return false;
            }

            var storedToken = await _context.RefreshTokens
                .FirstOrDefaultAsync(t => t.Token == refreshToken);

            if (storedToken == null)
            {
                _logger.LogWarning($"Attempt to revoke non-existent refresh token");
                return false;
            }

            if (storedToken.IsRevoked)
            {
                _logger.LogInformation($"Token {refreshToken.Substring(0, 10)}... already revoked");
                return true; // Already revoked
            }

            // Revoke the token
            storedToken.IsRevoked = true;
            storedToken.RevokedAt = DateTime.UtcNow;
            storedToken.RevokedByIp = GetClientIpAddress();
            storedToken.ReasonRevoked = "Explicit revocation";

            _context.RefreshTokens.Update(storedToken);
            await _context.SaveChangesAsync();

            // Track token revocation for security monitoring
            await TrackTokenRevocation(storedToken.UserId, refreshToken, GetClientIpAddress());
            _logger.LogInformation($"Token {refreshToken.Substring(0, 10)}... revoked for user {storedToken.UserId}");

            return true;
        }

        /// <summary>
        /// Revokes all refresh tokens for a user
        /// </summary>
        public async Task<bool> RevokeAllUserTokensAsync(int userId)
        {
            var activeTokens = await _context.RefreshTokens
                .Where(t => t.UserId == userId && !t.IsRevoked)
                .ToListAsync();

            if (!activeTokens.Any())
            {
                _logger.LogInformation($"No active tokens to revoke for user {userId}");
                return true; // No active tokens to revoke
            }

            foreach (var token in activeTokens)
            {
                token.IsRevoked = true;
                token.RevokedAt = DateTime.UtcNow;
                token.RevokedByIp = GetClientIpAddress();
                token.ReasonRevoked = "Administrative action - all tokens revoked";
            }

            await _context.SaveChangesAsync();

            // Track for security monitoring
            await TrackAllTokensRevocation(userId, GetClientIpAddress());
            _logger.LogInformation($"All {activeTokens.Count} tokens revoked for user {userId}");

            return true;
        }

        /// <summary>
        /// Validates an access token
        /// </summary>
        public bool ValidateAccessToken(string token, out ClaimsPrincipal principal)
        {
            principal = null;

            if (string.IsNullOrEmpty(token))
            {
                _logger.LogWarning("Attempted to validate null or empty token");
                return false;
            }

            try
            {
                var jwtKey = _configuration["Jwt:Key"];
                var jwtIssuer = _configuration["Jwt:Issuer"];
                var jwtAudience = _configuration["Jwt:Audience"];

                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);

                // Ensure token's signing algorithm is correct
                var jwtToken = validatedToken as JwtSecurityToken;
                if (jwtToken == null || !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogWarning("Token validation failed - invalid algorithm");
                    return false;
                }

                return true;
            }
            catch (SecurityTokenExpiredException)
            {
                _logger.LogInformation("Token validation failed - token expired");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating access token");
                return false;
            }
        }

        // -- Private helper methods --

        private JwtSecurityToken ValidateAccessTokenNoLifetime(string token, out ClaimsPrincipal principal)
        {
            principal = null;

            try
            {
                var jwtKey = _configuration["Jwt:Key"];
                var jwtIssuer = _configuration["Jwt:Issuer"];
                var jwtAudience = _configuration["Jwt:Audience"];

                var tokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ValidateIssuer = true,
                    ValidIssuer = jwtIssuer,
                    ValidateAudience = true,
                    ValidAudience = jwtAudience,
                    ValidateLifetime = false, // We don't care about expiration for refresh token validation
                    ClockSkew = TimeSpan.Zero
                };

                var tokenHandler = new JwtSecurityTokenHandler();
                principal = tokenHandler.ValidateToken(token, tokenValidationParameters, out SecurityToken validatedToken);

                // Ensure token's signing algorithm is correct
                var jwtToken = validatedToken as JwtSecurityToken;
                if (jwtToken == null || !jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    _logger.LogWarning("Token validation failed - invalid algorithm");
                    return null;
                }

                return jwtToken;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating access token for refresh");
                return null;
            }
        }

        private string GenerateRefreshToken()
        {
            var randomBytes = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomBytes);
            return Convert.ToBase64String(randomBytes);
        }

        private string GetClientIpAddress()
        {
            if (_httpContextAccessor.HttpContext == null)
            {
                return "127.0.0.1";
            }

            // Try to get the forwarded IP if behind a proxy
            string ip = _httpContextAccessor.HttpContext.Request.Headers["X-Forwarded-For"].FirstOrDefault();

            // If no forwarded IP, use the remote IP
            if (string.IsNullOrEmpty(ip))
            {
                ip = _httpContextAccessor.HttpContext.Connection.RemoteIpAddress?.ToString();
            }

            // If still no IP, use a default
            if (string.IsNullOrEmpty(ip))
            {
                ip = "127.0.0.1";
            }

            // If multiple IPs (comma separated), take the first one
            if (ip.Contains(","))
            {
                ip = ip.Split(',')[0].Trim();
            }

            return ip;
        }

        // Security monitoring methods
        private async Task TrackUserLogin(int userId, string ipAddress, string tokenId)
        {
            try
            {
                var loginEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "Login",
                    IpAddress = ipAddress,
                    TokenId = tokenId,
                    EventTime = DateTime.UtcNow,
                    UserAgent = _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString(),
                    AdditionalInfo = $"User login at {DateTime.UtcNow}"
                };

                _context.UserSecurityEvents.Add(loginEvent);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Logged login event for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error tracking login for user {userId}");
                // Don't throw - this is just for monitoring
            }
        }

        private async Task TrackTokenRefresh(int userId, string oldToken, string newToken, string ipAddress)
        {
            try
            {
                var refreshEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "TokenRefresh",
                    IpAddress = ipAddress,
                    TokenId = newToken,
                    EventTime = DateTime.UtcNow,
                    UserAgent = _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString(),
                    AdditionalInfo = $"Token refreshed at {DateTime.UtcNow}. Old token: {oldToken.Substring(0, 10)}..."
                };

                _context.UserSecurityEvents.Add(refreshEvent);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Logged token refresh for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error tracking token refresh for user {userId}");
                // Don't throw - this is just for monitoring
            }
        }

        private async Task TrackTokenRevocation(int userId, string token, string ipAddress)
        {
            try
            {
                var revocationEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "TokenRevocation",
                    IpAddress = ipAddress,
                    TokenId = token,
                    EventTime = DateTime.UtcNow,
                    UserAgent = _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString(),
                    AdditionalInfo = $"Token revoked at {DateTime.UtcNow}"
                };

                _context.UserSecurityEvents.Add(revocationEvent);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Logged token revocation for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error tracking token revocation for user {userId}");
                // Don't throw - this is just for monitoring
            }
        }

        private async Task TrackAllTokensRevocation(int userId, string ipAddress)
        {
            try
            {
                var revocationEvent = new UserSecurityEvent
                {
                    UserId = userId,
                    EventType = "AllTokensRevocation",
                    IpAddress = ipAddress,
                    TokenId = null,
                    EventTime = DateTime.UtcNow,
                    UserAgent = _httpContextAccessor.HttpContext?.Request.Headers["User-Agent"].ToString(),
                    AdditionalInfo = $"All tokens revoked at {DateTime.UtcNow}"
                };

                _context.UserSecurityEvents.Add(revocationEvent);
                await _context.SaveChangesAsync();
                _logger.LogInformation($"Logged all tokens revocation for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error tracking all tokens revocation for user {userId}");
                // Don't throw - this is just for monitoring
            }
        }

        Task<Iterfaces.TokenResult> ITokenService.GenerateTokensAsync(User user, IEnumerable<string> roles)
        {
            throw new NotImplementedException();
        }

        Task<Iterfaces.TokenResult> ITokenService.RefreshTokenAsync(string refreshToken, string accessToken)
        {
            throw new NotImplementedException();
        }
    }
}