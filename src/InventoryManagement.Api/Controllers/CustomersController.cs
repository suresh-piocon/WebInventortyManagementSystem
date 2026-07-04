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
    public class CustomersController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public CustomersController(InventoryDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetCustomers([FromQuery] string? search, [FromQuery] string? city, [FromQuery] string? state, [FromQuery] string? type, [FromQuery] Guid? firmId)
        {
            var query = _context.Customers.AsQueryable();

            if (firmId.HasValue)
            {
                query = query.Where(c => c.FirmId == firmId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(c => c.CustomerName.ToLower().Contains(lowerSearch) || 
                                         c.CustomerCode.ToLower().Contains(lowerSearch) || 
                                         c.MobileNo.Contains(search));
            }

            if (!string.IsNullOrWhiteSpace(city))
            {
                query = query.Where(c => c.City == city);
            }

            if (!string.IsNullOrWhiteSpace(state))
            {
                query = query.Where(c => c.State == state);
            }

            if (!string.IsNullOrWhiteSpace(type))
            {
                query = query.Where(c => c.CustomerType == type);
            }

            var data = await query.OrderBy(c => c.CustomerName).ToListAsync();
            return Ok(data);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCustomer(Guid id)
        {
            var customer = await _context.Customers.FindAsync(id);
            if (customer == null) return NotFound();
            return Ok(customer);
        }

        [HttpGet("check-duplicate-name")]
        public async Task<IActionResult> CheckDuplicateName([FromQuery] string name, [FromQuery] Guid? excludeId)
        {
            if (string.IsNullOrWhiteSpace(name)) return Ok(false);
            
            var query = _context.Customers.Where(c => c.CustomerName.ToLower() == name.Trim().ToLower());
            if (excludeId.HasValue)
            {
                query = query.Where(c => c.CustomerId != excludeId.Value);
            }

            var exists = await query.AnyAsync();
            return Ok(exists);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCustomer([FromBody] Customer customer)
        {
            if (customer == null) return BadRequest("Invalid customer data.");

            // Firm selection is mandatory
            if (customer.FirmId == Guid.Empty)
            {
                return BadRequest("Firm selection is mandatory.");
            }

            var firm = await _context.Firms.FindAsync(customer.FirmId);
            if (firm == null)
            {
                return BadRequest("Selected Firm not found.");
            }

            // 1. Validations
            if (string.IsNullOrWhiteSpace(customer.CustomerName))
            {
                return BadRequest("Customer Name is mandatory.");
            }

            if (string.IsNullOrWhiteSpace(customer.MobileNo))
            {
                return BadRequest("Mobile Number is mandatory.");
            }

            // Mobile uniqueness
            if (await _context.Customers.AnyAsync(c => c.MobileNo == customer.MobileNo.Trim()))
            {
                return BadRequest("Mobile Number must be unique. This number is already registered.");
            }

            // Email validation
            if (!string.IsNullOrWhiteSpace(customer.Email))
            {
                var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                if (!emailRegex.IsMatch(customer.Email.Trim()))
                {
                    return BadRequest("Invalid Email ID format.");
                }
            }

            // GSTIN validation for Registered customers
            if (customer.CustomerType == "Registered")
            {
                if (string.IsNullOrWhiteSpace(customer.GSTIN))
                {
                    return BadRequest("GSTIN is required for Registered customers.");
                }
                if (customer.GSTIN.Trim().Length != 15)
                {
                    return BadRequest("GSTIN must be exactly 15 characters long.");
                }
            }

            // 2. Generate Customer Code
            customer.CustomerId = Guid.NewGuid();
            customer.CustomerCode = await GenerateCustomerCodeAsync();
            customer.FirmCode = firm.FirmCode;
            customer.FirmName = firm.FirmName;
            customer.CustomerName = customer.CustomerName.Trim();
            customer.MobileNo = customer.MobileNo.Trim();
            if (customer.GSTIN != null) customer.GSTIN = customer.GSTIN.Trim().ToUpper();
            if (customer.PANNo != null) customer.PANNo = customer.PANNo.Trim().ToUpper();
            customer.CreatedDate = DateTimeOffset.UtcNow;
            customer.ModifiedDate = DateTimeOffset.UtcNow;

            _context.Customers.Add(customer);
            await _context.SaveChangesAsync();

            return Ok(customer);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCustomer(Guid id, [FromBody] Customer customer)
        {
            var entity = await _context.Customers.FindAsync(id);
            if (entity == null) return NotFound();

            // Firm selection is mandatory
            if (customer.FirmId == Guid.Empty)
            {
                return BadRequest("Firm selection is mandatory.");
            }

            var firm = await _context.Firms.FindAsync(customer.FirmId);
            if (firm == null)
            {
                return BadRequest("Selected Firm not found.");
            }

            // 1. Validations
            if (string.IsNullOrWhiteSpace(customer.CustomerName))
            {
                return BadRequest("Customer Name is mandatory.");
            }

            if (string.IsNullOrWhiteSpace(customer.MobileNo))
            {
                return BadRequest("Mobile Number is mandatory.");
            }

            // Mobile uniqueness
            if (await _context.Customers.AnyAsync(c => c.MobileNo == customer.MobileNo.Trim() && c.CustomerId != id))
            {
                return BadRequest("Mobile Number must be unique. This number is already registered to another customer.");
            }

            // Email validation
            if (!string.IsNullOrWhiteSpace(customer.Email))
            {
                var emailRegex = new Regex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$");
                if (!emailRegex.IsMatch(customer.Email.Trim()))
                {
                    return BadRequest("Invalid Email ID format.");
                }
            }

            // GSTIN validation for Registered customers
            if (customer.CustomerType == "Registered")
            {
                if (string.IsNullOrWhiteSpace(customer.GSTIN))
                {
                    return BadRequest("GSTIN is required for Registered customers.");
                }
                if (customer.GSTIN.Trim().Length != 15)
                {
                    return BadRequest("GSTIN must be exactly 15 characters long.");
                }
            }

            // 2. Update properties
            entity.FirmId = customer.FirmId;
            entity.FirmCode = firm.FirmCode;
            entity.FirmName = firm.FirmName;
            entity.CustomerName = customer.CustomerName.Trim();
            entity.ContactPerson = customer.ContactPerson?.Trim();
            entity.MobileNo = customer.MobileNo.Trim();
            entity.WhatsappNo = customer.WhatsappNo?.Trim();
            entity.Email = customer.Email?.Trim();
            entity.GSTIN = customer.GSTIN?.Trim().ToUpper();
            entity.PANNo = customer.PANNo?.Trim().ToUpper();
            entity.Address1 = customer.Address1?.Trim();
            entity.Address2 = customer.Address2?.Trim();
            entity.City = customer.City?.Trim();
            entity.State = customer.State?.Trim();
            entity.Pincode = customer.Pincode?.Trim();
            entity.Country = customer.Country?.Trim();
            entity.CustomerType = customer.CustomerType;
            entity.CreditDays = customer.CreditDays;
            entity.CreditLimit = customer.CreditLimit;
            entity.Status = customer.Status;
            entity.Remarks = customer.Remarks?.Trim();
            entity.ModifiedDate = DateTimeOffset.UtcNow;

            _context.Customers.Update(entity);
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCustomer(Guid id)
        {
            var entity = await _context.Customers.FindAsync(id);
            if (entity == null) return NotFound();

            // Check if referenced by ProformaInvoices
            if (await _context.ProformaInvoices.AnyAsync(p => p.CustomerId == id))
            {
                return BadRequest("Cannot delete customer. Transactions exist for this customer.");
            }

            _context.Customers.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        private async Task<string> GenerateCustomerCodeAsync()
        {
            var lastRecord = await _context.Customers
                .OrderByDescending(c => c.CustomerCode)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastRecord != null)
            {
                var codePart = lastRecord.CustomerCode.Replace("CUST-", "");
                if (int.TryParse(codePart, out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"CUST-{nextNum:D6}";
        }
    }
}
