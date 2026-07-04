using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Api.Data;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class FirmsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public FirmsController(InventoryDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetFirms([FromQuery] string? search, [FromQuery] string? status)
        {
            var query = _context.Firms.AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(f => f.FirmName.ToLower().Contains(lowerSearch) || 
                                         f.FirmCode.ToLower().Contains(lowerSearch) ||
                                         f.GSTIN.ToLower().Contains(lowerSearch));
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                query = query.Where(f => f.Status == status);
            }

            var data = await query.OrderBy(f => f.FirmName).ToListAsync();
            return Ok(data);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetFirm(Guid id)
        {
            var firm = await _context.Firms.FindAsync(id);
            if (firm == null) return NotFound();
            return Ok(firm);
        }

        [HttpGet("check-duplicate-name")]
        public async Task<IActionResult> CheckDuplicateName([FromQuery] string name, [FromQuery] Guid? excludeId)
        {
            if (string.IsNullOrWhiteSpace(name)) return Ok(false);
            
            var query = _context.Firms.Where(f => f.FirmName.ToLower() == name.Trim().ToLower());
            if (excludeId.HasValue)
            {
                query = query.Where(f => f.FirmId != excludeId.Value);
            }

            var exists = await query.AnyAsync();
            return Ok(exists);
        }

        [HttpPost]
        public async Task<IActionResult> CreateFirm([FromBody] Firm firm)
        {
            if (firm == null) return BadRequest("Invalid firm data.");

            // 1. Validations
            if (string.IsNullOrWhiteSpace(firm.FirmName))
            {
                return BadRequest("Firm Name is mandatory.");
            }

            // Email validation
            if (!string.IsNullOrWhiteSpace(firm.Email))
            {
                var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                if (!emailRegex.IsMatch(firm.Email.Trim()))
                {
                    return BadRequest("Invalid Email ID format.");
                }
            }

            // GSTIN validation if entered
            if (!string.IsNullOrWhiteSpace(firm.GSTIN) && firm.GSTIN.Trim().Length != 15)
            {
                return BadRequest("GSTIN must be exactly 15 characters long.");
            }

            // 2. Generate Firm Code
            firm.FirmId = Guid.NewGuid();
            firm.FirmCode = await GenerateFirmCodeAsync();
            firm.FirmName = firm.FirmName.Trim();
            if (firm.GSTIN != null) firm.GSTIN = firm.GSTIN.Trim().ToUpper();
            if (firm.PANNo != null) firm.PANNo = firm.PANNo.Trim().ToUpper();
            firm.CreatedDate = DateTimeOffset.UtcNow;
            firm.ModifiedDate = DateTimeOffset.UtcNow;

            _context.Firms.Add(firm);
            await _context.SaveChangesAsync();

            return Ok(firm);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateFirm(Guid id, [FromBody] Firm firm)
        {
            var entity = await _context.Firms.FindAsync(id);
            if (entity == null) return NotFound();

            // 1. Validations
            if (string.IsNullOrWhiteSpace(firm.FirmName))
            {
                return BadRequest("Firm Name is mandatory.");
            }

            // Email validation
            if (!string.IsNullOrWhiteSpace(firm.Email))
            {
                var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                if (!emailRegex.IsMatch(firm.Email.Trim()))
                {
                    return BadRequest("Invalid Email ID format.");
                }
            }

            // GSTIN validation if entered
            if (!string.IsNullOrWhiteSpace(firm.GSTIN) && firm.GSTIN.Trim().Length != 15)
            {
                return BadRequest("GSTIN must be exactly 15 characters long.");
            }

            // 2. Update properties
            entity.FirmName = firm.FirmName.Trim();
            entity.ContactPerson = firm.ContactPerson?.Trim();
            entity.MobileNo = firm.MobileNo?.Trim();
            entity.Email = firm.Email?.Trim();
            entity.GSTIN = firm.GSTIN?.Trim().ToUpper();
            entity.PANNo = firm.PANNo?.Trim().ToUpper();
            entity.Address1 = firm.Address1?.Trim();
            entity.Address2 = firm.Address2?.Trim();
            entity.City = firm.City?.Trim();
            entity.State = firm.State?.Trim();
            entity.Pincode = firm.Pincode?.Trim();
            entity.Country = firm.Country?.Trim();
            entity.Status = firm.Status;
            entity.Remarks = firm.Remarks?.Trim();
            entity.ModifiedDate = DateTimeOffset.UtcNow;

            // Cascade updates to CustomerMaster (to sync FirmName & FirmCode)
            var linkedCustomers = await _context.Customers.Where(c => c.FirmId == id).ToListAsync();
            foreach (var cust in linkedCustomers)
            {
                cust.FirmCode = entity.FirmCode;
                cust.FirmName = entity.FirmName;
                cust.ModifiedDate = DateTimeOffset.UtcNow;
            }

            // Cascade updates to ProformaInvoices (to sync FirmName & FirmCode in snapshots)
            var linkedInvoices = await _context.ProformaInvoices.Where(p => p.FirmId == id).ToListAsync();
            foreach (var inv in linkedInvoices)
            {
                inv.FirmCode = entity.FirmCode;
                inv.FirmName = entity.FirmName;
            }

            _context.Firms.Update(entity);
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteFirm(Guid id)
        {
            var entity = await _context.Firms.FindAsync(id);
            if (entity == null) return NotFound();

            // Block if customers are linked
            if (await _context.Customers.AnyAsync(c => c.FirmId == id))
            {
                return BadRequest("Cannot delete firm. Customer profiles are linked to this firm.");
            }

            _context.Firms.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        private async Task<string> GenerateFirmCodeAsync()
        {
            var lastRecord = await _context.Firms
                .OrderByDescending(f => f.FirmCode)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastRecord != null)
            {
                var codePart = lastRecord.FirmCode.Replace("F", "");
                if (int.TryParse(codePart, out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"F{nextNum:D3}"; // e.g. F001, F002
        }
    }
}
