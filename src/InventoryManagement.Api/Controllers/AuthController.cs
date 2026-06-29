using System;
using System.Text;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using InventoryManagement.Api.Data;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class AuthController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly ICurrentUserService _currentUserService;
        private readonly IConfiguration _config;

        public AuthController(InventoryDbContext context, ICurrentUserService currentUserService, IConfiguration config)
        {
            _context = context;
            _currentUserService = currentUserService;
            _config = config;
        }

        [HttpPost("login-dev-mock")]
        [AllowAnonymous]
        public async Task<IActionResult> LoginDevMock([FromBody] DevLoginDto dto)
        {
            var jwtSecret = _config["Supabase:JwtSecret"] ?? "your-jwt-secret-here-at-least-32-chars-long";
            var supabaseUrl = _config["Supabase:Url"] ?? "https://your-project.supabase.co";

            // Mock User ID
            var userId = Guid.Parse("00000000-0000-0000-0000-000000000001");

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, dto.Email),
                new Claim(ClaimTypes.Role, "Admin"),
                new Claim("role", "authenticated")
            };

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

            var token = new JwtSecurityToken(
                issuer: $"{supabaseUrl}/auth/v1",
                audience: "authenticated",
                claims: claims,
                expires: DateTime.Now.AddDays(7),
                signingCredentials: creds
            );

            var tokenHandler = new JwtSecurityTokenHandler();
            var tokenString = tokenHandler.WriteToken(token);

            // Sync mock profile inside DB
            var profile = await _context.UserProfiles.FindAsync(userId);
            if (profile == null)
            {
                profile = new UserProfile
                {
                    Id = userId,
                    Email = dto.Email,
                    FullName = dto.Email.Split('@')[0] + " (Dev Admin)",
                    Role = "Admin",
                    Status = "Active",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.UserProfiles.Add(profile);
            }
            else
            {
                profile.FullName = dto.Email.Split('@')[0] + " (Dev Admin)";
                profile.Email = dto.Email;
                _context.UserProfiles.Update(profile);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                access_token = tokenString,
                user = new
                {
                    id = userId,
                    user_metadata = new { full_name = profile.FullName }
                }
            });
        }

        public class DevLoginDto
        {
            public string Email { get; set; } = string.Empty;
            public string Password { get; set; } = string.Empty;
        }

        [HttpGet("profile")]
        public async Task<IActionResult> GetProfile()
        {
            var userId = _currentUserService.UserId;
            if (userId == Guid.Empty) return Unauthorized();

            var profile = await _context.UserProfiles.FindAsync(userId);
            if (profile == null)
            {
                // Auto-sync if profile not found but JWT is valid
                return await SyncProfile(new UserSyncDto { FullName = "New User" });
            }

            return Ok(profile);
        }

        [HttpPost("sync")]
        public async Task<IActionResult> SyncProfile([FromBody] UserSyncDto dto)
        {
            var userId = _currentUserService.UserId;
            if (userId == Guid.Empty) return Unauthorized();

            // Extract email from JWT claims
            var email = User.FindFirst(ClaimTypes.Email)?.Value 
                        ?? User.FindFirst("email")?.Value 
                        ?? "";

            var profile = await _context.UserProfiles.FindAsync(userId);
            var isFirstUser = !await _context.UserProfiles.AnyAsync();

            if (profile == null)
            {
                profile = new UserProfile
                {
                    Id = userId,
                    Email = email,
                    FullName = dto.FullName,
                    Role = isFirstUser ? "Admin" : "Viewer", // First user is Admin
                    Status = "Active",
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.UserProfiles.Add(profile);
            }
            else
            {
                profile.FullName = dto.FullName;
                if (!string.IsNullOrEmpty(email)) profile.Email = email;
                _context.UserProfiles.Update(profile);
            }

            await _context.SaveChangesAsync();
            return Ok(profile);
        }

        [HttpPost("update-role")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> UpdateRole([FromBody] UserRoleUpdateDto dto)
        {
            var profile = await _context.UserProfiles.FindAsync(dto.UserId);
            if (profile == null) return NotFound("User profile not found.");

            profile.Role = dto.Role;
            profile.Status = dto.Status;
            
            _context.UserProfiles.Update(profile);
            await _context.SaveChangesAsync();
            return Ok(profile);
        }

        [HttpGet("users")]
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> GetAllUsers()
        {
            var users = await _context.UserProfiles.ToListAsync();
            return Ok(users);
        }
    }
}
