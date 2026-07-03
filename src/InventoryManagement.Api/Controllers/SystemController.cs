using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Api.Data;

namespace InventoryManagement.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [AllowAnonymous]
    public class SystemController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public SystemController(InventoryDbContext context)
        {
            _context = context;
        }

        [HttpPost("reset-transactions")]
        public async Task<IActionResult> ResetTransactions([FromQuery] string secret)
        {
            if (secret != "PioconTextileReset2026")
            {
                return Unauthorized("Invalid secret key.");
            }

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // Delete in order to satisfy foreign keys / relationships
                
                // 1. Delete StockOutwardDetails
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM StockOutwardDetails");
                
                // 2. Delete StockOutwards
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM StockOutwards");
                
                // 3. Delete StockInwardDetails
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM StockInwardDetails");
                
                // 4. Delete StockInwards
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM StockInwards");
                
                // 5. Delete BarcodeMasters
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM BarcodeMasters");
                
                // 6. Delete QRCodeMasters
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM QRCodeMasters");
                
                // 7. Delete StockLedgers
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM StockLedgers");

                // 8. Delete AuditLogs
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM AuditLogs");

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(new { message = "All stock transactional data (inwards, outwards, barcodes, QR codes, ledgers, and audit logs) has been successfully reset. Masters (Items, Categories, Suppliers, Units, UserProfiles) are preserved." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"Error resetting data: {ex.Message}");
            }
        }
    }
}
