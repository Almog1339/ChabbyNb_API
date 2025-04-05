using System;
using System.Security.Claims;
using System.Threading.Tasks;
using ChabbyNb_API.Models;

namespace ChabbyNb_API.Services.Iterfaces
{
    /// <summary>
    /// Interface for token service that handles JWT and refresh tokens
    /// </summary>
    public interface ITokenService
    {
        /// <summary>
        /// Generates access and refresh tokens for a user
        /// </summary>
        Task<TokenResult> GenerateTokensAsync(User user, IEnumerable<string> roles);

        /// <summary>
        /// Refreshes an access token using a refresh token
        /// </summary>
        Task<TokenResult> RefreshTokenAsync(string refreshToken, string accessToken);

        /// <summary>
        /// Revokes a refresh token
        /// </summary>
        Task<bool> RevokeTokenAsync(string refreshToken);

        /// <summary>
        /// Revokes all refresh tokens for a user
        /// </summary>
        Task<bool> RevokeAllUserTokensAsync(int userId);

        /// <summary>
        /// Validates an access token
        /// </summary>
        bool ValidateAccessToken(string token, out ClaimsPrincipal principal);
    }

    /// <summary>
    /// Result returned from token operations containing tokens and expiration information
    /// </summary>
    public class TokenResult
    {
        public string AccessToken { get; set; }
        public string RefreshToken { get; set; }
        public DateTime AccessTokenExpiration { get; set; }
        public DateTime RefreshTokenExpiration { get; set; }
    }
}