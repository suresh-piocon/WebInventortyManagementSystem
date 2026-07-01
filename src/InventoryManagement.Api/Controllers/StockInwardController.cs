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
        public async Task<IActionResult> GetInwards(
            [FromQuery] DateTimeOffset? startDate,
            [FromQuery] DateTimeOffset? endDate)
        {
            var query = _context.StockInwards
                .Include(si => si.Supplier)
                .Include(si => si.Details)
                    .ThenInclude(d => d.Item)
                .AsQueryable();

            if (startDate.HasValue)
            {
                query = query.Where(si => si.InwardDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(si => si.InwardDate <= endDate.Value);
            }

            var inwards = await query
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

            var maxDbInward = await _context.StockInwards
                .Where(si => si.InwardNo.ToUpper().StartsWith(prefix))
                .OrderByDescending(si => si.InwardNo)
                .Select(si => si.InwardNo)
                .FirstOrDefaultAsync();

            var maxLocalInward = _context.StockInwards.Local
                .Where(si => si.InwardNo.ToUpper().StartsWith(prefix))
                .OrderByDescending(si => si.InwardNo)
                .Select(si => si.InwardNo)
                .FirstOrDefault();

            string? maxInward = null;
            if (maxDbInward != null && maxLocalInward != null)
            {
                maxInward = string.Compare(maxDbInward, maxLocalInward, StringComparison.OrdinalIgnoreCase) > 0 ? maxDbInward : maxLocalInward;
            }
            else
            {
                maxInward = maxDbInward ?? maxLocalInward;
            }

            int nextNum = 1;
            if (maxInward != null)
            {
                var parts = maxInward.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var num))
                {
                    nextNum = num + 1;
                }
            }

            return $"{prefix}{nextNum:D6}";
        }

        private async Task<string> GenerateTrackingNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"TRK-{year}-";

            var maxDbTracking = await _context.StockInwardDetails
                .Where(sid => sid.TrackingNo.ToUpper().StartsWith(prefix))
                .OrderByDescending(sid => sid.TrackingNo)
                .Select(sid => sid.TrackingNo)
                .FirstOrDefaultAsync();

            var maxDbQrTracking = await _context.QRCodeMasters
                .Where(qm => qm.TrackingNo.ToUpper().StartsWith(prefix))
                .OrderByDescending(qm => qm.TrackingNo)
                .Select(qm => qm.TrackingNo)
                .FirstOrDefaultAsync();

            var maxLocalTracking = _context.StockInwardDetails.Local
                .Where(sid => sid.TrackingNo.ToUpper().StartsWith(prefix))
                .OrderByDescending(sid => sid.TrackingNo)
                .Select(sid => sid.TrackingNo)
                .FirstOrDefault();

            var maxLocalQrTracking = _context.QRCodeMasters.Local
                .Where(qm => qm.TrackingNo.ToUpper().StartsWith(prefix))
                .OrderByDescending(qm => qm.TrackingNo)
                .Select(qm => qm.TrackingNo)
                .FirstOrDefault();

            var trackingNumbersList = new List<string?> { maxDbTracking, maxDbQrTracking, maxLocalTracking, maxLocalQrTracking };
            string? maxTracking = null;

            foreach (var trk in trackingNumbersList)
            {
                if (trk == null) continue;
                if (maxTracking == null)
                {
                    maxTracking = trk;
                }
                else if (string.Compare(trk, maxTracking, StringComparison.OrdinalIgnoreCase) > 0)
                {
                    maxTracking = trk;
                }
            }

            int nextNum = 1;
            if (maxTracking != null)
            {
                var parts = maxTracking.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var num))
                {
                    nextNum = num + 1;
                }
            }

            return $"{prefix}{nextNum:D6}";
        }

        private async Task<string> GenerateUniqueBarcodeAsync()
        {
            var maxDbBarcode = await _context.BarcodeMasters
                .Where(b => b.Barcode.ToUpper().StartsWith("ITEM"))
                .OrderByDescending(b => b.Barcode)
                .Select(b => b.Barcode)
                .FirstOrDefaultAsync();

            var maxLocalBarcode = _context.BarcodeMasters.Local
                .Where(b => b.Barcode.ToUpper().StartsWith("ITEM"))
                .OrderByDescending(b => b.Barcode)
                .Select(b => b.Barcode)
                .FirstOrDefault();

            string? maxBarcode = null;
            if (maxDbBarcode != null && maxLocalBarcode != null)
            {
                maxBarcode = string.Compare(maxDbBarcode, maxLocalBarcode, StringComparison.OrdinalIgnoreCase) > 0 ? maxDbBarcode : maxLocalBarcode;
            }
            else
            {
                maxBarcode = maxDbBarcode ?? maxLocalBarcode;
            }

            int nextNum = 1;
            if (maxBarcode != null)
            {
                var numStr = maxBarcode.ToUpper().Replace("ITEM", "");
                if (int.TryParse(numStr, out var num))
                {
                    nextNum = num + 1;
                }
            }

            return $"ITEM{nextNum:D6}";
        }

        private async Task<string> GenerateBatchBarcodeAsync()
        {
            var maxDbBarcode = await _context.BarcodeMasters
                .Where(b => b.Barcode.ToUpper().StartsWith("BATCH"))
                .OrderByDescending(b => b.Barcode)
                .Select(b => b.Barcode)
                .FirstOrDefaultAsync();

            var maxLocalBarcode = _context.BarcodeMasters.Local
                .Where(b => b.Barcode.ToUpper().StartsWith("BATCH"))
                .OrderByDescending(b => b.Barcode)
                .Select(b => b.Barcode)
                .FirstOrDefault();

            string? maxBarcode = null;
            if (maxDbBarcode != null && maxLocalBarcode != null)
            {
                maxBarcode = string.Compare(maxDbBarcode, maxLocalBarcode, StringComparison.OrdinalIgnoreCase) > 0 ? maxDbBarcode : maxLocalBarcode;
            }
            else
            {
                maxBarcode = maxDbBarcode ?? maxLocalBarcode;
            }

            int nextNum = 1;
            if (maxBarcode != null)
            {
                var numStr = maxBarcode.ToUpper().Replace("BATCH", "");
                if (int.TryParse(numStr, out var num))
                {
                    nextNum = num + 1;
                }
            }

            return $"BATCH{nextNum:D6}";
        }
    }
}
