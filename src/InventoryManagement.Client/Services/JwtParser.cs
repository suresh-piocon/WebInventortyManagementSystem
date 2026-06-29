using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;

namespace InventoryManagement.Client.Services
{
    public static class JwtParser
    {
        public static IEnumerable<Claim> ParseClaimsFromJwt(string jwt)
        {
            var claims = new List<Claim>();
            var payload = jwt.Split('.')[1];

            var jsonBytes = ParseBase64WithoutPadding(payload);
            var keyValuePairs = JsonSerializer.Deserialize<Dictionary<string, object>>(jsonBytes);

            if (keyValuePairs != null)
            {
                foreach (var kvp in keyValuePairs)
                {
                    var valueStr = kvp.Value?.ToString() ?? "";
                    
                    if (kvp.Key == "role" || kvp.Key == ClaimTypes.Role)
                    {
                        claims.Add(new Claim(ClaimTypes.Role, valueStr));
                    }
                    else if (kvp.Key == "email" || kvp.Key == ClaimTypes.Email)
                    {
                        claims.Add(new Claim(ClaimTypes.Email, valueStr));
                    }
                    else if (kvp.Key == "sub" || kvp.Key == ClaimTypes.NameIdentifier)
                    {
                        claims.Add(new Claim(ClaimTypes.NameIdentifier, valueStr));
                    }
                    else if (kvp.Key == "name" || kvp.Key == ClaimTypes.Name)
                    {
                        claims.Add(new Claim(ClaimTypes.Name, valueStr));
                    }
                    else
                    {
                        claims.Add(new Claim(kvp.Key, valueStr));
                    }
                }
            }

            return claims;
        }

        private static byte[] ParseBase64WithoutPadding(string base64)
        {
            switch (base64.Length % 4)
            {
                case 2: base64 += "=="; break;
                case 3: base64 += "="; break;
            }
            return Convert.FromBase64String(base64);
        }
    }
}
