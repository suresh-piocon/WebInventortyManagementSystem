using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Api.Data;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class StorageController : ControllerBase
    {
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly InventoryDbContext _context;

        public StorageController(IWebHostEnvironment env, IConfiguration config, InventoryDbContext context)
        {
            _env = env;
            _config = config;
            _context = context;
        }

        [HttpPost("upload")]
        public async Task<IActionResult> UploadImage(IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                return BadRequest("No file uploaded.");
            }

            var extension = Path.GetExtension(file.FileName);
            var uniqueFileName = $"{Guid.NewGuid()}{extension}";

            var supabaseUrl = _config["Supabase:Url"];
            var serviceRoleKey = _config["Supabase:ServiceRoleKey"];
            var anonKey = _config["Supabase:AnonKey"];

            // Helper function to check if a value is a placeholder or invalid key
            bool IsPlaceholder(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return true;
                var v = value.ToLowerInvariant();
                if (v.Contains("your-anon-key") || v.Contains("your-service-role-key") || v.Contains("your-jwt-secret") || v.Contains("your-project"))
                {
                    return true;
                }
                
                // Supabase service keys and anon keys are JWTs (3 parts separated by dots).
                // If it doesn't have 3 parts, it's not a valid Supabase key and upload would fail with 403.
                var parts = value.Split('.');
                if (parts.Length != 3)
                {
                    return true;
                }

                return false;
            }

            if (IsPlaceholder(serviceRoleKey)) serviceRoleKey = null;
            if (IsPlaceholder(anonKey)) anonKey = null;

            var serviceKey = serviceRoleKey ?? anonKey;

            // If Supabase is not configured, fall back to local file storage
            if (string.IsNullOrEmpty(supabaseUrl) || IsPlaceholder(supabaseUrl) || string.IsNullOrEmpty(serviceKey))
            {
                var uploadsFolder = Path.Combine(_env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"), "uploads");
                if (!Directory.Exists(uploadsFolder))
                {
                    Directory.CreateDirectory(uploadsFolder);
                }

                var filePath = Path.Combine(uploadsFolder, uniqueFileName);
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                // Return local server URL
                var localUrl = $"{Request.Scheme}://{Request.Host}/uploads/{uniqueFileName}";
                return Ok(new { url = localUrl });
            }

            // Supabase is configured - upload to Supabase Storage REST API
            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
                httpClient.DefaultRequestHeaders.Add("apikey", serviceKey);

                // Supabase Storage Upload URL: POST /storage/v1/object/{bucket}/{path}
                // We use a default bucket name 'inventory-images'
                var bucketName = "inventory-images";
                var uploadUrl = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/{bucketName}/{uniqueFileName}";

                using var stream = file.OpenReadStream();
                using var content = new StreamContent(stream);
                content.Headers.ContentType = new MediaTypeHeaderValue(file.ContentType ?? "image/jpeg");
                content.Headers.Add("x-upsert", "true");

                var response = await httpClient.PostAsync(uploadUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    // Construct public URL
                    // Public URL format: {supabaseUrl}/storage/v1/object/public/{bucket}/{path}
                    var publicUrl = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/public/{bucketName}/{uniqueFileName}";
                    return Ok(new { url = publicUrl });
                }
                else
                {
                    var errorResponse = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode, $"Supabase Storage upload failed: {errorResponse}");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error uploading to Supabase: {ex.Message}");
            }
        }

        [HttpGet("lookup/{code}")]
        public async Task<IActionResult> LookupItem(string code)
        {
            var barcode = await _context.BarcodeMasters
                .Include(b => b.Item)
                .FirstOrDefaultAsync(b => b.Barcode == code || b.BatchNo == code);

            if (barcode == null)
            {
                return NotFound("Barcode or Batch number not found in system.");
            }

            var imageUrl = barcode.ImageUrl;
            if (string.IsNullOrEmpty(imageUrl))
            {
                imageUrl = await _context.BarcodeMasters
                    .Where(b => b.TrackingNo == barcode.TrackingNo && b.BatchNo == barcode.BatchNo && b.ImageUrl != null && b.ImageUrl != "")
                    .Select(b => b.ImageUrl)
                    .FirstOrDefaultAsync();
            }

            return Ok(new
            {
                itemName = barcode.Item?.Name ?? "Unknown Item",
                itemCode = barcode.Item?.Code ?? "Unknown Code",
                existingImageUrl = imageUrl
            });
        }

        [HttpGet("image")]
        public async Task<IActionResult> GetImage([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("URL is required.");
            }

            var supabaseUrl = _config["Supabase:Url"];
            if (string.IsNullOrEmpty(supabaseUrl) || !url.Contains(supabaseUrl, StringComparison.OrdinalIgnoreCase))
            {
                // If not a Supabase URL, redirect to local or external target
                return Redirect(url);
            }

            var serviceRoleKey = _config["Supabase:ServiceRoleKey"];
            var anonKey = _config["Supabase:AnonKey"];

            bool IsPlaceholder(string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return true;
                var v = value.ToLowerInvariant();
                if (v.Contains("your-anon-key") || v.Contains("your-service-role-key") || v.Contains("your-jwt-secret") || v.Contains("your-project"))
                {
                    return true;
                }
                var parts = value.Split('.');
                if (parts.Length != 3)
                {
                    return true;
                }
                return false;
            }

            if (IsPlaceholder(serviceRoleKey)) serviceRoleKey = null;
            if (IsPlaceholder(anonKey)) anonKey = null;

            var serviceKey = serviceRoleKey ?? anonKey;

            if (string.IsNullOrEmpty(serviceKey))
            {
                // Fallback to anonymous fetch if no keys are configured
                try
                {
                    using var httpClient = new HttpClient();
                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        var stream = await response.Content.ReadAsStreamAsync();
                        return File(stream, contentType);
                    }
                    return StatusCode((int)response.StatusCode, "Failed to retrieve image anonymously.");
                }
                catch (Exception ex)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError, $"Error: {ex.Message}");
                }
            }

            try
            {
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", serviceKey);
                httpClient.DefaultRequestHeaders.Add("apikey", serviceKey);

                // Try the public URL with authorization headers
                var response = await httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                    var stream = await response.Content.ReadAsStreamAsync();
                    return File(stream, contentType);
                }

                // If public access with headers failed, try accessing via authenticated storage REST endpoint
                if (url.Contains("/storage/v1/object/public/", StringComparison.OrdinalIgnoreCase))
                {
                    var authenticatedUrl = url.Replace("/storage/v1/object/public/", "/storage/v1/object/authenticated/", StringComparison.OrdinalIgnoreCase);
                    var authResponse = await httpClient.GetAsync(authenticatedUrl);
                    if (authResponse.IsSuccessStatusCode)
                    {
                        var contentType = authResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        var stream = await authResponse.Content.ReadAsStreamAsync();
                        return File(stream, contentType);
                    }
                }

                return StatusCode((int)response.StatusCode, "Failed to retrieve image from Supabase storage.");
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError, $"Error fetching from Supabase: {ex.Message}");
            }
        }

        [HttpPost("update-photo")]
        public async Task<IActionResult> UpdatePhoto([FromBody] UpdatePhotoRequest request)
        {
            if (string.IsNullOrEmpty(request.Code))
            {
                return BadRequest("Code is required.");
            }

            var barcodes = await _context.BarcodeMasters
                .Where(b => b.Barcode == request.Code || b.BatchNo == request.Code)
                .ToListAsync();

            if (!barcodes.Any())
            {
                return NotFound("No barcode or batch number found matching the code.");
            }

            foreach (var b in barcodes)
            {
                b.ImageUrl = request.ImageUrl;
            }

            _context.BarcodeMasters.UpdateRange(barcodes);
            await _context.SaveChangesAsync();

            return Ok(new { message = $"Photo updated successfully for {barcodes.Count} item(s)." });
        }
    }

    public class UpdatePhotoRequest
    {
        public string Code { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
    }
}
