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
                     ?? Environment.GetEnvironmentVariable("SUPABASE_SERVICEROLEKEY")
                     ?? Environment.GetEnvironmentVariable("Supabase__ServiceRoleKey");
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
// Ensure wwwroot/uploads folders exist dynamically to prevent 404 on local fallback uploads
var wwwrootPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
if (!Directory.Exists(wwwrootPath))
{
    Directory.CreateDirectory(wwwrootPath);
}
var uploadsPath = Path.Combine(wwwrootPath, "uploads");
if (!Directory.Exists(uploadsPath))
{
    Directory.CreateDirectory(uploadsPath);
}

app.UseStaticFiles(); // Serve default WebRootPath if configured
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(wwwrootPath),
    RequestPath = ""
});
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
        // Run raw SQL migrations for Proforma Invoices and GSTPercent column
        try
        {
            var isSqlite = context.Database.ProviderName?.Contains("Sqlite") == true;
            if (isSqlite)
            {
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""ProformaInvoices"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""ProformaNo"" TEXT NOT NULL,
                        ""ProformaDate"" TEXT NOT NULL,
                        ""CustomerName"" TEXT NOT NULL,
                        ""MobileNo"" TEXT NULL,
                        ""Address"" TEXT NULL,
                        ""GSTIN"" TEXT NULL,
                        ""State"" TEXT NULL,
                        ""TaxType"" TEXT NOT NULL DEFAULT 'Intra-State',
                        ""TotalQty"" TEXT NOT NULL,
                        ""TotalTaxableValue"" TEXT NOT NULL,
                        ""TotalCGST"" TEXT NOT NULL,
                        ""TotalSGST"" TEXT NOT NULL,
                        ""TotalIGST"" TEXT NOT NULL,
                        ""GrandTotal"" TEXT NOT NULL,
                        ""RoundOff"" TEXT NOT NULL,
                        ""NetAmount"" TEXT NOT NULL,
                        ""IsConverted"" INTEGER NOT NULL DEFAULT 0,
                        ""ConvertedDate"" TEXT NULL,
                        ""ConvertedStockOutwardId"" TEXT NULL,
                        ""CreatedBy"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""ProformaInvoiceDetails"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""ProformaInvoiceId"" TEXT NOT NULL REFERENCES ""ProformaInvoices""(""Id"") ON DELETE CASCADE,
                        ""ItemId"" TEXT NOT NULL REFERENCES ""Items""(""Id""),
                        ""Particulars"" TEXT NOT NULL,
                        ""HSNCode"" TEXT NULL,
                        ""Quantity"" TEXT NOT NULL,
                        ""Rate"" TEXT NOT NULL,
                        ""DiscountPercent"" TEXT NOT NULL,
                        ""DiscountAmount"" TEXT NOT NULL,
                        ""TaxableValue"" TEXT NOT NULL,
                        ""GSTPercent"" TEXT NOT NULL,
                        ""GSTAmount"" TEXT NOT NULL,
                        ""LineTotal"" TEXT NOT NULL,
                        ""BarcodeList"" TEXT NOT NULL DEFAULT ''
                    );
                ");

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""ProformaInvoiceDetailBarcodes"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""ProformaInvoiceDetailId"" TEXT NOT NULL REFERENCES ""ProformaInvoiceDetails""(""Id"") ON DELETE CASCADE,
                        ""Barcode"" TEXT NOT NULL,
                        ""BatchNo"" TEXT NOT NULL,
                        ""TrackingNo"" TEXT NOT NULL,
                        ""Quantity"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""CustomerMaster"" (
                        ""CustomerId"" TEXT NOT NULL PRIMARY KEY,
                        ""CustomerCode"" TEXT NOT NULL,
                        ""CustomerName"" TEXT NOT NULL,
                        ""ContactPerson"" TEXT NULL,
                        ""MobileNo"" TEXT NOT NULL,
                        ""WhatsappNo"" TEXT NULL,
                        ""Email"" TEXT NULL,
                        ""GSTIN"" TEXT NULL,
                        ""PANNo"" TEXT NULL,
                        ""Address1"" TEXT NULL,
                        ""Address2"" TEXT NULL,
                        ""City"" TEXT NULL,
                        ""State"" TEXT NULL,
                        ""Pincode"" TEXT NULL,
                        ""Country"" TEXT NULL,
                        ""CustomerType"" TEXT NOT NULL DEFAULT 'Unregistered',
                        ""CreditDays"" INTEGER NOT NULL DEFAULT 0,
                        ""CreditLimit"" TEXT NOT NULL DEFAULT '0',
                        ""Status"" TEXT NOT NULL DEFAULT 'Active',
                        ""Remarks"" TEXT NULL,
                        ""CreatedDate"" TEXT NOT NULL,
                        ""ModifiedDate"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomerMaster_MobileNo"" ON ""CustomerMaster"" (""MobileNo"");");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomerMaster_CustomerCode"" ON ""CustomerMaster"" (""CustomerCode"");");
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""CustomerId"" TEXT NULL REFERENCES ""CustomerMaster""(""CustomerId"") ON DELETE SET NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""GSTIN"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""State"" TEXT NULL;"); } catch {}

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""FirmMaster"" (
                        ""FirmId"" TEXT NOT NULL PRIMARY KEY,
                        ""FirmCode"" TEXT NOT NULL,
                        ""FirmName"" TEXT NOT NULL,
                        ""ContactPerson"" TEXT NULL,
                        ""MobileNo"" TEXT NULL,
                        ""Email"" TEXT NULL,
                        ""GSTIN"" TEXT NULL,
                        ""PANNo"" TEXT NULL,
                        ""Address1"" TEXT NULL,
                        ""Address2"" TEXT NULL,
                        ""City"" TEXT NULL,
                        ""State"" TEXT NULL,
                        ""Pincode"" TEXT NULL,
                        ""Country"" TEXT NULL,
                        ""Status"" TEXT NOT NULL DEFAULT 'Active',
                        ""Remarks"" TEXT NULL,
                        ""CreatedDate"" TEXT NOT NULL,
                        ""ModifiedDate"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FirmMaster_FirmCode"" ON ""FirmMaster"" (""FirmCode"");");

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""CustomerCollections"" (
                        ""CollectionId"" TEXT NOT NULL PRIMARY KEY,
                        ""CustomerId"" TEXT NOT NULL REFERENCES ""CustomerMaster""(""CustomerId"") ON DELETE CASCADE,
                        ""CustomerName"" TEXT NOT NULL,
                        ""FirmId"" TEXT NOT NULL REFERENCES ""FirmMaster""(""FirmId"") ON DELETE RESTRICT,
                        ""FirmCode"" TEXT NOT NULL,
                        ""FirmName"" TEXT NOT NULL,
                        ""CollectionNo"" TEXT NOT NULL,
                        ""CollectionDate"" TEXT NOT NULL,
                        ""Amount"" TEXT NOT NULL,
                        ""PaymentMode"" TEXT NOT NULL,
                        ""ReferenceNo"" TEXT NULL,
                        ""Remarks"" TEXT NULL,
                        ""CreatedBy"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomerCollections_CollectionNo"" ON ""CustomerCollections"" (""CollectionNo"");");

                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""CustomerMaster"" ADD COLUMN ""FirmId"" TEXT NULL REFERENCES ""FirmMaster""(""FirmId"") ON DELETE RESTRICT;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""CustomerMaster"" ADD COLUMN ""FirmCode"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""CustomerMaster"" ADD COLUMN ""FirmName"" TEXT NULL;"); } catch {}

                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""FirmId"" TEXT NULL REFERENCES ""FirmMaster""(""FirmId"") ON DELETE SET NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""FirmCode"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""FirmName"" TEXT NULL;"); } catch {}

                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""GSTPercent"" numeric(5,2) NOT NULL DEFAULT 18.00;"); } catch {}
            }
            else
            {
                // PostgreSQL
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""ProformaInvoices"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""ProformaNo"" varchar(30) NOT NULL,
                        ""ProformaDate"" timestamptz NOT NULL,
                        ""CustomerName"" varchar(150) NOT NULL,
                        ""MobileNo"" varchar(20) NULL,
                        ""Address"" text NULL,
                        ""GSTIN"" varchar(15) NULL,
                        ""State"" varchar(100) NULL,
                        ""TaxType"" varchar(20) NOT NULL DEFAULT 'Intra-State',
                        ""TotalQty"" numeric(12,2) NOT NULL,
                        ""TotalTaxableValue"" numeric(12,2) NOT NULL,
                        ""TotalCGST"" numeric(12,2) NOT NULL,
                        ""TotalSGST"" numeric(12,2) NOT NULL,
                        ""TotalIGST"" numeric(12,2) NOT NULL,
                        ""GrandTotal"" numeric(12,2) NOT NULL,
                        ""RoundOff"" numeric(12,2) NOT NULL,
                        ""NetAmount"" numeric(12,2) NOT NULL,
                        ""IsConverted"" boolean NOT NULL DEFAULT false,
                        ""ConvertedDate"" timestamptz NULL,
                        ""ConvertedStockOutwardId"" uuid NULL,
                        ""CreatedBy"" uuid NOT NULL,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""ProformaInvoiceDetails"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""ProformaInvoiceId"" uuid NOT NULL REFERENCES ""ProformaInvoices""(""Id"") ON DELETE CASCADE,
                        ""ItemId"" uuid NOT NULL REFERENCES ""Items""(""Id""),
                        ""Particulars"" varchar(150) NOT NULL,
                        ""HSNCode"" varchar(20) NULL,
                        ""Quantity"" numeric(12,2) NOT NULL,
                        ""Rate"" numeric(12,4) NOT NULL,
                        ""DiscountPercent"" numeric(5,2) NOT NULL,
                        ""DiscountAmount"" numeric(12,2) NOT NULL,
                        ""TaxableValue"" numeric(12,2) NOT NULL,
                        ""GSTPercent"" numeric(5,2) NOT NULL,
                        ""GSTAmount"" numeric(12,2) NOT NULL,
                        ""LineTotal"" numeric(12,2) NOT NULL,
                        ""BarcodeList"" text NOT NULL DEFAULT ''
                    );
                ");

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""ProformaInvoiceDetailBarcodes"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""ProformaInvoiceDetailId"" uuid NOT NULL REFERENCES ""ProformaInvoiceDetails""(""Id"") ON DELETE CASCADE,
                        ""Barcode"" varchar(50) NOT NULL,
                        ""BatchNo"" varchar(50) NOT NULL,
                        ""TrackingNo"" varchar(30) NOT NULL,
                        ""Quantity"" numeric(12,2) NOT NULL
                    );
                ");

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""CustomerMaster"" (
                        ""CustomerId"" uuid NOT NULL PRIMARY KEY,
                        ""CustomerCode"" varchar(30) NOT NULL,
                        ""CustomerName"" varchar(150) NOT NULL,
                        ""ContactPerson"" varchar(100) NULL,
                        ""MobileNo"" varchar(20) NOT NULL,
                        ""WhatsappNo"" varchar(20) NULL,
                        ""Email"" varchar(100) NULL,
                        ""GSTIN"" varchar(15) NULL,
                        ""PANNo"" varchar(10) NULL,
                        ""Address1"" text NULL,
                        ""Address2"" text NULL,
                        ""City"" varchar(100) NULL,
                        ""State"" varchar(100) NULL,
                        ""Pincode"" varchar(10) NULL,
                        ""Country"" varchar(100) NULL,
                        ""CustomerType"" varchar(20) NOT NULL DEFAULT 'Unregistered',
                        ""CreditDays"" integer NOT NULL DEFAULT 0,
                        ""CreditLimit"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""Status"" varchar(20) NOT NULL DEFAULT 'Active',
                        ""Remarks"" text NULL,
                        ""CreatedDate"" timestamptz NOT NULL,
                        ""ModifiedDate"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomerMaster_MobileNo"" ON ""CustomerMaster"" (""MobileNo"");");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomerMaster_CustomerCode"" ON ""CustomerMaster"" (""CustomerCode"");");
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""CustomerId"" uuid NULL REFERENCES ""CustomerMaster""(""CustomerId"") ON DELETE SET NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""GSTIN"" varchar(15) NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""State"" varchar(100) NULL;"); } catch {}

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""FirmMaster"" (
                        ""FirmId"" uuid NOT NULL PRIMARY KEY,
                        ""FirmCode"" varchar(30) NOT NULL,
                        ""FirmName"" varchar(150) NOT NULL,
                        ""ContactPerson"" varchar(100) NULL,
                        ""MobileNo"" varchar(20) NULL,
                        ""Email"" varchar(100) NULL,
                        ""GSTIN"" varchar(15) NULL,
                        ""PANNo"" varchar(10) NULL,
                        ""Address1"" text NULL,
                        ""Address2"" text NULL,
                        ""City"" varchar(100) NULL,
                        ""State"" varchar(100) NULL,
                        ""Pincode"" varchar(10) NULL,
                        ""Country"" varchar(100) NULL,
                        ""Status"" varchar(20) NOT NULL DEFAULT 'Active',
                        ""Remarks"" text NULL,
                        ""CreatedDate"" timestamptz NOT NULL,
                        ""ModifiedDate"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_FirmMaster_FirmCode"" ON ""FirmMaster"" (""FirmCode"");");

                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""CustomerCollections"" (
                        ""CollectionId"" uuid NOT NULL PRIMARY KEY,
                        ""CustomerId"" uuid NOT NULL REFERENCES ""CustomerMaster""(""CustomerId"") ON DELETE CASCADE,
                        ""CustomerName"" varchar(150) NOT NULL,
                        ""FirmId"" uuid NOT NULL REFERENCES ""FirmMaster""(""FirmId"") ON DELETE RESTRICT,
                        ""FirmCode"" varchar(30) NOT NULL,
                        ""FirmName"" varchar(150) NOT NULL,
                        ""CollectionNo"" varchar(30) NOT NULL,
                        ""CollectionDate"" timestamptz NOT NULL,
                        ""Amount"" numeric(12,2) NOT NULL,
                        ""PaymentMode"" varchar(20) NOT NULL,
                        ""ReferenceNo"" varchar(50) NULL,
                        ""Remarks"" text NULL,
                        ""CreatedBy"" uuid NOT NULL,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_CustomerCollections_CollectionNo"" ON ""CustomerCollections"" (""CollectionNo"");");

                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""CustomerMaster"" ADD COLUMN ""FirmId"" uuid NULL REFERENCES ""FirmMaster""(""FirmId"") ON DELETE RESTRICT;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""CustomerMaster"" ADD COLUMN ""FirmCode"" varchar(30) NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""CustomerMaster"" ADD COLUMN ""FirmName"" varchar(150) NULL;"); } catch {}

                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""FirmId"" uuid NULL REFERENCES ""FirmMaster""(""FirmId"") ON DELETE SET NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""FirmCode"" varchar(30) NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""ProformaInvoices"" ADD COLUMN ""FirmName"" varchar(150) NULL;"); } catch {}

                context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN IF NOT EXISTS ""GSTPercent"" numeric(5,2) NOT NULL DEFAULT 18.00;");
            }
            Console.WriteLine("Proforma Invoice tables and GSTPercent column verified successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to run custom Proforma migrations: {ex.Message}");
        }

        // Run Job Work custom migrations
        try
        {
            var isSqlite = context.Database.ProviderName?.Contains("Sqlite") == true;
            if (isSqlite)
            {
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""JobWorkMaster"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""Name"" TEXT NOT NULL,
                        ""Type"" TEXT NOT NULL,
                        ""Address"" TEXT NULL,
                        ""Mobile"" TEXT NULL,
                        ""GSTIN"" TEXT NULL,
                        ""LedgerAccount"" TEXT NULL,
                        ""Active"" INTEGER NOT NULL DEFAULT 1,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""LoomMaster"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""LoomNo"" TEXT NOT NULL,
                        ""WeaverId"" TEXT NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE RESTRICT,
                        ""Active"" INTEGER NOT NULL DEFAULT 1,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""LoomAllocation"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""LoomId"" TEXT NOT NULL REFERENCES ""LoomMaster""(""Id"") ON DELETE CASCADE,
                        ""ItemId"" TEXT NOT NULL REFERENCES ""Items""(""Id"") ON DELETE RESTRICT,
                        ""SubWeaver"" TEXT NULL,
                        ""WarpRefNo"" TEXT NULL,
                        ""StartDate"" TEXT NOT NULL,
                        ""Active"" INTEGER NOT NULL DEFAULT 1,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingIssues"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""IssueNo"" TEXT NOT NULL,
                        ""IssueDate"" TEXT NOT NULL,
                        ""DyerId"" TEXT NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE RESTRICT,
                        ""Narration"" TEXT NULL,
                        ""CreatedBy"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingIssueDetails"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""DyeingIssueId"" TEXT NOT NULL REFERENCES ""DyeingIssues""(""Id"") ON DELETE CASCADE,
                        ""DesignId"" TEXT NOT NULL REFERENCES ""Items""(""Id"") ON DELETE RESTRICT,
                        ""YarnType"" TEXT NOT NULL,
                        ""WarpYarn"" TEXT NULL,
                        ""WeftYarn"" TEXT NULL,
                        ""Color"" TEXT NULL,
                        ""Qty"" TEXT NOT NULL,
                        ""WeightKgs"" TEXT NOT NULL,
                        ""Rate"" TEXT NOT NULL,
                        ""Amount"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingReceives"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""ReceiveNo"" TEXT NOT NULL,
                        ""ReceiveDate"" TEXT NOT NULL,
                        ""DyerId"" TEXT NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE RESTRICT,
                        ""IssueReferenceNo"" TEXT NULL,
                        ""CreatedBy"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingReceiveDetails"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""DyeingReceiveId"" TEXT NOT NULL REFERENCES ""DyeingReceives""(""Id"") ON DELETE CASCADE,
                        ""DesignId"" TEXT NOT NULL REFERENCES ""Items""(""Id"") ON DELETE RESTRICT,
                        ""YarnType"" TEXT NOT NULL,
                        ""DyedColor"" TEXT NULL,
                        ""QtyReceived"" TEXT NOT NULL,
                        ""WeightReceived"" TEXT NOT NULL,
                        ""WasteWeight"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""WeavingLedger"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""LoomAllocationId"" TEXT NOT NULL REFERENCES ""LoomAllocation""(""Id"") ON DELETE CASCADE,
                        ""Date"" TEXT NOT NULL,
                        ""EntryType"" TEXT NOT NULL,
                        ""Details"" TEXT NULL,
                        ""WarpQty"" TEXT NOT NULL DEFAULT '0',
                        ""IssuedWt"" TEXT NOT NULL DEFAULT '0',
                        ""RodQty"" TEXT NOT NULL DEFAULT '0',
                        ""RodWt"" TEXT NOT NULL DEFAULT '0',
                        ""Debit"" TEXT NOT NULL DEFAULT '0',
                        ""Credit"" TEXT NOT NULL DEFAULT '0',
                        ""Narration"" TEXT NULL,
                        ""Status"" TEXT NOT NULL DEFAULT 'S',
                        ""CreatedBy"" TEXT NOT NULL,
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""JobLedger"" (
                        ""Id"" TEXT NOT NULL PRIMARY KEY,
                        ""JobWorkerId"" TEXT NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE CASCADE,
                        ""TransactionDate"" TEXT NOT NULL,
                        ""VoucherNo"" TEXT NOT NULL,
                        ""Particulars"" TEXT NOT NULL,
                        ""IssueQty"" TEXT NOT NULL DEFAULT '0',
                        ""ReceiveQty"" TEXT NOT NULL DEFAULT '0',
                        ""IssueWeight"" TEXT NOT NULL DEFAULT '0',
                        ""ReceiveWeight"" TEXT NOT NULL DEFAULT '0',
                        ""Debit"" TEXT NOT NULL DEFAULT '0',
                        ""Credit"" TEXT NOT NULL DEFAULT '0',
                        ""Balance"" TEXT NOT NULL DEFAULT '0',
                        ""CreatedAt"" TEXT NOT NULL
                    );
                ");

                // Alter Items Table
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WarpType"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WeftType"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""Wages"" TEXT NOT NULL DEFAULT '0';"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WarpWeight"" TEXT NOT NULL DEFAULT '0';"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WeftWeight"" TEXT NOT NULL DEFAULT '0';"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""ZariWeight"" TEXT NOT NULL DEFAULT '0';"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""TotalWeight"" TEXT NOT NULL DEFAULT '0';"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""Reed"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""Thread"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""NoOfCards"" INTEGER NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""NoOfMarks"" INTEGER NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""BodyImage"" TEXT NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""PalluImage"" TEXT NULL;"); } catch {}

                // Alter StockLedger Table
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""StockLedger"" ADD COLUMN ""InwardWeight"" TEXT NOT NULL DEFAULT '0';"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""StockLedger"" ADD COLUMN ""OutwardWeight"" TEXT NOT NULL DEFAULT '0';"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""StockLedger"" ADD COLUMN ""BalanceWeight"" TEXT NOT NULL DEFAULT '0';"); } catch {}
            }
            else
            {
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""JobWorkMaster"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""Name"" varchar(150) NOT NULL,
                        ""Type"" varchar(50) NOT NULL,
                        ""Address"" text NULL,
                        ""Mobile"" varchar(20) NULL,
                        ""GSTIN"" varchar(15) NULL,
                        ""LedgerAccount"" varchar(100) NULL,
                        ""Active"" boolean NOT NULL DEFAULT true,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""LoomMaster"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""LoomNo"" varchar(50) NOT NULL,
                        ""WeaverId"" uuid NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE RESTRICT,
                        ""Active"" boolean NOT NULL DEFAULT true,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""LoomAllocation"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""LoomId"" uuid NOT NULL REFERENCES ""LoomMaster""(""Id"") ON DELETE CASCADE,
                        ""ItemId"" uuid NOT NULL REFERENCES ""Items""(""Id"") ON DELETE RESTRICT,
                        ""SubWeaver"" varchar(100) NULL,
                        ""WarpRefNo"" varchar(50) NULL,
                        ""StartDate"" timestamptz NOT NULL,
                        ""Active"" boolean NOT NULL DEFAULT true,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingIssues"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""IssueNo"" varchar(50) NOT NULL,
                        ""IssueDate"" timestamptz NOT NULL,
                        ""DyerId"" uuid NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE RESTRICT,
                        ""Narration"" text NULL,
                        ""CreatedBy"" uuid NOT NULL,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingIssueDetails"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""DyeingIssueId"" uuid NOT NULL REFERENCES ""DyeingIssues""(""Id"") ON DELETE CASCADE,
                        ""DesignId"" uuid NOT NULL REFERENCES ""Items""(""Id"") ON DELETE RESTRICT,
                        ""YarnType"" varchar(20) NOT NULL,
                        ""WarpYarn"" varchar(100) NULL,
                        ""WeftYarn"" varchar(100) NULL,
                        ""Color"" varchar(50) NULL,
                        ""Qty"" numeric(12,2) NOT NULL,
                        ""WeightKgs"" numeric(12,3) NOT NULL,
                        ""Rate"" numeric(12,4) NOT NULL,
                        ""Amount"" numeric(12,2) NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingReceives"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""ReceiveNo"" varchar(50) NOT NULL,
                        ""ReceiveDate"" timestamptz NOT NULL,
                        ""DyerId"" uuid NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE RESTRICT,
                        ""IssueReferenceNo"" varchar(50) NULL,
                        ""CreatedBy"" uuid NOT NULL,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""DyeingReceiveDetails"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""DyeingReceiveId"" uuid NOT NULL REFERENCES ""DyeingReceives""(""Id"") ON DELETE CASCADE,
                        ""DesignId"" uuid NOT NULL REFERENCES ""Items""(""Id"") ON DELETE RESTRICT,
                        ""YarnType"" varchar(20) NOT NULL,
                        ""DyedColor"" varchar(50) NULL,
                        ""QtyReceived"" numeric(12,2) NOT NULL,
                        ""WeightReceived"" numeric(12,3) NOT NULL,
                        ""WasteWeight"" numeric(12,3) NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""WeavingLedger"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""LoomAllocationId"" uuid NOT NULL REFERENCES ""LoomAllocation""(""Id"") ON DELETE CASCADE,
                        ""Date"" timestamptz NOT NULL,
                        ""EntryType"" varchar(50) NOT NULL,
                        ""Details"" varchar(250) NULL,
                        ""WarpQty"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""IssuedWt"" numeric(12,3) NOT NULL DEFAULT 0,
                        ""RodQty"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""RodWt"" numeric(12,3) NOT NULL DEFAULT 0,
                        ""Debit"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""Credit"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""Narration"" varchar(500) NULL,
                        ""Status"" varchar(10) NOT NULL DEFAULT 'S',
                        ""CreatedBy"" uuid NOT NULL,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");
                context.Database.ExecuteSqlRaw(@"
                    CREATE TABLE IF NOT EXISTS ""JobLedger"" (
                        ""Id"" uuid NOT NULL PRIMARY KEY,
                        ""JobWorkerId"" uuid NOT NULL REFERENCES ""JobWorkMaster""(""Id"") ON DELETE CASCADE,
                        ""TransactionDate"" timestamptz NOT NULL,
                        ""VoucherNo"" varchar(50) NOT NULL,
                        ""Particulars"" varchar(250) NOT NULL,
                        ""IssueQty"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""ReceiveQty"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""IssueWeight"" numeric(12,3) NOT NULL DEFAULT 0,
                        ""ReceiveWeight"" numeric(12,3) NOT NULL DEFAULT 0,
                        ""Debit"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""Credit"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""Balance"" numeric(12,2) NOT NULL DEFAULT 0,
                        ""CreatedAt"" timestamptz NOT NULL
                    );
                ");

                // Alter Items Table
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WarpType"" varchar(100) NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WeftType"" varchar(100) NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""Wages"" numeric(12,2) NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WarpWeight"" numeric(12,3) NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""WeftWeight"" numeric(12,3) NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""ZariWeight"" numeric(12,3) NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""TotalWeight"" numeric(12,3) NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""Reed"" varchar(50) NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""Thread"" varchar(50) NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""NoOfCards"" integer NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""NoOfMarks"" integer NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""BodyImage"" text NULL;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""Items"" ADD COLUMN ""PalluImage"" text NULL;"); } catch {}

                // Alter StockLedger Table
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""StockLedger"" ADD COLUMN ""InwardWeight"" numeric(12,3) NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""StockLedger"" ADD COLUMN ""OutwardWeight"" numeric(12,3) NOT NULL DEFAULT 0;"); } catch {}
                try { context.Database.ExecuteSqlRaw(@"ALTER TABLE ""StockLedger"" ADD COLUMN ""BalanceWeight"" numeric(12,3) NOT NULL DEFAULT 0;"); } catch {}
            }
            Console.WriteLine("Job Work database tables and schema verified successfully.");
        }
        catch (Exception jwEx)
        {
            Console.WriteLine($"Warning: Failed to run custom Job Work migrations: {jwEx.Message}");
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
