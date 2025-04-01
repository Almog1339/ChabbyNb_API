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
        /// Generates both access token and refresh token for a user
        /// </summary>
        /// <param name="user">The user to generate tokens for</param>
        /// <returns>A TokenResult containing both tokens and their expiration times</returns>
        Task<TokenResult> GenerateTokensAsync(User user);

        /// <summary>
        /// Refreshes an access token using a refresh token
        /// </summary>
        /// <param name="refreshToken">The refresh token</param>
        /// <param name="accessToken">The expired access token</param>
        /// <returns>A new TokenResult with fresh tokens</returns>
        Task<TokenResult> RefreshTokenAsync(string refreshToken, string accessToken);

        /// <summary>
        /// Revokes a specific refresh token
        /// </summary>
        /// <param name="refreshToken">The refresh token to revoke</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> RevokeRefreshTokenAsync(string refreshToken);

        /// <summary>
        /// Revokes all refresh tokens for a specific user
        /// </summary>
        /// <param name="userId">The user ID</param>
        /// <returns>True if successful, false otherwise</returns>
        Task<bool> RevokeAllUserTokensAsync(int userId);

        /// <summary>
        /// Validates an access token and extracts its claims principal
        /// </summary>
        /// <param name="token">The access token to validate</param>
        /// <param name="principal">The extracted claims principal (out parameter)</param>
        /// <returns>True if valid, false otherwise</returns>
        bool ValidateAccessToken(string token, out ClaimsPrincipal principal);
    }

    /// <summary>
    /// Result object for token operations
    /// </summary>
    public class TokenResult
    {
        /// <summary>
        /// The JWT access token
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// The refresh token used to get a new access token
        /// </summary>
        public string RefreshToken { get; set; }

        /// <summary>
        /// When the access token expires
        /// </summary>
        public DateTime AccessTokenExpiration { get; set; }

        /// <summary>
        /// When the refresh token expires
        /// </summary>
        public DateTime RefreshTokenExpiration { get; set; }
    }
}