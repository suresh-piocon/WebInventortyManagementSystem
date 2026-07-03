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
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"StockOutwardDetails\"");
                
                // 2. Delete StockOutwards (is StockOutward singular)
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"StockOutward\"");
                
                // 3. Delete StockInwardDetails
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"StockInwardDetails\"");
                
                // 4. Delete StockInwards (is StockInward singular)
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"StockInward\"");
                
                // 5. Delete BarcodeMasters (is BarcodeMaster singular)
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"BarcodeMaster\"");
                
                // 6. Delete QRCodeMasters (is QRCodeMaster singular)
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"QRCodeMaster\"");
                
                // 7. Delete StockLedgers (is StockLedger singular)
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"StockLedger\"");

                // 8. Delete AuditLogs
                await _context.Database.ExecuteSqlRawAsync("DELETE FROM \"AuditLogs\"");

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
