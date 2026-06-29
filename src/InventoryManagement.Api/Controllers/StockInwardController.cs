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
                    InwardDate = dto.InwardDate,
                    SupplierId = dto.SupplierId,
                    InvoiceNo = dto.InvoiceNo,
                    InvoiceDate = dto.InvoiceDate,
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
                        InwardDate = dto.InwardDate.ToString("yyyy-MM-dd")
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
                        InwardDate = dto.InwardDate,
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
                        TransactionDate = dto.InwardDate,
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
                return StatusCode(500, $"An error occurred while saving stock inward: {ex.Message}");
            }
        }

        private async Task<string> GenerateInwardNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"INW-{year}-";
            var lastRecord = await _context.StockInwards
                .Where(si => si.InwardNo.StartsWith(prefix))
                .OrderByDescending(si => si.InwardNo)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastRecord != null)
            {
                var parts = lastRecord.InwardNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"{prefix}{nextNum:D6}";
        }

        private async Task<string> GenerateTrackingNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"TRK-{year}-";
            var lastRecord = await _context.StockInwardDetails
                .Where(sid => sid.TrackingNo.StartsWith(prefix))
                .OrderByDescending(sid => sid.TrackingNo)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastRecord != null)
            {
                var parts = lastRecord.TrackingNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"{prefix}{nextNum:D6}";
        }

        private async Task<string> GenerateUniqueBarcodeAsync()
        {
            var lastBarcode = await _context.BarcodeMasters
                .Where(b => b.Barcode.StartsWith("ITEM"))
                .OrderByDescending(b => b.Barcode)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastBarcode != null)
            {
                var numStr = lastBarcode.Barcode.Replace("ITEM", "");
                if (int.TryParse(numStr, out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"ITEM{nextNum:D6}";
        }

        private async Task<string> GenerateBatchBarcodeAsync()
        {
            var lastBarcode = await _context.BarcodeMasters
                .Where(b => b.Barcode.StartsWith("BATCH"))
                .OrderByDescending(b => b.Barcode)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastBarcode != null)
            {
                var numStr = lastBarcode.Barcode.Replace("BATCH", "");
                if (int.TryParse(numStr, out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"BATCH{nextNum:D6}";
        }
    }
}
