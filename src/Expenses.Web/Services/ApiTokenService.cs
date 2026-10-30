using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;

namespace Expenses.Web.Services;

public record ApiToken(string AccessToken, DateTime ExpiresAt);

public class ApiTokenService
{
    private readonly SymmetricSecurityKey? key;
    public string Issuer { get; }
    public string Audience { get; }
    public bool Enabled => key is not null;

    public ApiTokenService(IConfiguration configuration)
    {
        Issuer = configuration["ApiTokens:Issuer"] ?? "expenses";
        Audience = configuration["ApiTokens:Audience"] ?? "expenses-api";
        var configuredKey = configuration["ApiTokens:SigningKey"];
        if (string.IsNullOrWhiteSpace(configuredKey)) return;
        byte[] bytes;
        try { bytes = Convert.FromBase64String(configuredKey); }
        catch (FormatException) { throw new InvalidOperationException("ApiTokens:SigningKey must contain a Base64-encoded random key of at least 32 bytes."); }
        if (bytes.Length < 32)
            throw new InvalidOperationException("ApiTokens:SigningKey must contain at least 32 random bytes, encoded as Base64.");
        key = new SymmetricSecurityKey(bytes);
    }

    public TokenValidationParameters ValidationParameters => new()
    {
        ValidateIssuerSigningKey = true, IssuerSigningKey = key,
        ValidateIssuer = true, ValidIssuer = Issuer,
        ValidateAudience = true, ValidAudience = Audience,
        ValidateLifetime = true, RequireExpirationTime = true, RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ValidTypes = ["JWT"],
        ClockSkew = TimeSpan.FromSeconds(30),
        AuthenticationType = "Bearer", NameClaimType = JwtRegisteredClaimNames.Email
    };

    public ApiToken Create(IdentityUser user)
    {
        if (key is null) throw new InvalidOperationException("API tokens are not configured.");
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(15);
        var token = new JwtSecurityToken(Issuer, Audience,
        [
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? ""),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64),
            new("scope", "expenses"),
            new("ver", UserVersion(user))
        ], now, expires, new SigningCredentials(key, SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires);
    }

    // Password/security-stamp changes invalidate existing tokens without storing each token.
    public static string UserVersion(IdentityUser user) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(user.SecurityStamp ?? "")));
}
