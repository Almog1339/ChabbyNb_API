using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Threading.Tasks;
using ChabbyNb_API.Models;
using ChabbyNb_API.Services.Auth;
using ChabbyNb_API.Services.Iterfaces;

namespace ChabbyNb_API.Services
{
    public class JwtTokenService
    {
        private readonly IConfiguration _configuration;
        private readonly IRoleService _roleService;
        private readonly ITokenService _tokenService;

        public JwtTokenService(
            IConfiguration configuration,
            IRoleService roleService = null,
            ITokenService tokenService = null)
        {
            _configuration = configuration;
            _roleService = roleService;
            _tokenService = tokenService;
        }

        /// <summary>
        /// Legacy method to generate JWT token without refresh token
        /// Kept for backward compatibility
        /// </summary>
        public string GenerateJwtToken(User user)
        {
            // Get JWT settings from configuration
            var jwtKey = _configuration["Jwt:Key"];
            var jwtIssuer = _configuration["Jwt:Issuer"];
            var jwtAudience = _configuration["Jwt:Audience"];
            var jwtExpiryMinutes = user.IsAdmin ? _configuration.GetValue<int>("Jwt:ExpiryInMinutes", 14400) : _configuration.GetValue<int>("Jwt:ExpiryInMinutes", 180); // Default to 3 hours

            if (string.IsNullOrEmpty(jwtKey) || string.IsNullOrEmpty(jwtIssuer) || string.IsNullOrEmpty(jwtAudience))
            {
                throw new InvalidOperationException("JWT configuration is incomplete. Please check your appsettings.json file.");
            }

            // Create claims for the token
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, user.UserID.ToString()),
                new Claim(ClaimTypes.Email, user.Email),
                new Claim(ClaimTypes.Name, user.Username ?? user.Email),
                new Claim("IsAdmin", user.IsAdmin.ToString())
            };

            if (!string.IsNullOrEmpty(user.FirstName))
                claims.Add(new Claim(ClaimTypes.GivenName, user.FirstName));

            if (!string.IsNullOrEmpty(user.LastName))
                claims.Add(new Claim(ClaimTypes.Surname, user.LastName));

            // Create the JWT security token
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: jwtIssuer,
                audience: jwtAudience,
                claims: claims,
                expires: DateTime.Now.AddMinutes(jwtExpiryMinutes),
                signingCredentials: creds
            );

            // Return the serialized token
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// New method to generate both JWT and refresh tokens
        /// </summary>
        public async Task<TokenResult> GenerateTokensAsync(User user)
        {
            // If token service is not available, just return a wrapper around the legacy method
            if (_tokenService == null)
            {
                var accessToken = GenerateJwtToken(user);
                return new TokenResult
                {
                    AccessToken = accessToken,
                    RefreshToken = null,
                    AccessTokenExpiration = DateTime.Now.AddMinutes(_configuration.GetValue<int>("Jwt:ExpiryInMinutes", 180)),
                    RefreshTokenExpiration = DateTime.MinValue
                };
            }

            return await _tokenService.GenerateTokensAsync(user);
        }

        /// <summary>
        /// Refresh an access token using a refresh token
        /// </summary>
        public async Task<TokenResult> RefreshTokenAsync(string refreshToken, string accessToken)
        {
            if (_tokenService == null)
            {
                throw new InvalidOperationException("Token service is not available. Cannot refresh token.");
            }

            return await _tokenService.RefreshTokenAsync(refreshToken, accessToken);
        }

        /// <summary>
        /// Revoke a refresh token
        /// </summary>
        public async Task<bool> RevokeTokenAsync(string refreshToken)
        {
            if (_tokenService == null)
            {
                throw new InvalidOperationException("Token service is not available. Cannot revoke token.");
            }

            return await _tokenService.RevokeRefreshTokenAsync(refreshToken);
        }

        /// <summary>
        /// Revoke all refresh tokens for a user
        /// </summary>
        public async Task<bool> RevokeAllUserTokensAsync(int userId)
        {
            if (_tokenService == null)
            {
                throw new InvalidOperationException("Token service is not available. Cannot revoke tokens.");
            }

            return await _tokenService.RevokeAllUserTokensAsync(userId);
        }
    }
}