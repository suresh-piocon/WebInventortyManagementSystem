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
    public class StockInwardController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public StockInwardController(InventoryDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        [HttpGet]
        public async Task<IActionResult> GetInwards()
        {
            var inwards = await _context.StockInwards
                .Include(si => si.Supplier)
                .Include(si => si.Details)
                    .ThenInclude(d => d.Item)
                .OrderByDescending(si => si.InwardDate)
                .ToListAsync();

            return Ok(inwards);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetInwardById(Guid id)
        {
            var inward = await _context.StockInwards
                .Include(si => si.Supplier)
                .Include(si => si.Details)
                    .ThenInclude(d => d.Item)
                .FirstOrDefaultAsync(si => si.Id == id);

            if (inward == null) return NotFound();
            return Ok(inward);
        }

        [HttpGet("next-batch-seq")]
        public async Task<IActionResult> GetNextBatchSequence([FromQuery] string prefix)
        {
            if (string.IsNullOrEmpty(prefix))
            {
                return BadRequest("Prefix is required.");
            }

            var batchNumbers = await _context.StockInwardDetails
                .Where(d => d.BatchNo.StartsWith(prefix))
                .Select(d => d.BatchNo)
                .ToListAsync();

            int maxSeq = 0;
            foreach (var batch in batchNumbers)
            {
                var suffix = batch.Substring(prefix.Length);
                if (int.TryParse(suffix, out var seq))
                {
                    if (seq > maxSeq)
                    {
                        maxSeq = seq;
                    }
                }
            }

            return Ok(new { nextSeq = maxSeq + 1 });
        }

        [HttpPost]
        public async Task<IActionResult> CreateInward([FromBody] StockInwardPostDto dto)
        {
            if (dto == null || dto.Details == null || !dto.Details.Any())
            {
                return BadRequest("Inward details are required.");
            }

            var userId = _currentUserService.UserId;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Generate Inward Number
                var inwardNo = await GenerateInwardNumberAsync();

                var stockInward = new StockInward
                {
                    Id = Guid.NewGuid(),
                    InwardNo = inwardNo,
                    InwardDate = dto.InwardDate.ToUniversalTime(),
                    SupplierId = dto.SupplierId,
                    InvoiceNo = dto.InvoiceNo,
                    InvoiceDate = dto.InvoiceDate?.ToUniversalTime(),
                    CreatedBy = userId,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                _context.StockInwards.Add(stockInward);

                // Fetch supplier details for QR code
                var supplier = await _context.Suppliers.FindAsync(dto.SupplierId);
                var supplierName = supplier?.Name ?? "Unknown Supplier";

                foreach (var detailDto in dto.Details)
                {
                    var item = await _context.Items.FindAsync(detailDto.ItemId);
                    if (item == null)
                    {
                        return BadRequest($"Item ID {detailDto.ItemId} not found.");
                    }

                    // 2. Generate Tracking Number for each detail line
                    var trackingNo = await GenerateTrackingNumberAsync();

                    var detail = new StockInwardDetail
                    {
                        Id = Guid.NewGuid(),
                        StockInwardId = stockInward.Id,
                        ItemId = detailDto.ItemId,
                        Color = detailDto.Color,
                        Design = detailDto.Design,
                        Size = detailDto.Size,
                        BatchNo = detailDto.BatchNo,
                        Quantity = detailDto.Quantity,
                        Rate = detailDto.Rate,
                        Amount = Math.Round(detailDto.Quantity * detailDto.Rate, 2),
                        TrackingNo = trackingNo
                    };

                    _context.StockInwardDetails.Add(detail);

                    // 3. Generate QR Code containing JSON metadata
                    var qrDataObj = new
                    {
                        TrackingNo = trackingNo,
                        Supplier = supplierName,
                        Item = item.Name,
                        Batch = detailDto.BatchNo,
                        Quantity = detailDto.Quantity,
                        InwardDate = dto.InwardDate.ToUniversalTime().ToString("yyyy-MM-dd")
                    };
                    var qrJson = JsonSerializer.Serialize(qrDataObj);

                    var qrCodeMaster = new QRCodeMaster
                    {
                        Id = Guid.NewGuid(),
                        QRCode = qrJson,
                        TrackingNo = trackingNo,
                        SupplierId = dto.SupplierId,
                        ItemId = detailDto.ItemId,
                        BatchNo = detailDto.BatchNo,
                        Quantity = detailDto.Quantity,
                        InwardDate = dto.InwardDate.ToUniversalTime(),
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.QRCodeMasters.Add(qrCodeMaster);

                    // 4. Barcode Master Registration (Batch vs Unique)
                    if (item.BarcodeType.Equals("Unique", StringComparison.OrdinalIgnoreCase))
                    {
                        // Generate a barcode for each piece
                        int qtyInt = (int)Math.Ceiling(detailDto.Quantity);
                        for (int i = 1; i <= qtyInt; i++)
                        {
                            var uniqueBarcode = await GenerateUniqueBarcodeAsync();
                            var barcodeMaster = new BarcodeMaster
                            {
                                Id = Guid.NewGuid(),
                                Barcode = uniqueBarcode,
                                ItemId = detailDto.ItemId,
                                BatchNo = detailDto.BatchNo,
                                TrackingNo = trackingNo,
                                Type = "Unique",
                                ImageUrl = detailDto.ImageUrl, // Photo captured for this row
                                IsUsed = false,
                                CreatedAt = DateTimeOffset.UtcNow
                            };
                            _context.BarcodeMasters.Add(barcodeMaster);
                        }
                    }
                    else // Batch Barcode
                    {
                        var batchBarcode = await GenerateBatchBarcodeAsync();
                        var barcodeMaster = new BarcodeMaster
                        {
                            Id = Guid.NewGuid(),
                            Barcode = batchBarcode,
                            ItemId = detailDto.ItemId,
                            BatchNo = detailDto.BatchNo,
                            TrackingNo = trackingNo,
                            Type = "Batch",
                            ImageUrl = detailDto.ImageUrl, // Photo captured for this batch
                            IsUsed = false,
                            CreatedAt = DateTimeOffset.UtcNow
                        };
                        _context.BarcodeMasters.Add(barcodeMaster);
                    }

                    // 5. Update Stock Ledger
                    var currentBalance = await _context.StockLedgers
                        .Where(l => l.ItemId == detailDto.ItemId)
                        .OrderByDescending(l => l.TransactionDate)
                        .ThenByDescending(l => l.CreatedAt)
                        .Select(l => l.BalanceQty)
                        .FirstOrDefaultAsync();

                    var stockLedger = new StockLedger
                    {
                        Id = Guid.NewGuid(),
                        ItemId = detailDto.ItemId,
                        TransactionDate = dto.InwardDate.ToUniversalTime(),
                        TransactionType = "Purchase",
                        ReferenceNo = inwardNo,
                        BatchNo = detailDto.BatchNo,
                        TrackingNo = trackingNo,
                        InwardQty = detailDto.Quantity,
                        OutwardQty = 0,
                        BalanceQty = currentBalance + detailDto.Quantity,
                        UnitPrice = detailDto.Rate,
                        CreatedAt = DateTimeOffset.UtcNow
                    };
                    _context.StockLedgers.Add(stockLedger);
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(stockInward);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                var innerMsg = ex.InnerException != null ? $" | Inner: {ex.InnerException.Message}" : "";
                return StatusCode(500, $"An error occurred while saving stock inward: {ex.Message}{innerMsg}");
            }
        }

        private async Task<string> GenerateInwardNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"INW-{year}-";
            var dbRecords = await _context.StockInwards
                .Where(si => si.InwardNo.StartsWith(prefix))
                .Select(si => si.InwardNo)
                .ToListAsync();

            var localRecords = _context.StockInwards.Local
                .Where(si => si.InwardNo.StartsWith(prefix))
                .Select(si => si.InwardNo);

            var allRecords = dbRecords.Concat(localRecords).Distinct();

            int maxNum = 0;
            foreach (var r in allRecords)
            {
                var parts = r.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var num))
                {
                    if (num > maxNum) maxNum = num;
                }
            }
            return $"{prefix}{(maxNum + 1):D6}";
        }

        private async Task<string> GenerateTrackingNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"TRK-{year}-";
            var dbRecords = await _context.StockInwardDetails
                .Where(sid => sid.TrackingNo.StartsWith(prefix))
                .Select(sid => sid.TrackingNo)
                .ToListAsync();

            var localRecords = _context.StockInwardDetails.Local
                .Where(sid => sid.TrackingNo.StartsWith(prefix))
                .Select(sid => sid.TrackingNo);

            var allRecords = dbRecords.Concat(localRecords).Distinct();

            int maxNum = 0;
            foreach (var r in allRecords)
            {
                var parts = r.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var num))
                {
                    if (num > maxNum) maxNum = num;
                }
            }
            return $"{prefix}{(maxNum + 1):D6}";
        }

        private async Task<string> GenerateUniqueBarcodeAsync()
        {
            var dbBarcodes = await _context.BarcodeMasters
                .Where(b => b.Barcode.StartsWith("ITEM"))
                .Select(b => b.Barcode)
                .ToListAsync();

            var localBarcodes = _context.BarcodeMasters.Local
                .Where(b => b.Barcode.StartsWith("ITEM"))
                .Select(b => b.Barcode);

            var allBarcodes = dbBarcodes.Concat(localBarcodes).Distinct();

            int maxNum = 0;
            foreach (var b in allBarcodes)
            {
                var numStr = b.Replace("ITEM", "");
                if (int.TryParse(numStr, out var num))
                {
                    if (num > maxNum) maxNum = num;
                }
            }
            return $"ITEM{(maxNum + 1):D6}";
        }

        private async Task<string> GenerateBatchBarcodeAsync()
        {
            var dbBarcodes = await _context.BarcodeMasters
                .Where(b => b.Barcode.StartsWith("BATCH"))
                .Select(b => b.Barcode)
                .ToListAsync();

            var localBarcodes = _context.BarcodeMasters.Local
                .Where(b => b.Barcode.StartsWith("BATCH"))
                .Select(b => b.Barcode);

            var allBarcodes = dbBarcodes.Concat(localBarcodes).Distinct();

            int maxNum = 0;
            foreach (var b in allBarcodes)
            {
                var numStr = b.Replace("BATCH", "");
                if (int.TryParse(numStr, out var num))
                {
                    if (num > maxNum) maxNum = num;
                }
            }
            return $"BATCH{(maxNum + 1):D6}";
        }
    }
}
