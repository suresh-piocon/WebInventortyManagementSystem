using System;
using System.Net.Http;
using System.Threading.Tasks;

namespace InventoryManagement.Client.Services
{
    public class ImageLoaderService
    {
        private readonly HttpClient _httpClient;

        public ImageLoaderService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }

        public async Task<string?> LoadImageAsBase64Async(string? url)
        {
            if (string.IsNullOrEmpty(url)) return null;

            // If already a base64 string, return it
            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
            {
                return url;
            }

            try
            {
                // Fetch the image through the backend storage proxy
                var proxyUrl = $"api/storage/image?url={Uri.EscapeDataString(url)}";
                var response = await _httpClient.GetAsync(proxyUrl);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                    return $"data:{contentType};base64,{Convert.ToBase64String(bytes)}";
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
