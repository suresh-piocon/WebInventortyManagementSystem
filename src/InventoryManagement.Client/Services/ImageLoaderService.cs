using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace InventoryManagement.Client.Services
{
    public class ImageLoaderService
    {
        private readonly IConfiguration _config;

        public ImageLoaderService(IConfiguration config)
        {
            _config = config;
        }

        public async Task<string?> LoadImageAsBase64Async(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            // If already a base64 string, return it
            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            var supabaseUrl = _config["Supabase:Url"];
            if (string.IsNullOrEmpty(supabaseUrl) || !url.Contains(supabaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                return url; // Non-Supabase URLs remain unchanged
            }

            try
            {
                var anonKey = _config["Supabase:AnonKey"];

                using var client = new HttpClient();
                if (!string.IsNullOrEmpty(anonKey) && !anonKey.Contains("your-anon-key"))
                {
                    client.DefaultRequestHeaders.Add("apikey", anonKey);
                    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", anonKey);
                }

                // 1. Try public URL with auth headers
                var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                    return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
                }

                // 2. Try authenticated REST storage endpoint
                if (url.Contains("/storage/v1/object/public/", StringComparison.OrdinalIgnoreCase))
                {
                    var authenticatedUrl = url.Replace("/storage/v1/object/public/", "/storage/v1/object/authenticated/", StringComparison.OrdinalIgnoreCase);
                    var authResponse = await client.GetAsync(authenticatedUrl);
                    if (authResponse.IsSuccessStatusCode)
                    {
                        var bytes = await authResponse.Content.ReadAsByteArrayAsync();
                        var contentType = authResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
                    }
                }
            }
            catch
            {
                // Fall back to original url on exception
            }

            return url;
        }
    }
}
