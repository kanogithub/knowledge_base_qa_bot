using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace CloudKB.Tests.BDD;

public static class JwtMockGenerator
{
    // Symmetric key used for signing JWTs during local testing.
    // Must match the key used in Gateway authentication configuration during test runs.
    public const string SecretKey = "a_very_secret_key_used_only_for_cloudkb_bdd_testing_32_bytes_long!";

    public static string GenerateToken(string userId, string audience = "cloudkb-api", string issuer = "cloudkb-auth")
    {
        var tokenHandler = new JwtSecurityTokenHandler();
        var key = Encoding.UTF8.GetBytes(SecretKey);

        var claims = new List<Claim>
        {
            new Claim("user_id", userId),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(2),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(key),
                SecurityAlgorithms.HmacSha256Signature)
        };

        var token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }
}
