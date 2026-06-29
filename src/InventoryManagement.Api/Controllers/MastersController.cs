using System;
using System.Linq;
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
    public class MastersController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public MastersController(InventoryDbContext context)
        {
            _context = context;
        }

        // ==========================================
        // CATEGORY MASTER
        // ==========================================

        [HttpGet("categories")]
        public async Task<IActionResult> GetCategories()
        {
            var data = await _context.Categories.OrderBy(c => c.Name).ToListAsync();
            return Ok(data);
        }

        [HttpPost("categories")]
        public async Task<IActionResult> CreateCategory([FromBody] Category category)
        {
            if (await _context.Categories.AnyAsync(c => c.Name.ToLower() == category.Name.ToLower()))
            {
                return BadRequest("Category with this name already exists.");
            }
            category.Id = Guid.NewGuid();
            category.CreatedAt = DateTimeOffset.UtcNow;
            _context.Categories.Add(category);
            await _context.SaveChangesAsync();
            return Ok(category);
        }

        [HttpPut("categories/{id}")]
        public async Task<IActionResult> UpdateCategory(Guid id, [FromBody] Category category)
        {
            var entity = await _context.Categories.FindAsync(id);
            if (entity == null) return NotFound();

            if (await _context.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == category.Name.ToLower()))
            {
                return BadRequest("Category with this name already exists.");
            }

            entity.Name = category.Name;
            _context.Categories.Update(entity);
            await _context.SaveChangesAsync();
            return Ok(entity);
        }

        [HttpDelete("categories/{id}")]
        public async Task<IActionResult> DeleteCategory(Guid id)
        {
            var entity = await _context.Categories.FindAsync(id);
            if (entity == null) return NotFound();

            // Check dependencies
            if (await _context.Items.AnyAsync(i => i.CategoryId == id))
            {
                return BadRequest("Cannot delete category. It is referenced by one or more items.");
            }

            _context.Categories.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ==========================================
        // UNIT MASTER
        // ==========================================

        [HttpGet("units")]
        public async Task<IActionResult> GetUnits()
        {
            var data = await _context.Units.OrderBy(u => u.Code).ToListAsync();
            return Ok(data);
        }

        [HttpPost("units")]
        public async Task<IActionResult> CreateUnit([FromBody] Unit unit)
        {
            if (await _context.Units.AnyAsync(u => u.Code.ToLower() == unit.Code.ToLower()))
            {
                return BadRequest("Unit code already exists.");
            }
            unit.Id = Guid.NewGuid();
            unit.CreatedAt = DateTimeOffset.UtcNow;
            _context.Units.Add(unit);
            await _context.SaveChangesAsync();
            return Ok(unit);
        }

        [HttpPut("units/{id}")]
        public async Task<IActionResult> UpdateUnit(Guid id, [FromBody] Unit unit)
        {
            var entity = await _context.Units.FindAsync(id);
            if (entity == null) return NotFound();

            if (await _context.Units.AnyAsync(u => u.Id != id && u.Code.ToLower() == unit.Code.ToLower()))
            {
                return BadRequest("Unit code already exists.");
            }

            entity.Code = unit.Code;
            entity.Name = unit.Name;
            _context.Units.Update(entity);
            await _context.SaveChangesAsync();
            return Ok(entity);
        }

        [HttpDelete("units/{id}")]
        public async Task<IActionResult> DeleteUnit(Guid id)
        {
            var entity = await _context.Units.FindAsync(id);
            if (entity == null) return NotFound();

            if (await _context.Items.AnyAsync(i => i.UnitId == id))
            {
                return BadRequest("Cannot delete unit. It is referenced by one or more items.");
            }

            _context.Units.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ==========================================
        // SUPPLIER MASTER
        // ==========================================

        [HttpGet("suppliers")]
        public async Task<IActionResult> GetSuppliers()
        {
            var data = await _context.Suppliers.OrderBy(s => s.Name).ToListAsync();
            return Ok(data);
        }

        [HttpPost("suppliers")]
        public async Task<IActionResult> CreateSupplier([FromBody] Supplier supplier)
        {
            if (await _context.Suppliers.AnyAsync(s => s.Code.ToLower() == supplier.Code.ToLower()))
            {
                return BadRequest("Supplier code already exists.");
            }
            supplier.Id = Guid.NewGuid();
            supplier.CreatedAt = DateTimeOffset.UtcNow;
            _context.Suppliers.Add(supplier);
            await _context.SaveChangesAsync();
            return Ok(supplier);
        }

        [HttpPut("suppliers/{id}")]
        public async Task<IActionResult> UpdateSupplier(Guid id, [FromBody] Supplier supplier)
        {
            var entity = await _context.Suppliers.FindAsync(id);
            if (entity == null) return NotFound();

            if (await _context.Suppliers.AnyAsync(s => s.Id != id && s.Code.ToLower() == supplier.Code.ToLower()))
            {
                return BadRequest("Supplier code already exists.");
            }

            entity.Code = supplier.Code;
            entity.Name = supplier.Name;
            entity.ContactPerson = supplier.ContactPerson;
            entity.MobileNo = supplier.MobileNo;
            entity.GSTNo = supplier.GSTNo;
            entity.Address = supplier.Address;
            entity.Email = supplier.Email;
            entity.Status = supplier.Status;

            _context.Suppliers.Update(entity);
            await _context.SaveChangesAsync();
            return Ok(entity);
        }

        [HttpDelete("suppliers/{id}")]
        public async Task<IActionResult> DeleteSupplier(Guid id)
        {
            var entity = await _context.Suppliers.FindAsync(id);
            if (entity == null) return NotFound();

            if (await _context.StockInwards.AnyAsync(si => si.SupplierId == id))
            {
                return BadRequest("Cannot delete supplier. Transactions exist for this supplier.");
            }

            _context.Suppliers.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ==========================================
        // ITEM MASTER
        // ==========================================

        [HttpGet("items")]
        public async Task<IActionResult> GetItems()
        {
            var data = await _context.Items
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .OrderBy(i => i.Name)
                .ToListAsync();
            return Ok(data);
        }

        [HttpPost("items")]
        public async Task<IActionResult> CreateItem([FromBody] Item item)
        {
            if (await _context.Items.AnyAsync(i => i.Code.ToLower() == item.Code.ToLower()))
            {
                return BadRequest("Item code already exists.");
            }
            item.Id = Guid.NewGuid();
            item.CreatedAt = DateTimeOffset.UtcNow;
            
            // Clear navigation objects to prevent EF trying to insert them as new records
            item.Category = null;
            item.Unit = null;

            _context.Items.Add(item);
            await _context.SaveChangesAsync();

            // Reload to include navigation properties
            var savedItem = await _context.Items
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .FirstOrDefaultAsync(i => i.Id == item.Id);

            return Ok(savedItem);
        }

        [HttpPut("items/{id}")]
        public async Task<IActionResult> UpdateItem(Guid id, [FromBody] Item item)
        {
            var entity = await _context.Items.FindAsync(id);
            if (entity == null) return NotFound();

            if (await _context.Items.AnyAsync(i => i.Id != id && i.Code.ToLower() == item.Code.ToLower()))
            {
                return BadRequest("Item code already exists.");
            }

            entity.Code = item.Code;
            entity.Name = item.Name;
            entity.CategoryId = item.CategoryId;
            entity.UnitId = item.UnitId;
            entity.Brand = item.Brand;
            entity.HSNCode = item.HSNCode;
            entity.MinimumStock = item.MinimumStock;
            entity.ReorderLevel = item.ReorderLevel;
            entity.BarcodeType = item.BarcodeType;

            _context.Items.Update(entity);
            await _context.SaveChangesAsync();

            var savedItem = await _context.Items
                .Include(i => i.Category)
                .Include(i => i.Unit)
                .FirstOrDefaultAsync(i => i.Id == id);

            return Ok(savedItem);
        }

        [HttpDelete("items/{id}")]
        public async Task<IActionResult> DeleteItem(Guid id)
        {
            var entity = await _context.Items.FindAsync(id);
            if (entity == null) return NotFound();

            if (await _context.StockLedgers.AnyAsync(l => l.ItemId == id))
            {
                return BadRequest("Cannot delete item. Stock ledger records exist for this item.");
            }

            _context.Items.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }
    }
}
