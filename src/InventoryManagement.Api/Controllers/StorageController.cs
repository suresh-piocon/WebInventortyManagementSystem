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

        // ─────────────────────────────────────────────────────────────────────────
        // Checks whether a Supabase key value is a placeholder / invalid JWT
        // ─────────────────────────────────────────────────────────────────────────
        private static bool IsPlaceholder(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return true;
            var v = value.ToLowerInvariant();
            if (v.Contains("your-anon-key") || v.Contains("your-service-role-key") ||
                v.Contains("your-jwt-secret") || v.Contains("your-project"))
                return true;

            // Supabase JWTs have exactly 3 dot-separated parts
            return value.Split('.').Length != 3;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Resolve the Supabase service key from config
        // ─────────────────────────────────────────────────────────────────────────
        private string? GetSupabaseServiceKey()
        {
            var serviceRoleKey = _config["Supabase:ServiceRoleKey"];
            var anonKey        = _config["Supabase:AnonKey"];

            if (!IsPlaceholder(serviceRoleKey)) return serviceRoleKey;
            if (!IsPlaceholder(anonKey))        return anonKey;
            return null;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // POST /api/storage/upload?barcodeNo=ITEM000001
        //
        // Uploads an image to Supabase Storage "inventory-images" bucket.
        // When barcodeNo is supplied the file is named {barcodeNo}.jpg so that
        // the same barcode always overwrites (updates) its own photo.
        // Local-folder fallback has been REMOVED — Supabase must be configured.
        // ─────────────────────────────────────────────────────────────────────────
        [HttpPost("upload")]
        public async Task<IActionResult> UploadImage(IFormFile file, [FromQuery] string? barcodeNo = null)
        {
            if (file == null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var supabaseUrl = _config["Supabase:Url"];
            var serviceKey  = GetSupabaseServiceKey();

            if (string.IsNullOrEmpty(supabaseUrl) || IsPlaceholder(supabaseUrl) || string.IsNullOrEmpty(serviceKey))
            {
                return StatusCode(StatusCodes.Status503ServiceUnavailable,
                    "Supabase Storage is not configured on this server. " +
                    "Please set a valid Supabase:Url and Supabase:ServiceRoleKey in appsettings.json.");
            }

            // ── Determine filename ────────────────────────────────────────────
            // If a barcodeNo is provided, use it as the filename so the barcode
            // number is the stable key for retrieving the photo later.
            string fileName;
            if (!string.IsNullOrWhiteSpace(barcodeNo))
            {
                // Sanitise barcode — keep alphanumerics and hyphens only
                var safe = System.Text.RegularExpressions.Regex.Replace(barcodeNo.Trim(), @"[^A-Za-z0-9\-_]", "");
                fileName = $"{safe}.jpg";
            }
            else
            {
                fileName = $"{Guid.NewGuid()}.jpg";
            }

            // ── Upload to Supabase Storage ────────────────────────────────────
            try
            {
                var bucketName = "inventory-images";
                var uploadUrl  = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/{bucketName}/{fileName}";

                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", serviceKey);
                httpClient.DefaultRequestHeaders.Add("apikey", serviceKey);

                using var stream  = file.OpenReadStream();
                using var content = new StreamContent(stream);
                // Force image/jpeg so Supabase accepts the content-type
                content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                // x-upsert: true → overwrite if same filename already exists (photo update)
                content.Headers.Add("x-upsert", "true");

                var response = await httpClient.PostAsync(uploadUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    // Public URL — no auth token required to view
                    var publicUrl = $"{supabaseUrl.TrimEnd('/')}/storage/v1/object/public/{bucketName}/{fileName}";
                    return Ok(new { url = publicUrl });
                }
                else
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode,
                        $"Supabase Storage upload failed: {errorBody}");
                }
            }
            catch (Exception ex)
            {
                return StatusCode(StatusCodes.Status500InternalServerError,
                    $"Error uploading to Supabase: {ex.Message}");
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GET /api/storage/lookup/{code}
        // Used by CapturePhotos page to resolve a barcode or batch to item details
        // ─────────────────────────────────────────────────────────────────────────
        [HttpGet("lookup/{code}")]
        public async Task<IActionResult> LookupItem(string code)
        {
            // Find by Barcode first
            var barcode = await _context.BarcodeMasters
                .Include(b => b.Item)
                .FirstOrDefaultAsync(b => b.Barcode == code);

            if (barcode != null)
            {
                return Ok(new
                {
                    itemName         = barcode.Item?.Name ?? "Unknown Item",
                    itemCode         = barcode.Item?.Code ?? "Unknown Code",
                    isBarcode        = true,
                    selectedBarcode  = barcode.Barcode,
                    existingImageUrl = barcode.ImageUrl,
                    barcodes         = new[] { new { barcodeNo = barcode.Barcode, imageUrl = barcode.ImageUrl } }
                });
            }

            // Try finding by BatchNo
            var barcodesInBatch = await _context.BarcodeMasters
                .Include(b => b.Item)
                .Where(b => b.BatchNo == code)
                .ToListAsync();

            if (barcodesInBatch.Any())
            {
                var first = barcodesInBatch.First();
                return Ok(new
                {
                    itemName         = first.Item?.Name ?? "Unknown Item",
                    itemCode         = first.Item?.Code ?? "Unknown Code",
                    isBarcode        = false,
                    selectedBarcode  = (string?)null,
                    existingImageUrl = (string?)null,
                    barcodes         = barcodesInBatch
                        .Select(b => new { barcodeNo = b.Barcode, imageUrl = b.ImageUrl })
                        .ToList()
                });
            }

            return NotFound("Barcode or Batch number not found in system.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // GET /api/storage/image?url={supabaseUrl}
        //
        // Proxy endpoint — fetches an image from Supabase Storage using the
        // service-role key (server-side only) and streams it back to the browser.
        // This keeps the service key completely server-side.
        // ─────────────────────────────────────────────────────────────────────────
        [HttpGet("image")]
        public async Task<IActionResult> GetImage([FromQuery] string url)
        {
            if (string.IsNullOrEmpty(url))
                return BadRequest("URL is required.");

            // If it's a base64 data URL, return it directly
            if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                return BadRequest("Base64 data URLs should not be proxied.");

            var supabaseUrl = _config["Supabase:Url"];
            var serviceKey  = GetSupabaseServiceKey();

            // For Supabase URLs — fetch with auth header
            bool isSupabaseUrl = !string.IsNullOrEmpty(supabaseUrl) &&
                                 url.Contains(supabaseUrl, StringComparison.OrdinalIgnoreCase);

            if (isSupabaseUrl && !string.IsNullOrEmpty(serviceKey))
            {
                try
                {
                    using var httpClient = new HttpClient();
                    httpClient.DefaultRequestHeaders.Authorization =
                        new AuthenticationHeaderValue("Bearer", serviceKey);
                    httpClient.DefaultRequestHeaders.Add("apikey", serviceKey);

                    var response = await httpClient.GetAsync(url);
                    if (response.IsSuccessStatusCode)
                    {
                        var contentType = response.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        var stream = await response.Content.ReadAsStreamAsync();
                        return File(stream, contentType);
                    }

                    // Fallback: try public (no-auth) fetch for public bucket objects
                    var pubResponse = await new HttpClient().GetAsync(url);
                    if (pubResponse.IsSuccessStatusCode)
                    {
                        var ct = pubResponse.Content.Headers.ContentType?.MediaType ?? "image/jpeg";
                        return File(await pubResponse.Content.ReadAsStreamAsync(), ct);
                    }

                    var err = await response.Content.ReadAsStringAsync();
                    return StatusCode((int)response.StatusCode,
                        $"Failed to retrieve image from Supabase: {err}");
                }
                catch (Exception ex)
                {
                    return StatusCode(StatusCodes.Status500InternalServerError,
                        $"Error fetching from Supabase: {ex.Message}");
                }
            }

            // For non-Supabase URLs (e.g. old local /uploads/ paths) → redirect
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                return Redirect(url);
            }

            // Relative /uploads/ path → serve from wwwroot
            var uploadsIndex = url.IndexOf("/uploads/", StringComparison.OrdinalIgnoreCase);
            if (uploadsIndex >= 0)
            {
                var relativePath = url.Substring(uploadsIndex);
                var filePath = Path.Combine(
                    _env.WebRootPath ?? Path.Combine(Directory.GetCurrentDirectory(), "wwwroot"),
                    relativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));

                if (System.IO.File.Exists(filePath))
                {
                    var bytes = await System.IO.File.ReadAllBytesAsync(filePath);
                    return File(bytes, "image/jpeg");
                }
            }

            return NotFound("Image not found.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // POST /api/storage/update-photo
        // Associates (or clears) a Supabase image URL with a specific barcode
        // ─────────────────────────────────────────────────────────────────────────
        [HttpPost("update-photo")]
        public async Task<IActionResult> UpdatePhoto([FromBody] UpdatePhotoRequest request)
        {
            if (string.IsNullOrEmpty(request.Code))
                return BadRequest("Code is required.");

            var barcode = await _context.BarcodeMasters
                .FirstOrDefaultAsync(b => b.Barcode == request.Code);

            if (barcode != null)
            {
                barcode.ImageUrl = request.ImageUrl;
                _context.BarcodeMasters.Update(barcode);
                await _context.SaveChangesAsync();
                return Ok(new { message = $"Photo updated for barcode {barcode.Barcode}." });
            }

            // Prevent accidental batch-wide update
            var existsAsBatch = await _context.BarcodeMasters.AnyAsync(b => b.BatchNo == request.Code);
            if (existsAsBatch)
                return BadRequest("Select a specific barcode number to update the image. Batch-wide updates are disabled.");

            return NotFound("No barcode found matching the code.");
        }
    }

    public class UpdatePhotoRequest
    {
        public string Code      { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
    }
}
