using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
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
    public class CollectionsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public CollectionsController(InventoryDbContext context)
        {
            _context = context;
        }

        [HttpGet]
        public async Task<IActionResult> GetCollections([FromQuery] Guid? firmId, [FromQuery] Guid? customerId, [FromQuery] string? search)
        {
            var query = _context.CustomerCollections.AsQueryable();

            if (firmId.HasValue)
            {
                query = query.Where(cc => cc.FirmId == firmId.Value);
            }

            if (customerId.HasValue)
            {
                query = query.Where(cc => cc.CustomerId == customerId.Value);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var lowerSearch = search.ToLower();
                query = query.Where(cc => cc.CollectionNo.ToLower().Contains(lowerSearch) || 
                                         cc.CustomerName.ToLower().Contains(lowerSearch) ||
                                         cc.ReferenceNo.ToLower().Contains(lowerSearch));
            }

            var data = await query.OrderByDescending(cc => cc.CollectionDate).ToListAsync();
            return Ok(data);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetCollection(Guid id)
        {
            var collection = await _context.CustomerCollections.FindAsync(id);
            if (collection == null) return NotFound();
            return Ok(collection);
        }

        [HttpPost]
        public async Task<IActionResult> CreateCollection([FromBody] CustomerCollection collection)
        {
            if (collection == null) return BadRequest("Invalid collection data.");

            // Get customer details
            var customer = await _context.Customers.FindAsync(collection.CustomerId);
            if (customer == null) return BadRequest("Customer not found.");

            // Get current user id
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            Guid.TryParse(userIdClaim, out var userId);

            collection.CollectionId = Guid.NewGuid();
            collection.CollectionNo = await GenerateCollectionNoAsync();
            collection.CustomerName = customer.CustomerName;
            collection.FirmId = customer.FirmId;
            collection.FirmCode = customer.FirmCode;
            collection.FirmName = customer.FirmName;
            collection.CreatedBy = userId;
            collection.CreatedAt = DateTimeOffset.UtcNow;

            _context.CustomerCollections.Add(collection);
            await _context.SaveChangesAsync();

            return Ok(collection);
        }

        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateCollection(Guid id, [FromBody] CustomerCollection collection)
        {
            var entity = await _context.CustomerCollections.FindAsync(id);
            if (entity == null) return NotFound();

            entity.CollectionDate = collection.CollectionDate;
            entity.Amount = collection.Amount;
            entity.PaymentMode = collection.PaymentMode;
            entity.ReferenceNo = collection.ReferenceNo?.Trim();
            entity.Remarks = collection.Remarks?.Trim();

            _context.CustomerCollections.Update(entity);
            await _context.SaveChangesAsync();

            return Ok(entity);
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteCollection(Guid id)
        {
            var entity = await _context.CustomerCollections.FindAsync(id);
            if (entity == null) return NotFound();

            _context.CustomerCollections.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        private async Task<string> GenerateCollectionNoAsync()
        {
            var lastRecord = await _context.CustomerCollections
                .OrderByDescending(cc => cc.CollectionNo)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastRecord != null)
            {
                var codePart = lastRecord.CollectionNo.Replace("COL-", "");
                if (int.TryParse(codePart, out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"COL-{nextNum:D6}";
        }
    }
}
