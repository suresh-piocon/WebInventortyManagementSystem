using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
    public class JobWorkController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public JobWorkController(InventoryDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        // ==========================================
        // HELPERS FOR STANDARD STOCK ITEMS
        // ==========================================
        private async Task<Guid> GetOrCreateStandardItemAsync(string code, string name, string categoryName, string unitCode)
        {
            var item = await _context.Items.FirstOrDefaultAsync(i => i.Code == code);
            if (item != null) return item.Id;

            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Name == categoryName);
            if (category == null)
            {
                category = new Category { Id = Guid.NewGuid(), Name = categoryName, CreatedAt = DateTimeOffset.UtcNow };
                _context.Categories.Add(category);
                await _context.SaveChangesAsync();
            }

            var unit = await _context.Units.FirstOrDefaultAsync(u => u.Code == unitCode);
            if (unit == null)
            {
                unit = new Unit 
                { 
                    Id = Guid.NewGuid(), 
                    Code = unitCode, 
                    Name = unitCode == "KG" ? "Kilograms" : (unitCode == "MTR" ? "Meters" : "Pieces"), 
                    CreatedAt = DateTimeOffset.UtcNow 
                };
                _context.Units.Add(unit);
                await _context.SaveChangesAsync();
            }

            item = new Item
            {
                Id = Guid.NewGuid(),
                Code = code,
                Name = name,
                CategoryId = category.Id,
                UnitId = unit.Id,
                BarcodeType = "Batch",
                GSTPercent = 0.00M,
                CreatedAt = DateTimeOffset.UtcNow
            };

            _context.Items.Add(item);
            await _context.SaveChangesAsync();

            return item.Id;
        }

        private async Task<string> GenerateUniqueBarcodeAsync()
        {
            var dbBarcodes = await _context.BarcodeMasters
                .Where(b => b.Barcode.ToUpper().StartsWith("ITEM"))
                .Select(b => b.Barcode)
                .ToListAsync();

            var localBarcodes = _context.BarcodeMasters.Local
                .Where(b => b.Barcode != null && b.Barcode.ToUpper().StartsWith("ITEM"))
                .Select(b => b.Barcode)
                .ToList();

            var allBarcodes = dbBarcodes
                .Concat(localBarcodes)
                .Where(b => !string.IsNullOrEmpty(b))
                .Distinct();

            int maxSeq = 0;
            foreach (var bc in allBarcodes)
            {
                var numStr = bc.ToUpper().Replace("ITEM", "");
                if (int.TryParse(numStr, out var num))
                {
                    if (num > maxSeq)
                    {
                        maxSeq = num;
                    }
                }
            }

            int nextNum = maxSeq + 1;
            return $"ITEM{nextNum:D6}";
        }

        // ==========================================
        // JOB WORK MASTER API
        // ==========================================
        [HttpGet("workers")]
        public async Task<IActionResult> GetWorkers()
        {
            var data = await _context.JobWorkMasters.OrderBy(w => w.Name).ToListAsync();
            return Ok(data);
        }

        [HttpGet("workers/{id}")]
        public async Task<IActionResult> GetWorkerById(Guid id)
        {
            var data = await _context.JobWorkMasters.FindAsync(id);
            if (data == null) return NotFound();
            return Ok(data);
        }

        [HttpPost("workers")]
        public async Task<IActionResult> CreateWorker([FromBody] JobWorkMaster worker)
        {
            worker.Id = Guid.NewGuid();
            worker.CreatedAt = DateTimeOffset.UtcNow;
            _context.JobWorkMasters.Add(worker);
            await _context.SaveChangesAsync();
            return Ok(worker);
        }

        [HttpPut("workers/{id}")]
        public async Task<IActionResult> UpdateWorker(Guid id, [FromBody] JobWorkMaster worker)
        {
            var entity = await _context.JobWorkMasters.FindAsync(id);
            if (entity == null) return NotFound();

            entity.Name = worker.Name;
            entity.Type = worker.Type;
            entity.Address = worker.Address;
            entity.Mobile = worker.Mobile;
            entity.GSTIN = worker.GSTIN;
            entity.LedgerAccount = worker.LedgerAccount;
            entity.WastePercentage = worker.WastePercentage;
            entity.Active = worker.Active;

            _context.JobWorkMasters.Update(entity);
            await _context.SaveChangesAsync();
            return Ok(entity);
        }

        [HttpDelete("workers/{id}")]
        public async Task<IActionResult> DeleteWorker(Guid id)
        {
            var entity = await _context.JobWorkMasters.FindAsync(id);
            if (entity == null) return NotFound();

            bool hasLooms = await _context.LoomMasters.AnyAsync(l => l.WeaverId == id);
            bool hasLedgers = await _context.JobLedgers.AnyAsync(l => l.JobWorkerId == id);
            if (hasLooms || hasLedgers)
            {
                return BadRequest("Cannot delete job worker. Active looms or transactions exist for this worker.");
            }

            _context.JobWorkMasters.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ==========================================
        // LOOM MASTER API
        // ==========================================
        [HttpGet("looms")]
        public async Task<IActionResult> GetLooms()
        {
            var data = await _context.LoomMasters
                .Include(l => l.Weaver)
                .OrderBy(l => l.LoomNo)
                .ToListAsync();
            return Ok(data);
        }

        [HttpPost("looms")]
        public async Task<IActionResult> CreateLoom([FromBody] LoomMaster loom)
        {
            loom.Id = Guid.NewGuid();
            loom.CreatedAt = DateTimeOffset.UtcNow;
            loom.Weaver = null; // Prevent EF insert
            _context.LoomMasters.Add(loom);
            await _context.SaveChangesAsync();

            var saved = await _context.LoomMasters
                .Include(l => l.Weaver)
                .FirstOrDefaultAsync(l => l.Id == loom.Id);

            return Ok(saved);
        }

        [HttpPut("looms/{id}")]
        public async Task<IActionResult> UpdateLoom(Guid id, [FromBody] LoomMaster loom)
        {
            var entity = await _context.LoomMasters.FindAsync(id);
            if (entity == null) return NotFound();

            entity.LoomNo = loom.LoomNo;
            entity.WeaverId = loom.WeaverId;
            entity.Active = loom.Active;

            _context.LoomMasters.Update(entity);
            await _context.SaveChangesAsync();

            var saved = await _context.LoomMasters
                .Include(l => l.Weaver)
                .FirstOrDefaultAsync(l => l.Id == id);

            return Ok(saved);
        }

        [HttpDelete("looms/{id}")]
        public async Task<IActionResult> DeleteLoom(Guid id)
        {
            var entity = await _context.LoomMasters.FindAsync(id);
            if (entity == null) return NotFound();

            bool hasAllocations = await _context.LoomAllocations.AnyAsync(a => a.LoomId == id);
            if (hasAllocations)
            {
                return BadRequest("Cannot delete loom. Setup/Allocations exist for this loom.");
            }

            _context.LoomMasters.Remove(entity);
            await _context.SaveChangesAsync();
            return Ok();
        }

        // ==========================================
        // LOOM ALLOCATION SETUP
        // ==========================================
        [HttpGet("allocations")]
        public async Task<IActionResult> GetAllocations()
        {
            var data = await _context.LoomAllocations
                .Include(a => a.Loom)
                    .ThenInclude(l => l!.Weaver)
                .Include(a => a.Design)
                .OrderByDescending(a => a.StartDate)
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("allocations/active/{loomId}")]
        public async Task<IActionResult> GetActiveAllocationByLoom(Guid loomId)
        {
            var data = await _context.LoomAllocations
                .Include(a => a.Loom)
                    .ThenInclude(l => l!.Weaver)
                .Include(a => a.Design)
                .FirstOrDefaultAsync(a => a.LoomId == loomId && a.Active);

            if (data == null) return NotFound("No active design allocated to this loom.");
            return Ok(data);
        }

        [HttpPost("allocations")]
        public async Task<IActionResult> SetupAllocation([FromBody] LoomAllocation allocation)
        {
            using var trans = await _context.Database.BeginTransactionAsync();
            try
            {
                // Deactivate all previous active setups for this loom
                var activeSetups = await _context.LoomAllocations
                    .Where(a => a.LoomId == allocation.LoomId && a.Active)
                    .ToListAsync();
                foreach (var setup in activeSetups)
                {
                    setup.Active = false;
                }

                allocation.Id = Guid.NewGuid();
                allocation.Active = true;
                allocation.CreatedAt = DateTimeOffset.UtcNow;
                allocation.Design = null;
                allocation.Loom = null;

                _context.LoomAllocations.Add(allocation);
                await _context.SaveChangesAsync();
                await trans.CommitAsync();

                var saved = await _context.LoomAllocations
                    .Include(a => a.Loom)
                        .ThenInclude(l => l!.Weaver)
                    .Include(a => a.Design)
                    .FirstOrDefaultAsync(a => a.Id == allocation.Id);

                return Ok(saved);
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // DYEING ISSUE
        // ==========================================
        [HttpGet("dyeing/issues")]
        public async Task<IActionResult> GetDyeingIssues()
        {
            var data = await _context.DyeingIssues
                .Include(i => i.Dyer)
                .Include(i => i.Details)
                    .ThenInclude(d => d.WarpTypeSpec)
                .Include(i => i.Details)
                    .ThenInclude(d => d.WeftTypeSpec)
                .OrderByDescending(i => i.IssueDate)
                .ToListAsync();
            return Ok(data);
        }

        [HttpPost("dyeing/issues")]
        public async Task<IActionResult> SaveDyeingIssue([FromBody] DyeingIssue issue)
        {
            if (issue == null || issue.Details == null || !issue.Details.Any())
                return BadRequest("Issue details are required.");

            using var trans = await _context.Database.BeginTransactionAsync();
            try
            {
                if (string.IsNullOrWhiteSpace(issue.IssueNo))
                {
                    var year = DateTime.UtcNow.Year;
                    var prefix = $"DYI-{year}-";
                    var maxNo = await _context.DyeingIssues
                        .Where(i => i.IssueNo.StartsWith(prefix))
                        .Select(i => i.IssueNo)
                        .ToListAsync();
                    int nextSeq = 1;
                    foreach (var no in maxNo)
                    {
                        var parts = no.Split('-');
                        if (parts.Length >= 3 && int.TryParse(parts[2], out var seq))
                        {
                            if (seq >= nextSeq) nextSeq = seq + 1;
                        }
                    }
                    issue.IssueNo = $"{prefix}{nextSeq:D6}";
                }
                else
                {
                    if (await _context.DyeingIssues.AnyAsync(i => i.IssueNo.ToLower() == issue.IssueNo.ToLower()))
                    {
                        return BadRequest($"Issue number {issue.IssueNo} already exists.");
                    }
                }

                issue.Id = Guid.NewGuid();
                issue.CreatedAt = DateTimeOffset.UtcNow;
                issue.Dyer = null;

                _context.DyeingIssues.Add(issue);

                // Fetch Dyer Name for ledger
                var dyer = await _context.JobWorkMasters.FindAsync(issue.DyerId);
                var dyerName = dyer?.Name ?? "Dyer";

                foreach (var detail in issue.Details)
                {
                    detail.Id = Guid.NewGuid();
                    detail.DyeingIssueId = issue.Id;
                    
                    detail.Design = null;
                    detail.WarpTypeSpec = null;
                    detail.WeftTypeSpec = null;

                    // Resolve Stock Item
                    string spec = "";
                    if (detail.YarnType == "Warp")
                    {
                        var warp = await _context.WarpTypeMasters.FindAsync(detail.WarpTypeId);
                        spec = warp?.WarpType ?? "Unknown Warp";
                    }
                    else
                    {
                        var weft = await _context.WeftTypeMasters.FindAsync(detail.WeftTypeId);
                        spec = weft?.WeftType ?? "Unknown Weft";
                    }

                    string stockCode = $"GREY-{detail.YarnType.ToUpper()}-{spec.Replace("*","-").Replace("+","-")}";
                    string stockName = $"Grey {detail.YarnType} Yarn | {spec}";
                    var stockItemId = await GetOrCreateStandardItemAsync(stockCode, stockName, "Grey Yarn", "KG");

                    // 1. Stock Deduct from Grey Warp/Weft Yarn Stock
                    var currentStockBal = await _context.StockLedgers
                        .Where(sl => sl.ItemId == stockItemId)
                        .OrderByDescending(sl => sl.TransactionDate)
                        .ThenByDescending(sl => sl.CreatedAt)
                        .Select(sl => new { sl.BalanceQty, sl.BalanceWeight })
                        .FirstOrDefaultAsync();

                    decimal prevQty = currentStockBal?.BalanceQty ?? 0;
                    decimal prevWeight = currentStockBal?.BalanceWeight ?? 0;

                    var stockLedger = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        ItemId = stockItemId,
                        TransactionDate = issue.IssueDate,
                        TransactionType = "Adjustment", // standard outward
                        ReferenceNo = issue.IssueNo,
                        BatchNo = "GREY-BATCH",
                        TrackingNo = $"TRK-GREY-{Guid.NewGuid().ToString("N").Substring(0,8).ToUpper()}",
                        InwardQty = 0,
                        OutwardQty = detail.Qty,
                        BalanceQty = prevQty - detail.Qty,
                        UnitPrice = detail.Rate,
                        InwardWeight = 0,
                        OutwardWeight = detail.WeightKgs,
                        BalanceWeight = prevWeight - detail.WeightKgs,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.StockLedgers.Add(stockLedger);

                    // 2. Create Dyer Job Ledger Entry (Debit Dyer for yarn issued)
                    var lastJobBal = await _context.JobLedgers
                        .Where(jl => jl.JobWorkerId == issue.DyerId)
                        .OrderByDescending(jl => jl.TransactionDate)
                        .ThenByDescending(jl => jl.CreatedAt)
                        .Select(jl => jl.Balance)
                        .FirstOrDefaultAsync();

                    var jobLedger = new JobLedger
                    {
                        Id = Guid.NewGuid(),
                        JobWorkerId = issue.DyerId,
                        TransactionDate = issue.IssueDate,
                        VoucherNo = issue.IssueNo,
                        Particulars = $"Grey {detail.YarnType} Yarn issued | {spec}",
                        IssueQty = detail.Qty,
                        ReceiveQty = 0,
                        IssueWeight = detail.WeightKgs,
                        ReceiveWeight = 0,
                        Debit = detail.Amount,
                        Credit = 0,
                        Balance = lastJobBal + detail.Amount, // Dyer owes us the yarn value
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.JobLedgers.Add(jobLedger);
                }

                await _context.SaveChangesAsync();
                await trans.CommitAsync();
                return Ok(issue);
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // DYEING RECEIVE
        // ==========================================
        [HttpGet("dyeing/receives")]
        public async Task<IActionResult> GetDyeingReceives()
        {
            var data = await _context.DyeingReceives
                .Include(r => r.Dyer)
                .Include(r => r.Details)
                    .ThenInclude(d => d.WarpTypeSpec)
                .Include(r => r.Details)
                    .ThenInclude(d => d.WeftTypeSpec)
                .OrderByDescending(r => r.ReceiveDate)
                .ToListAsync();
            return Ok(data);
        }

        [HttpPost("dyeing/receives")]
        public async Task<IActionResult> SaveDyeingReceive([FromBody] DyeingReceive receive)
        {
            if (receive == null || receive.Details == null || !receive.Details.Any())
                return BadRequest("Receive details are required.");

            using var trans = await _context.Database.BeginTransactionAsync();
            try
            {
                var year = DateTime.UtcNow.Year;
                var prefix = $"DYR-{year}-";
                var maxNo = await _context.DyeingReceives
                    .Where(r => r.ReceiveNo.StartsWith(prefix))
                    .Select(r => r.ReceiveNo)
                    .ToListAsync();
                int nextSeq = 1;
                foreach (var no in maxNo)
                {
                    var parts = no.Split('-');
                    if (parts.Length >= 3 && int.TryParse(parts[2], out var seq))
                    {
                        if (seq >= nextSeq) nextSeq = seq + 1;
                    }
                }
                receive.ReceiveNo = $"{prefix}{nextSeq:D6}";
                receive.Id = Guid.NewGuid();
                receive.CreatedAt = DateTimeOffset.UtcNow;
                receive.Dyer = null;

                _context.DyeingReceives.Add(receive);

                foreach (var detail in receive.Details)
                {
                    detail.Id = Guid.NewGuid();
                    detail.DyeingReceiveId = receive.Id;

                    detail.Design = null;
                    detail.WarpTypeSpec = null;
                    detail.WeftTypeSpec = null;

                    // Resolve Stock Item (Dyed Warp or Dyed Weft)
                    string spec = "";
                    if (detail.YarnType == "Warp")
                    {
                        var warp = await _context.WarpTypeMasters.FindAsync(detail.WarpTypeId);
                        spec = warp?.WarpType ?? "Unknown Warp";
                    }
                    else
                    {
                        var weft = await _context.WeftTypeMasters.FindAsync(detail.WeftTypeId);
                        spec = weft?.WeftType ?? "Unknown Weft";
                    }
                    string color = string.IsNullOrEmpty(detail.DyedColor) ? "RAW" : detail.DyedColor;

                    // Group by WarpType/WeftType Stock directly!
                    string typeCode = detail.YarnType.ToUpper(); // "WARP" or "WEFT"
                    string stockCode = $"DYED-{typeCode}-{spec.Replace("*","-").Replace("+","-")}-{color.ToUpper()}";
                    string stockName = $"Dyed {detail.YarnType} Yarn | {spec} | {color}";
                    string category = detail.YarnType == "Warp" ? "Warp Yarn Stock" : "Weft Yarn Stock";
                    
                    var stockItemId = await GetOrCreateStandardItemAsync(stockCode, stockName, category, "KG");

                    // 1. Add Stock To Dyed Warp / Dyed Weft Yarn Stock
                    var currentStockBal = await _context.StockLedgers
                        .Where(sl => sl.ItemId == stockItemId)
                        .OrderByDescending(sl => sl.TransactionDate)
                        .ThenByDescending(sl => sl.CreatedAt)
                        .Select(sl => new { sl.BalanceQty, sl.BalanceWeight })
                        .FirstOrDefaultAsync();

                    decimal prevQty = currentStockBal?.BalanceQty ?? 0;
                    decimal prevWeight = currentStockBal?.BalanceWeight ?? 0;

                    // Find rate from referenced Dyeing Issue (if available) to record inventory cost
                    decimal dyeingRate = detail.Rate;
                    if (dyeingRate == 0 && !string.IsNullOrEmpty(receive.IssueReferenceNo))
                    {
                        dyeingRate = await _context.DyeingIssueDetails
                            .Include(d => d.DyeingIssue)
                            .Where(d => d.DyeingIssue!.IssueNo == receive.IssueReferenceNo 
                                     && d.YarnType == detail.YarnType 
                                     && (detail.YarnType == "Warp" ? d.WarpTypeId == detail.WarpTypeId : d.WeftTypeId == detail.WeftTypeId))
                            .Select(d => d.Rate)
                            .FirstOrDefaultAsync();
                    }

                    var stockLedger = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        ItemId = stockItemId,
                        TransactionDate = receive.ReceiveDate,
                        TransactionType = "Purchase", // Inward
                        ReferenceNo = receive.ReceiveNo,
                        BatchNo = "DYED-BATCH",
                        TrackingNo = $"TRK-DYED-{Guid.NewGuid().ToString("N").Substring(0,8).ToUpper()}",
                        InwardQty = detail.QtyReceived,
                        OutwardQty = 0,
                        BalanceQty = prevQty + detail.QtyReceived,
                        UnitPrice = dyeingRate,
                        InwardWeight = detail.WeightReceived,
                        OutwardWeight = 0,
                        BalanceWeight = prevWeight + detail.WeightReceived,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.StockLedgers.Add(stockLedger);

                    // 2. Dyer Job Ledger Credit (Credit Dyer for dyed yarn delivered - clears grey yarn debt or records service payout)
                    decimal creditVal = detail.QtyReceived * dyeingRate;

                    var lastJobBal = await _context.JobLedgers
                        .Where(jl => jl.JobWorkerId == receive.DyerId)
                        .OrderByDescending(jl => jl.TransactionDate)
                        .ThenByDescending(jl => jl.CreatedAt)
                        .Select(jl => jl.Balance)
                        .FirstOrDefaultAsync();

                    var jobLedger = new JobLedger
                    {
                        Id = Guid.NewGuid(),
                        JobWorkerId = receive.DyerId,
                        TransactionDate = receive.ReceiveDate,
                        VoucherNo = receive.ReceiveNo,
                        Particulars = $"Dyed {detail.YarnType} Yarn received | {spec} | {color}",
                        IssueQty = 0,
                        ReceiveQty = detail.QtyReceived,
                        IssueWeight = 0,
                        ReceiveWeight = detail.WeightReceived,
                        Debit = 0,
                        Credit = creditVal,
                        Balance = lastJobBal - creditVal, // Dyer's outstanding balance drops
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.JobLedgers.Add(jobLedger);
                }

                await _context.SaveChangesAsync();
                await trans.CommitAsync();
                return Ok(receive);
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        // ==========================================
        // WEAVING PRODUCTION LEDGER (Unified Screen)
        // ==========================================
        [HttpGet("weaving/ledger/{loomAllocationId}")]
        public async Task<IActionResult> GetWeavingLedger(Guid loomAllocationId)
        {
            var data = await _context.WeavingLedgerEntries
                .Where(w => w.LoomAllocationId == loomAllocationId)
                .OrderBy(w => w.Date)
                .ThenBy(w => w.CreatedAt)
                .ToListAsync();
            return Ok(data);
        }

        [HttpPost("weaving/ledger")]
        public async Task<IActionResult> AddWeavingLedgerEntry([FromBody] WeavingLedgerEntry entry)
        {
            using var trans = await _context.Database.BeginTransactionAsync();
            try
            {
                var allocation = await _context.LoomAllocations
                    .Include(a => a.Loom)
                        .ThenInclude(l => l!.Weaver)
                    .Include(a => a.Design)
                    .FirstOrDefaultAsync(a => a.Id == entry.LoomAllocationId);

                if (allocation == null)
                    return BadRequest("Loom allocation setup not found.");

                entry.Id = Guid.NewGuid();
                entry.CreatedAt = DateTimeOffset.UtcNow;
                entry.LoomAllocation = null;

                _context.WeavingLedgerEntries.Add(entry);
                await _context.SaveChangesAsync();

                // Process Stock Ledger and Job Ledger based on entry type
                string entryType = entry.EntryType.Trim();
                decimal debitAmt = entry.Debit;
                decimal creditAmt = entry.Credit;

                if (entryType.Equals("Dyed Warp", StringComparison.OrdinalIgnoreCase) ||
                    entryType.Equals("Dyed Weft", StringComparison.OrdinalIgnoreCase))
                {
                    // 1. Stock Deduct
                    string spec = "";
                    if (entryType.Equals("Dyed Warp", StringComparison.OrdinalIgnoreCase))
                    {
                        var designItem = await _context.Items
                            .Include(d => d.WarpTypeSpec)
                            .FirstOrDefaultAsync(d => d.Id == allocation.ItemId);
                        spec = designItem?.WarpTypeSpec?.WarpType ?? allocation.Design?.WarpType ?? "Unknown Warp";
                    }
                    else
                    {
                        var designItem = await _context.Items
                            .Include(d => d.WeftTypeSpec)
                            .FirstOrDefaultAsync(d => d.Id == allocation.ItemId);
                        spec = designItem?.WeftTypeSpec?.WeftType ?? allocation.Design?.WeftType ?? "Unknown Weft";
                    }
                    
                    string color = "RAW";
                    if (!string.IsNullOrEmpty(entry.Details))
                    {
                        color = entry.Details.Trim().ToUpper();
                    }
                    else if (!string.IsNullOrEmpty(entry.Narration))
                    {
                        color = entry.Narration.Trim().ToUpper();
                    }

                    string typeCode = entryType.Equals("Dyed Warp", StringComparison.OrdinalIgnoreCase) ? "WARP" : "WEFT";
                    string code = $"DYED-{typeCode}-{spec.Replace("*","-").Replace("+","-")}-{color.Replace(" ","-").ToUpper()}";
                    string name = $"Dyed {(typeCode == "WARP" ? "Warp" : "Weft")} Yarn | {spec} | {color}";
                    string category = typeCode == "WARP" ? "Warp Yarn Stock" : "Weft Yarn Stock";
                    
                    var itemId = await GetOrCreateStandardItemAsync(code, name, category, "KG");

                    var lastBal = await _context.StockLedgers
                        .Where(sl => sl.ItemId == itemId)
                        .OrderByDescending(sl => sl.TransactionDate)
                        .ThenByDescending(sl => sl.CreatedAt)
                        .Select(sl => new { sl.BalanceQty, sl.BalanceWeight })
                        .FirstOrDefaultAsync();

                    decimal prevQty = lastBal?.BalanceQty ?? 0;
                    decimal prevWeight = lastBal?.BalanceWeight ?? 0;

                    var slRecord = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        ItemId = itemId,
                        TransactionDate = entry.Date,
                        TransactionType = "Adjustment", // Outward
                        ReferenceNo = $"LOM-{allocation.Loom!.LoomNo}",
                        BatchNo = "WEAVE-BATCH",
                        TrackingNo = $"TRK-WEAVE-{Guid.NewGuid().ToString("N").Substring(0,8).ToUpper()}",
                        InwardQty = 0,
                        OutwardQty = entry.WarpQty,
                        BalanceQty = prevQty - entry.WarpQty,
                        UnitPrice = 0,
                        InwardWeight = 0,
                        OutwardWeight = entry.IssuedWt,
                        BalanceWeight = prevWeight - entry.IssuedWt,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.StockLedgers.Add(slRecord);
                }
                else if (entryType.Equals("Zari", StringComparison.OrdinalIgnoreCase) ||
                         entryType.Equals("Silk", StringComparison.OrdinalIgnoreCase) ||
                         entryType.Equals("Others", StringComparison.OrdinalIgnoreCase))
                {
                    // Stock Deduct
                    string code = entryType.ToUpper();
                    string name = entryType;
                    var itemId = await GetOrCreateStandardItemAsync(code, name, "Material", "KG");

                    var lastBal = await _context.StockLedgers
                        .Where(sl => sl.ItemId == itemId)
                        .OrderByDescending(sl => sl.TransactionDate)
                        .ThenByDescending(sl => sl.CreatedAt)
                        .Select(sl => new { sl.BalanceQty, sl.BalanceWeight })
                        .FirstOrDefaultAsync();

                    decimal prevQty = lastBal?.BalanceQty ?? 0;
                    decimal prevWeight = lastBal?.BalanceWeight ?? 0;

                    var slRecord = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        ItemId = itemId,
                        TransactionDate = entry.Date,
                        TransactionType = "Adjustment",
                        ReferenceNo = $"LOM-{allocation.Loom!.LoomNo}",
                        BatchNo = "WEAVE-BATCH",
                        TrackingNo = $"TRK-WEAVE-{Guid.NewGuid().ToString("N").Substring(0,8).ToUpper()}",
                        InwardQty = 0,
                        OutwardQty = entry.WarpQty > 0 ? entry.WarpQty : 1, // Default qty
                        BalanceQty = prevQty - (entry.WarpQty > 0 ? entry.WarpQty : 1),
                        UnitPrice = 0,
                        InwardWeight = 0,
                        OutwardWeight = entry.IssuedWt,
                        BalanceWeight = prevWeight - entry.IssuedWt,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.StockLedgers.Add(slRecord);
                }
                else if (entryType.Equals("Saree", StringComparison.OrdinalIgnoreCase))
                {
                    // 1. Finished Saree Stock Add (Specific Item/Design)
                    var itemId = allocation.ItemId;
                    var lastBal = await _context.StockLedgers
                        .Where(sl => sl.ItemId == itemId)
                        .OrderByDescending(sl => sl.TransactionDate)
                        .ThenByDescending(sl => sl.CreatedAt)
                        .Select(sl => new { sl.BalanceQty, sl.BalanceWeight })
                        .FirstOrDefaultAsync();

                    decimal prevQty = lastBal?.BalanceQty ?? 0;
                    decimal prevWeight = lastBal?.BalanceWeight ?? 0;

                    var trackingNo = $"TRK-SAREE-{Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper()}";

                    var slRecord = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        ItemId = itemId,
                        TransactionDate = entry.Date,
                        TransactionType = "Purchase", // Inward Saree
                        ReferenceNo = $"LOM-{allocation.Loom!.LoomNo}",
                        BatchNo = "SAREE-BATCH",
                        TrackingNo = trackingNo,
                        InwardQty = entry.RodQty,
                        OutwardQty = 0,
                        BalanceQty = prevQty + entry.RodQty,
                        UnitPrice = allocation.Design!.Wages,
                        InwardWeight = entry.RodWt,
                        OutwardWeight = 0,
                        BalanceWeight = prevWeight + entry.RodWt,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.StockLedgers.Add(slRecord);

                    // Generate unique barcodes for each saree received
                    int qtyInt = (int)Math.Ceiling(entry.RodQty);
                    for (int i = 1; i <= qtyInt; i++)
                    {
                        var uniqueBarcode = await GenerateUniqueBarcodeAsync();
                        var barcodeMaster = new BarcodeMaster
                        {
                            Id = Guid.NewGuid(),
                            Barcode = uniqueBarcode,
                            ItemId = itemId,
                            BatchNo = "SAREE-BATCH",
                            TrackingNo = trackingNo,
                            Type = "Unique",
                            ImageUrl = allocation.Design?.BodyImage,
                            IsUsed = false,
                            CreatedAt = DateTimeOffset.UtcNow
                        };
                        _context.BarcodeMasters.Add(barcodeMaster);
                    }

                    // 2. Calculate Wages Credit amount
                    creditAmt = entry.RodQty * allocation.Design!.Wages;
                    entry.Credit = creditAmt;
                    _context.WeavingLedgerEntries.Update(entry);
                }

                // 3. Update consolidated JobWorker Ledger (JobLedger table)
                var lastJobBal = await _context.JobLedgers
                    .Where(jl => jl.JobWorkerId == allocation.Loom!.WeaverId)
                    .OrderByDescending(jl => jl.TransactionDate)
                    .ThenByDescending(jl => jl.CreatedAt)
                    .Select(jl => jl.Balance)
                    .FirstOrDefaultAsync();

                var jobLedger = new JobLedger
                {
                    Id = Guid.NewGuid(),
                    JobWorkerId = allocation.Loom!.WeaverId,
                    TransactionDate = entry.Date,
                    VoucherNo = $"LOM-{allocation.Loom.LoomNo}",
                    Particulars = $"Loom {allocation.Loom.LoomNo} - {entryType}: {entry.Details} {entry.Narration}",
                    IssueQty = entry.WarpQty,
                    ReceiveQty = entry.RodQty,
                    IssueWeight = entry.IssuedWt,
                    ReceiveWeight = entry.RodWt,
                    Debit = debitAmt,
                    Credit = creditAmt,
                    Balance = lastJobBal + debitAmt - creditAmt, // debit increases balance, credit/wages decreases it
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.JobLedgers.Add(jobLedger);

                await _context.SaveChangesAsync();
                await trans.CommitAsync();

                return Ok(entry);
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        [HttpDelete("weaving/ledger/{entryId}")]
        public async Task<IActionResult> DeleteWeavingLedgerEntry(Guid entryId)
        {
            using var trans = await _context.Database.BeginTransactionAsync();
            try
            {
                var entry = await _context.WeavingLedgerEntries
                    .Include(w => w.LoomAllocation)
                        .ThenInclude(a => a!.Loom)
                    .FirstOrDefaultAsync(w => w.Id == entryId);

                if (entry == null) return NotFound();

                // Reverse Stock Ledger entries
                var stockLedgers = await _context.StockLedgers
                    .Where(s => s.ReferenceNo == $"LOM-{entry.LoomAllocation!.Loom!.LoomNo}" && s.TransactionDate == entry.Date)
                    .ToListAsync();

                // Delete associated barcodes and check usage
                var trackingNos = stockLedgers.Select(s => s.TrackingNo).ToList();
                if (trackingNos.Any())
                {
                    var isAnyBarcodeUsed = await _context.BarcodeMasters.AnyAsync(b => trackingNos.Contains(b.TrackingNo) && b.IsUsed);
                    if (isAnyBarcodeUsed)
                    {
                        return BadRequest("Cannot delete this weaving entry because some barcodes have already been issued in a proforma invoice/outward transaction.");
                    }

                    var barcodesToDelete = await _context.BarcodeMasters.Where(b => trackingNos.Contains(b.TrackingNo)).ToListAsync();
                    _context.BarcodeMasters.RemoveRange(barcodesToDelete);
                }

                _context.StockLedgers.RemoveRange(stockLedgers);

                // Reverse Job Ledger entries
                var jobLedgers = await _context.JobLedgers
                    .Where(j => j.JobWorkerId == entry.LoomAllocation.Loom.WeaverId && j.TransactionDate == entry.Date && j.VoucherNo == $"LOM-{entry.LoomAllocation.Loom.LoomNo}")
                    .ToListAsync();
                _context.JobLedgers.RemoveRange(jobLedgers);

                _context.WeavingLedgerEntries.Remove(entry);
                await _context.SaveChangesAsync();
                await trans.CommitAsync();
                return Ok();
            }
            catch (Exception ex)
            {
                await trans.RollbackAsync();
                return StatusCode(500, ex.Message);
            }
        }

        [HttpGet("weaving/receipts")]
        public async Task<IActionResult> GetWeavingReceipts(
            [FromQuery] DateTimeOffset? startDate,
            [FromQuery] DateTimeOffset? endDate)
        {
            var query = _context.WeavingLedgerEntries
                .Include(w => w.LoomAllocation)
                    .ThenInclude(a => a!.Loom)
                        .ThenInclude(l => l!.Weaver)
                .Include(w => w.LoomAllocation)
                    .ThenInclude(a => a!.Design)
                .Where(w => w.EntryType == "Saree")
                .AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(w => w.Date >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(w => w.Date <= endDate.Value);
            }

            var receipts = await query
                .OrderByDescending(w => w.Date)
                .ToListAsync();

            return Ok(receipts);
        }

        [HttpGet("weaving/receipts/{id}/barcodes")]
        public async Task<IActionResult> GetWeavingReceiptBarcodes(Guid id)
        {
            var entry = await _context.WeavingLedgerEntries
                .Include(w => w.LoomAllocation)
                    .ThenInclude(a => a!.Loom)
                .FirstOrDefaultAsync(w => w.Id == id);

            if (entry == null) return NotFound("Weaving receipt not found.");

            var trackingNo = await _context.StockLedgers
                .Where(sl => sl.ItemId == entry.LoomAllocation!.ItemId 
                          && sl.ReferenceNo == $"LOM-{entry.LoomAllocation!.Loom!.LoomNo}"
                          && sl.TransactionDate == entry.Date
                          && sl.InwardQty == entry.RodQty)
                .Select(sl => sl.TrackingNo)
                .FirstOrDefaultAsync();

            if (string.IsNullOrEmpty(trackingNo))
            {
                return Ok(new List<BarcodeMaster>());
            }

            var barcodes = await _context.BarcodeMasters
                .Include(b => b.Item)
                .Where(b => b.TrackingNo == trackingNo)
                .OrderBy(b => b.Barcode)
                .ToListAsync();

            return Ok(barcodes);
        }

        // ==========================================
        // REPORTS & METRICS
        // ==========================================
        [HttpGet("reports/jobwork-ledger/{workerId}")]
        public async Task<IActionResult> GetJobWorkLedger(Guid workerId)
        {
            var data = await _context.JobLedgers
                .Where(j => j.JobWorkerId == workerId)
                .OrderBy(j => j.TransactionDate)
                .ThenBy(j => j.CreatedAt)
                .ToListAsync();

            decimal running = 0;
            foreach (var entry in data)
            {
                running += entry.Debit - entry.Credit;
                entry.Balance = running;
            }

            return Ok(data);
        }

        [HttpGet("reports/weaver-accounts")]
        public async Task<IActionResult> GetWeaverAccounts()
        {
            var allocations = await _context.LoomAllocations
                .Include(a => a.Loom)
                    .ThenInclude(l => l!.Weaver)
                .Include(a => a.Design)
                .ToListAsync();

            var result = new List<WeaverAccountDto>();
            foreach (var alloc in allocations)
            {
                var ledger = await _context.WeavingLedgerEntries
                    .Where(w => w.LoomAllocationId == alloc.Id)
                    .ToListAsync();

                decimal debits = ledger.Sum(w => w.Debit);
                decimal credits = ledger.Sum(w => w.Credit);

                result.Add(new WeaverAccountDto
                {
                    LoomAllocationId = alloc.Id,
                    LoomNo = alloc.Loom?.LoomNo ?? "N/A",
                    WeaverName = alloc.Loom?.Weaver?.Name ?? "N/A",
                    DesignName = alloc.Design?.Name ?? "N/A",
                    TotalDebit = debits,
                    TotalCredit = credits,
                    OutstandingBalance = debits - credits
                });
            }

            return Ok(result);
        }

        [HttpGet("reports/loom-balances")]
        public async Task<IActionResult> GetLoomBalances()
        {
            var allocations = await _context.LoomAllocations
                .Include(a => a.Loom)
                    .ThenInclude(l => l!.Weaver)
                .Include(a => a.Design)
                .ToListAsync();

            var result = new List<LoomBalanceDto>();
            foreach (var alloc in allocations)
            {
                var ledger = await _context.WeavingLedgerEntries
                    .Where(w => w.LoomAllocationId == alloc.Id)
                    .ToListAsync();

                decimal issuedWarp = ledger.Where(w => w.EntryType.Contains("Warp")).Sum(w => w.WarpQty);
                decimal recdSaree = ledger.Where(w => w.EntryType.Contains("Saree")).Sum(w => w.RodQty);

                decimal issuedWt = ledger.Sum(w => w.IssuedWt);
                decimal recdWt = ledger.Sum(w => w.RodWt);

                result.Add(new LoomBalanceDto
                {
                    LoomNo = alloc.Loom?.LoomNo ?? "N/A",
                    WeaverName = alloc.Loom?.Weaver?.Name ?? "N/A",
                    DesignName = alloc.Design?.Name ?? "N/A",
                    IssuedWarpQty = issuedWarp,
                    ReceivedSareeQty = recdSaree,
                    BalanceSareeQty = issuedWarp - recdSaree,
                    IssuedWeight = issuedWt,
                    ReceivedWeight = recdWt,
                    BalanceWeight = issuedWt - recdWt
                });
            }

            return Ok(result);
        }

        [HttpGet("dashboard")]
        public async Task<IActionResult> GetJobWorkDashboard()
        {
            var workers = await _context.JobWorkMasters.ToListAsync();
            decimal dyerOut = 0;
            decimal weaverOut = 0;

            foreach (var worker in workers)
            {
                var bal = await _context.JobLedgers
                    .Where(j => j.JobWorkerId == worker.Id)
                    .OrderByDescending(j => j.TransactionDate)
                    .ThenByDescending(j => j.CreatedAt)
                    .Select(j => j.Balance)
                    .FirstOrDefaultAsync();

                if (worker.Type.Equals("Dyer", StringComparison.OrdinalIgnoreCase))
                    dyerOut += bal;
                else if (worker.Type.Equals("Weaver", StringComparison.OrdinalIgnoreCase))
                    weaverOut += bal;
            }

            // Loom Balances
            var loomBalancesResult = new List<LoomBalanceDto>();
            var allocations = await _context.LoomAllocations
                .Include(a => a.Loom)
                    .ThenInclude(l => l!.Weaver)
                .Include(a => a.Design)
                .Where(a => a.Active)
                .ToListAsync();

            foreach (var alloc in allocations)
            {
                var ledger = await _context.WeavingLedgerEntries
                    .Where(w => w.LoomAllocationId == alloc.Id)
                    .ToListAsync();

                decimal issuedWarp = ledger.Where(w => w.EntryType.Contains("Warp")).Sum(w => w.WarpQty);
                decimal recdSaree = ledger.Where(w => w.EntryType.Contains("Saree")).Sum(w => w.RodQty);
                decimal issuedWt = ledger.Sum(w => w.IssuedWt);
                decimal recdWt = ledger.Sum(w => w.RodWt);

                loomBalancesResult.Add(new LoomBalanceDto
                {
                    LoomNo = alloc.Loom?.LoomNo ?? "N/A",
                    WeaverName = alloc.Loom?.Weaver?.Name ?? "N/A",
                    DesignName = alloc.Design?.Name ?? "N/A",
                    IssuedWarpQty = issuedWarp,
                    ReceivedSareeQty = recdSaree,
                    BalanceSareeQty = issuedWarp - recdSaree,
                    IssuedWeight = issuedWt,
                    ReceivedWeight = recdWt,
                    BalanceWeight = issuedWt - recdWt
                });
            }

            // Pending quantities and stock
            decimal pendingDyeing = 0;
            var dyeingIssues = await _context.DyeingIssueDetails.SumAsync(d => d.Qty);
            var dyeingReceives = await _context.DyeingReceiveDetails.SumAsync(d => d.QtyReceived);
            pendingDyeing = Math.Max(0, dyeingIssues - dyeingReceives);

            decimal pendingWeaving = loomBalancesResult.Sum(l => l.BalanceSareeQty);

            // Saree stock count (Finished Goods)
            decimal finishedSareeStock = 0;
            var category = await _context.Categories.FirstOrDefaultAsync(c => c.Name == "Finished Goods" || c.Name == "Silk Sarees");
            if (category != null)
            {
                var items = await _context.Items.Where(i => i.CategoryId == category.Id).Select(i => i.Id).ToListAsync();
                foreach (var itemId in items)
                {
                    var bal = await _context.StockLedgers
                        .Where(sl => sl.ItemId == itemId)
                        .OrderByDescending(sl => sl.TransactionDate)
                        .ThenByDescending(sl => sl.CreatedAt)
                        .Select(sl => sl.BalanceQty)
                        .FirstOrDefaultAsync();
                    finishedSareeStock += bal;
                }
            }

            // Design wise balance
            var designBalances = new List<DesignBalanceDto>();
            var designs = await _context.Items.OrderBy(i => i.Name).ToListAsync();
            foreach (var d in designs)
            {
                var diIssues = await _context.DyeingIssueDetails.Where(x => x.DesignId == d.Id).SumAsync(x => x.Qty);
                var diRecs = await _context.DyeingReceiveDetails.Where(x => x.DesignId == d.Id).SumAsync(x => x.QtyReceived);
                
                var lAllocIds = await _context.LoomAllocations.Where(x => x.ItemId == d.Id).Select(x => x.Id).ToListAsync();
                decimal weavingIssued = 0;
                decimal weavingRecd = 0;
                if (lAllocIds.Any())
                {
                    weavingIssued = await _context.WeavingLedgerEntries
                        .Where(w => lAllocIds.Contains(w.LoomAllocationId) && w.EntryType.Contains("Warp"))
                        .SumAsync(w => w.WarpQty);
                    weavingRecd = await _context.WeavingLedgerEntries
                        .Where(w => lAllocIds.Contains(w.LoomAllocationId) && w.EntryType.Contains("Saree"))
                        .SumAsync(w => w.RodQty);
                }

                // Saree Stock for this design item
                var designSareeStock = await _context.StockLedgers
                    .Where(sl => sl.ItemId == d.Id)
                    .OrderByDescending(sl => sl.TransactionDate)
                    .ThenByDescending(sl => sl.CreatedAt)
                    .Select(sl => 0 + sl.BalanceQty) // Avoid EF translation issues
                    .FirstOrDefaultAsync();

                if (diIssues > 0 || weavingIssued > 0 || designSareeStock > 0)
                {
                    designBalances.Add(new DesignBalanceDto
                    {
                        DesignName = d.Name,
                        PendingDyeingQty = Math.Max(0, diIssues - diRecs),
                        PendingWeavingQty = Math.Max(0, weavingIssued - weavingRecd),
                        FinishedSareeStock = designSareeStock
                    });
                }
            }

            var dashboard = new JobWorkDashboardDto
            {
                DyerOutstanding = dyerOut,
                WeaverOutstanding = weaverOut,
                LoomBalances = loomBalancesResult,
                DesignBalances = designBalances,
                PendingDyeingQty = pendingDyeing,
                PendingWeavingQty = pendingWeaving,
                FinishedSareeStock = finishedSareeStock
            };

            return Ok(dashboard);
        }

        [HttpGet("dyeing/next-issue-no")]
        public async Task<IActionResult> GetNextIssueNo()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"DYI-{year}-";
            var maxNo = await _context.DyeingIssues
                .Where(i => i.IssueNo.StartsWith(prefix))
                .Select(i => i.IssueNo)
                .ToListAsync();
            int nextSeq = 1;
            foreach (var no in maxNo)
            {
                var parts = no.Split('-');
                if (parts.Length >= 3 && int.TryParse(parts[2], out var seq))
                {
                    if (seq >= nextSeq) nextSeq = seq + 1;
                }
            }
            return Ok(new { nextNo = $"{prefix}{nextSeq:D6}" });
        }

        [HttpGet("weaving/available-yarn")]
        public async Task<IActionResult> GetAvailableYarnForWeaving([FromQuery] Guid allocationId, [FromQuery] string yarnType)
        {
            var allocation = await _context.LoomAllocations
                .Include(a => a.Design)
                    .ThenInclude(d => d!.WarpTypeSpec)
                .Include(a => a.Design)
                    .ThenInclude(d => d!.WeftTypeSpec)
                .FirstOrDefaultAsync(a => a.Id == allocationId);

            if (allocation == null || allocation.Design == null)
            {
                return BadRequest("Invalid allocation.");
            }

            var design = allocation.Design;
            string spec = "";
            if (yarnType.Equals("Dyed Warp", StringComparison.OrdinalIgnoreCase))
            {
                spec = design.WarpTypeSpec?.WarpType ?? design.WarpType ?? "";
            }
            else
            {
                spec = design.WeftTypeSpec?.WeftType ?? design.WeftType ?? "";
            }

            if (string.IsNullOrEmpty(spec))
            {
                return Ok(new List<object>());
            }

            // Find all items that match this spec (ignore design code if they just want the warp/weft type stock sum!)
            // Wait, the user said:
            // "weaving issue if i select dyed warp it should fetch the warp type stock of respective design."
            // "same in dyed weft also."
            // So we fetch the stock items where category is "Warp Yarn Stock" or "Weft Yarn Stock" and the spec matches.
            string categoryName = yarnType.Equals("Dyed Warp", StringComparison.OrdinalIgnoreCase) ? "Warp Yarn Stock" : "Weft Yarn Stock";

            var stockItems = await _context.Items
                .Where(i => i.CategoryId == _context.Categories.FirstOrDefault(c => c.Name == categoryName).Id 
                         && i.Name.Contains(spec))
                .ToListAsync();

            var result = new List<object>();
            foreach (var item in stockItems)
            {
                var lastLedger = await _context.StockLedgers
                    .Where(sl => sl.ItemId == item.Id)
                    .OrderByDescending(sl => sl.TransactionDate)
                    .ThenByDescending(sl => sl.CreatedAt)
                    .FirstOrDefaultAsync();

                decimal balanceQty = lastLedger?.BalanceQty ?? 0;
                decimal balanceWeight = lastLedger?.BalanceWeight ?? 0;

                if (balanceQty == 0 && balanceWeight == 0) continue;

                // Extract color name from name (format: Dyed Warp Yarn | [Spec] | [Color])
                var parts = item.Name.Split('|');
                string colorName = parts.Length > 2 ? parts[2].Trim() : "RAW";

                result.Add(new
                {
                    itemId = item.Id,
                    color = colorName,
                    balanceQty = balanceQty,
                    balanceWeight = balanceWeight,
                    displayName = $"{colorName} (Bal: {balanceQty:N0} Pcs / {balanceWeight:F3} Kgs)"
                });
            }

            return Ok(result);
        }

        [HttpGet("stock/warp-weft-summary")]
        public async Task<IActionResult> GetWarpWeftStockSummary()
        {
            var stockItems = await _context.Items
                .Where(i => i.Code.StartsWith("DYED-WARP-") || 
                            i.Code.StartsWith("DYED-WEFT-") || 
                            i.Code.StartsWith("GREY-WARP-") || 
                            i.Code.StartsWith("GREY-WEFT-"))
                .ToListAsync();

            var warpList = new List<object>();
            var weftList = new List<object>();

            foreach (var item in stockItems)
            {
                var lastLedger = await _context.StockLedgers
                    .Where(sl => sl.ItemId == item.Id)
                    .OrderByDescending(sl => sl.TransactionDate)
                    .ThenByDescending(sl => sl.CreatedAt)
                    .FirstOrDefaultAsync();

                decimal balanceQty = lastLedger?.BalanceQty ?? 0;
                decimal balanceWeight = lastLedger?.BalanceWeight ?? 0;

                if (balanceQty == 0 && balanceWeight == 0) continue;

                var parts = item.Code.Split('-');
                bool isDyed = parts[0] == "DYED";
                bool isWarp = parts[1] == "WARP";

                string typeFormula = "";
                string color = "Grey (Raw)";

                if (isDyed)
                {
                    var nameParts = item.Name.Split('|');
                    typeFormula = nameParts.Length > 1 ? nameParts[1].Trim() : "Unknown";
                    color = nameParts.Length > 2 ? nameParts[2].Trim() : "RAW";
                }
                else
                {
                    var nameParts = item.Name.Split('|');
                    typeFormula = nameParts.Length > 1 ? nameParts[1].Trim() : "Unknown";
                }

                var record = new
                {
                    type = typeFormula,
                    color = color,
                    qty = balanceQty,
                    weight = balanceWeight
                };

                if (isWarp) warpList.Add(record);
                else weftList.Add(record);
            }

            var groupedWarp = warpList
                .Cast<dynamic>()
                .GroupBy(w => new { w.type, w.color })
                .Select(g => new
                {
                    type = g.Key.type,
                    color = g.Key.color,
                    qty = g.Sum(x => (decimal)x.qty),
                    weight = g.Sum(x => (decimal)x.weight)
                })
                .OrderBy(g => g.type)
                .ToList();

            var groupedWeft = weftList
                .Cast<dynamic>()
                .GroupBy(w => new { w.type, w.color })
                .Select(g => new
                {
                    type = g.Key.type,
                    color = g.Key.color,
                    qty = g.Sum(x => (decimal)x.qty),
                    weight = g.Sum(x => (decimal)x.weight)
                })
                .OrderBy(g => g.type)
                .ToList();

            return Ok(new { warpStock = groupedWarp, weftStock = groupedWeft });
        }
    }
}
