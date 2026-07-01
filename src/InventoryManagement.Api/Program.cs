using System;
using System.Text;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using InventoryManagement.Api.Data;
using InventoryManagement.Shared;

var builder = WebApplication.CreateBuilder(args);
// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddHttpContextAccessor();

// Configure Current User Service
builder.Services.AddScoped<ICurrentUserService, HttpContextCurrentUserService>();
builder.Services.AddScoped<InventoryManagement.Api.Services.ValuationService>();
builder.Services.AddScoped<InventoryManagement.Api.Services.ReportingService>();

// Support standard Supabase environment variables (for docker, render, railway, etc.)
var envSupabaseUrl = Environment.GetEnvironmentVariable("SUPABASE_URL");
if (!string.IsNullOrEmpty(envSupabaseUrl))
{
    builder.Configuration["Supabase:Url"] = envSupabaseUrl;
}

var envJwtSecret = Environment.GetEnvironmentVariable("SUPABASE_JWT_SECRET") 
                ?? Environment.GetEnvironmentVariable("SUPABASE_JWTSECRET");
if (!string.IsNullOrEmpty(envJwtSecret))
{
    builder.Configuration["Supabase:JwtSecret"] = envJwtSecret;
}

var envAnonKey = Environment.GetEnvironmentVariable("SUPABASE_ANON_KEY") 
              ?? Environment.GetEnvironmentVariable("SUPABASE_ANONKEY");
if (!string.IsNullOrEmpty(envAnonKey))
{
    builder.Configuration["Supabase:AnonKey"] = envAnonKey;
}

var envServiceRoleKey = Environment.GetEnvironmentVariable("SUPABASE_SERVICE_ROLE_KEY") 
                     ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICEROLEKEY");
if (!string.IsNullOrEmpty(envServiceRoleKey))
{
    builder.Configuration["Supabase:ServiceRoleKey"] = envServiceRoleKey;
}

var envSupabaseConnection = Environment.GetEnvironmentVariable("SUPABASE_CONNECTION") 
                         ?? Environment.GetEnvironmentVariable("SUPABASE_CONNECTION_STRING")
                         ?? Environment.GetEnvironmentVariable("ConnectionStrings__SupabaseConnection");
if (!string.IsNullOrEmpty(envSupabaseConnection))
{
    builder.Configuration["ConnectionStrings:SupabaseConnection"] = envSupabaseConnection;
}

// Configure Database Connection (dynamic fallback SQLite / Npgsql)
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=inventory.db";
var supabaseConnection = builder.Configuration.GetConnectionString("SupabaseConnection");

// Use Supabase PostgreSQL if it is configured (not matching default placeholder)
if (!string.IsNullOrEmpty(supabaseConnection) && !supabaseConnection.Contains("YOUR_PASSWORD"))
{
    connectionString = supabaseConnection;
}


builder.Services.AddDbContext<InventoryDbContext>(options =>
{
    if (connectionString.Contains("inventory.db") || connectionString.StartsWith("Data Source"))
    {
        options.UseSqlite(connectionString);
    }
    else
    {
        options.UseNpgsql(connectionString);
    }
});

// Configure CORS
builder.Services.AddCors(options =>
{
    options.AddPolicy("BlazorCors", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Configure Supabase JWT Bearer Authentication
var jwtSecret = builder.Configuration["Supabase:JwtSecret"] ?? "your-jwt-secret-here-at-least-32-chars-long";
var supabaseUrl = builder.Configuration["Supabase:Url"] ?? "https://your-project.supabase.co";

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = $"{supabaseUrl}/auth/v1",
        ValidateAudience = true,
        ValidAudience = "authenticated",
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret)),
        ValidateLifetime = true
    };
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseCors("BlazorCors");

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => Results.Text("Inventory Management System API is running successfully!"));
app.MapControllers();

// Ensure Database is Created and Seeded
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<InventoryDbContext>();
        context.Database.EnsureCreated();
        
        // Adjust database indexes to remove uniqueness on TrackingNo
        try
        {
            var isSqlite = context.Database.ProviderName?.Contains("Sqlite") == true;
            if (isSqlite)
            {
                context.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_StockInwardDetails_TrackingNo\";");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_StockInwardDetails_TrackingNo\" ON \"StockInwardDetails\"(\"TrackingNo\");");
                
                context.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_QRCodeMaster_TrackingNo\";");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_QRCodeMaster_TrackingNo\" ON \"QRCodeMaster\"(\"TrackingNo\");");
            }
            else
            {
                // PostgreSQL
                context.Database.ExecuteSqlRaw("ALTER TABLE \"StockInwardDetails\" DROP CONSTRAINT IF EXISTS \"IX_StockInwardDetails_TrackingNo\";");
                context.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_StockInwardDetails_TrackingNo\";");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_StockInwardDetails_TrackingNo\" ON \"StockInwardDetails\"(\"TrackingNo\");");
                
                context.Database.ExecuteSqlRaw("ALTER TABLE \"QRCodeMaster\" DROP CONSTRAINT IF EXISTS \"IX_QRCodeMaster_TrackingNo\";");
                context.Database.ExecuteSqlRaw("DROP INDEX IF EXISTS \"IX_QRCodeMaster_TrackingNo\";");
                context.Database.ExecuteSqlRaw("CREATE INDEX IF NOT EXISTS \"IX_QRCodeMaster_TrackingNo\" ON \"QRCodeMaster\"(\"TrackingNo\");");
            }
            Console.WriteLine("Database unique index constraints on TrackingNo adjusted successfully.");
            
            // Run purchase report query to see if IDs are populated
            var query = context.StockInwardDetails
                .Include(d => d.StockInward)
                    .ThenInclude(si => si!.Supplier)
                .Include(d => d.Item)
                .AsQueryable();
            var reportList = context.StockInwardDetails
                .OrderByDescending(d => d.StockInward!.InwardDate)
                .Select(d => new SupplierPurchaseReportDto
                {
                    Id = d.Id,
                    StockInwardId = d.StockInwardId,
                    InwardDate = d.StockInward!.InwardDate,
                    SupplierName = d.StockInward.Supplier!.Name,
                    InvoiceNo = d.StockInward.InvoiceNo ?? "N/A",
                    ItemCode = d.Item!.Code,
                    ItemName = d.Item.Name,
                    Quantity = d.Quantity,
                    Rate = d.Rate,
                    Amount = d.Amount
                })
                .ToList();
            Console.WriteLine($"Report list count: {reportList.Count}");
            foreach (var r in reportList.Take(5))
            {
                Console.WriteLine($"Row Id: {r.Id}, StockInwardId: {r.StockInwardId}, InvoiceNo: {r.InvoiceNo}, SupplierName: {r.SupplierName}");
            }
        }
        catch (Exception indexEx)
        {
            Console.WriteLine($"Warning: Failed to adjust database unique index constraints: {indexEx.Message}");
        }

        DbInitializer.Initialize(context);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while creating/seeding the database.");
    }
}

app.Run();

// Current User Service Implementation
public class HttpContextCurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public HttpContextCurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId
    {
        get
        {
            var user = _httpContextAccessor.HttpContext?.User;
            if (user == null) return Guid.Empty;

            var subClaim = user.FindFirst(ClaimTypes.NameIdentifier)?.Value 
                           ?? user.FindFirst("sub")?.Value;

            if (Guid.TryParse(subClaim, out var userId))
            {
                return userId;
            }

            return Guid.Empty;
        }
    }
}
