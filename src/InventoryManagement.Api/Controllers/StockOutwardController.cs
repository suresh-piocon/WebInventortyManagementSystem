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
    public class StockOutwardController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public StockOutwardController(InventoryDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        [HttpGet]
        public async Task<IActionResult> GetOutwards()
        {
            var data = await _context.StockOutwards
                .Include(so => so.Details)
                    .ThenInclude(d => d.Item)
                .OrderByDescending(so => so.OutwardDate)
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("resolve/{code}")]
        public async Task<IActionResult> ResolveBarcodeOrQR(string code)
        {
            string? trackingNo = null;
            string? batchNo = null;
            Guid? itemId = null;
            string? imageUrl = null;

            // 1. Try parsing code as JSON (QR Code payload format)
            try
            {
                using var jsonDoc = JsonDocument.Parse(code);
                var root = jsonDoc.RootElement;
                if (root.TryGetProperty("TrackingNo", out var trackingProp))
                {
                    trackingNo = trackingProp.GetString();
                }
                if (root.TryGetProperty("Batch", out var batchProp))
                {
                    batchNo = batchProp.GetString();
                }
            }
            catch
            {
                // Not a JSON string - search in BarcodeMaster
            }

            // 2. Query BarcodeMaster if not resolved from JSON
            if (string.IsNullOrEmpty(trackingNo))
            {
                var barcodeMaster = await _context.BarcodeMasters
                    .Include(b => b.Item)
                    .FirstOrDefaultAsync(b => b.Barcode == code);

                if (barcodeMaster != null)
                {
                    trackingNo = barcodeMaster.TrackingNo;
                    batchNo = barcodeMaster.BatchNo;
                    itemId = barcodeMaster.ItemId;
                    imageUrl = barcodeMaster.ImageUrl;
                }
                else
                {
                    // Maybe code is the tracking number itself
                    var detailLine = await _context.StockInwardDetails
                        .Include(d => d.Item)
                        .FirstOrDefaultAsync(d => d.TrackingNo == code);

                    if (detailLine != null)
                    {
                        trackingNo = detailLine.TrackingNo;
                        batchNo = detailLine.BatchNo;
                        itemId = detailLine.ItemId;
                    }
                }
            }
            else
            {
                // Resolved from QR JSON, let's load item and image from BarcodeMaster
                var barcodeMaster = await _context.BarcodeMasters
                    .Include(b => b.Item)
                    .FirstOrDefaultAsync(b => b.TrackingNo == trackingNo && b.BatchNo == batchNo);
                
                if (barcodeMaster != null)
                {
                    itemId = barcodeMaster.ItemId;
                    imageUrl = barcodeMaster.ImageUrl;
                }
                else
                {
                    var detailLine = await _context.StockInwardDetails
                        .FirstOrDefaultAsync(d => d.TrackingNo == trackingNo);
                    if (detailLine != null)
                    {
                        itemId = detailLine.ItemId;
                    }
                }
            }

            if (string.IsNullOrEmpty(trackingNo) || itemId == null)
            {
                return NotFound("Could not resolve barcode or QR code.");
            }

            var item = await _context.Items
                .Include(i => i.Unit)
                .FirstOrDefaultAsync(i => i.Id == itemId);

            if (item == null) return NotFound("Item associated with this barcode was not found.");

            // 3. Calculate Available Stock Balance of this specific batch/tracking
            var inwardQty = await _context.StockLedgers
                .Where(l => l.ItemId == itemId && l.TrackingNo == trackingNo && l.BatchNo == batchNo)
                .SumAsync(l => l.InwardQty);

            var outwardQty = await _context.StockLedgers
                .Where(l => l.ItemId == itemId && l.TrackingNo == trackingNo && l.BatchNo == batchNo)
                .SumAsync(l => l.OutwardQty);

            var availableQty = inwardQty - outwardQty;

            // Get the purchase price from the inward ledger entry
            var rate = await _context.StockLedgers
                .Where(l => l.ItemId == itemId && l.TrackingNo == trackingNo && l.InwardQty > 0)
                .Select(l => l.UnitPrice)
                .FirstOrDefaultAsync();

            var result = new ScannedItemDto
            {
                ItemId = item.Id,
                ItemCode = item.Code,
                ItemName = item.Name,
                UnitCode = item.Unit?.Code ?? "PCS",
                BatchNo = batchNo ?? string.Empty,
                TrackingNo = trackingNo,
                Barcode = code,
                AvailableQuantity = availableQty,
                Rate = rate, // Defaults to the purchase price
                ImageUrl = imageUrl
            };

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateOutward([FromBody] StockOutwardPostDto dto)
        {
            if (dto == null || dto.Details == null || !dto.Details.Any())
            {
                return BadRequest("Outward details are required.");
            }

            var userId = _currentUserService.UserId;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Generate Outward Number
                var outwardNo = await GenerateOutwardNumberAsync();

                var stockOutward = new StockOutward
                {
                    Id = Guid.NewGuid(),
                    OutwardNo = outwardNo,
                    OutwardDate = dto.OutwardDate.ToUniversalTime(),
                    CustomerName = dto.CustomerName,
                    ReferenceNo = dto.ReferenceNo,
                    CreatedBy = userId,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.StockOutwards.Add(stockOutward);

                foreach (var detailDto in dto.Details)
                {
                    // 2. Validate current available stock balance of this batch (Negative Stock Prevention)
                    var inwardQty = await _context.StockLedgers
                        .Where(l => l.ItemId == detailDto.ItemId && l.TrackingNo == detailDto.TrackingNo && l.BatchNo == detailDto.BatchNo)
                        .SumAsync(l => l.InwardQty);

                    var outwardQty = await _context.StockLedgers
                        .Where(l => l.ItemId == detailDto.ItemId && l.TrackingNo == detailDto.TrackingNo && l.BatchNo == detailDto.BatchNo)
                        .SumAsync(l => l.OutwardQty);

                    var currentBatchBalance = inwardQty - outwardQty;

                    if (currentBatchBalance < detailDto.Quantity)
                    {
                        var item = await _context.Items.FindAsync(detailDto.ItemId);
                        return BadRequest($"Insufficient stock for item '{item?.Name}' (Batch: {detailDto.BatchNo}, Tracking: {detailDto.TrackingNo}). Available: {currentBatchBalance}, Requested: {detailDto.Quantity}");
                    }

                    var detail = new StockOutwardDetail
                    {
                        Id = Guid.NewGuid(),
                        StockOutwardId = stockOutward.Id,
                        ItemId = detailDto.ItemId,
                        BatchNo = detailDto.BatchNo,
                        TrackingNo = detailDto.TrackingNo,
                        Barcode = detailDto.Barcode,
                        Quantity = detailDto.Quantity,
                        Rate = detailDto.Rate,
                        Amount = Math.Round(detailDto.Quantity * detailDto.Rate, 2)
                    };

                    _context.StockOutwardDetails.Add(detail);

                    // 3. Mark barcode as used if it is a unique barcode
                    var barcodeMaster = await _context.BarcodeMasters
                        .FirstOrDefaultAsync(b => b.Barcode == detailDto.Barcode);
                    if (barcodeMaster != null)
                    {
                        barcodeMaster.IsUsed = true;
                        _context.BarcodeMasters.Update(barcodeMaster);
                    }

                    // 4. Update Stock Ledger
                    var currentOverallBalance = await _context.StockLedgers
                        .Where(l => l.ItemId == detailDto.ItemId)
                        .OrderByDescending(l => l.TransactionDate)
                        .ThenByDescending(l => l.CreatedAt)
                        .Select(l => l.BalanceQty)
                        .FirstOrDefaultAsync();

                    var stockLedger = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        ItemId = detailDto.ItemId,
                        TransactionDate = dto.OutwardDate.ToUniversalTime(),
                        TransactionType = "Sales",
                        ReferenceNo = outwardNo,
                        BatchNo = detailDto.BatchNo,
                        TrackingNo = detailDto.TrackingNo,
                        InwardQty = 0,
                        OutwardQty = detailDto.Quantity,
                        BalanceQty = currentOverallBalance - detailDto.Quantity,
                        UnitPrice = detailDto.Rate,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.StockLedgers.Add(stockLedger);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(stockOutward);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"An error occurred while saving stock outward: {ex.Message}");
            }
        }

        private async Task<string> GenerateOutwardNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"OUT-{year}-";
            var lastRecord = await _context.StockOutwards
                .Where(so => so.OutwardNo.StartsWith(prefix))
                .OrderByDescending(so => so.OutwardNo)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastRecord != null)
            {
                var parts = lastRecord.OutwardNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"{prefix}{nextNum:D6}";
        }
    }
}
