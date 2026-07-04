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
    public class ProformaInvoiceController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly ICurrentUserService _currentUserService;

        public ProformaInvoiceController(InventoryDbContext context, ICurrentUserService currentUserService)
        {
            _context = context;
            _currentUserService = currentUserService;
        }

        [HttpGet]
        public async Task<IActionResult> GetProformas()
        {
            var data = await _context.ProformaInvoices
                .Include(p => p.Customer)
                .Include(p => p.Details)
                    .ThenInclude(d => d.Item)
                .OrderByDescending(p => p.ProformaDate)
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetProforma(Guid id)
        {
            var data = await _context.ProformaInvoices
                .Include(p => p.Customer)
                .Include(p => p.Details)
                    .ThenInclude(d => d.Barcodes)
                .FirstOrDefaultAsync(p => p.Id == id);
            if (data == null) return NotFound();
            return Ok(data);
        }

        [HttpGet("resolve/{code}")]
        public async Task<IActionResult> ResolveBarcodeOrQR(string code)
        {
            string? trackingNo = null;
            string? batchNo = null;
            Guid? itemId = null;
            string? imageUrl = null;
            string itemType = "Batch";

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
                    if (barcodeMaster.Type == "Unique" && barcodeMaster.IsUsed)
                    {
                        return BadRequest("This unique barcode has already been issued.");
                    }

                    trackingNo = barcodeMaster.TrackingNo;
                    batchNo = barcodeMaster.BatchNo;
                    itemId = barcodeMaster.ItemId;
                    imageUrl = barcodeMaster.ImageUrl;
                    itemType = barcodeMaster.Type;

                    // Fallback to any barcode in the same batch/tracking that has an image
                    if (string.IsNullOrEmpty(imageUrl))
                    {
                        imageUrl = await _context.BarcodeMasters
                            .Where(b => b.TrackingNo == trackingNo && b.BatchNo == batchNo && b.ImageUrl != null && b.ImageUrl != "")
                            .Select(b => b.ImageUrl)
                            .FirstOrDefaultAsync();
                    }
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
                // Resolved from QR JSON, load item and image from BarcodeMaster
                var barcodeMaster = await _context.BarcodeMasters
                    .Include(b => b.Item)
                    .Where(b => b.TrackingNo == trackingNo && b.BatchNo == batchNo && b.ImageUrl != null && b.ImageUrl != "")
                    .FirstOrDefaultAsync()
                    ?? await _context.BarcodeMasters
                    .Include(b => b.Item)
                    .Where(b => b.TrackingNo == trackingNo && b.BatchNo == batchNo)
                    .FirstOrDefaultAsync();
                
                if (barcodeMaster != null)
                {
                    if (barcodeMaster.Type == "Unique" && barcodeMaster.IsUsed)
                    {
                        return BadRequest("This unique barcode has already been issued.");
                    }

                    itemId = barcodeMaster.ItemId;
                    imageUrl = barcodeMaster.ImageUrl;
                    itemType = barcodeMaster.Type;
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
                Rate = rate,
                ImageUrl = imageUrl,
                Type = itemType,
                GSTPercent = item.GSTPercent,
                HSNCode = item.HSNCode
            };

            return Ok(result);
        }

        [HttpPost]
        public async Task<IActionResult> CreateProforma([FromBody] ProformaInvoicePostDto dto)
        {
            if (dto == null || dto.Details == null || !dto.Details.Any())
            {
                return BadRequest("Proforma details are required.");
            }

            var userId = _currentUserService.UserId;

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. Generate Numbers
                var proformaNo = await GenerateProformaNumberAsync();
                var outwardNo = await GenerateOutwardNumberAsync();

                // 2. Create StockOutward header for reports compatibility
                var stockOutward = new StockOutward
                {
                    Id = Guid.NewGuid(),
                    OutwardNo = outwardNo,
                    OutwardDate = dto.ProformaDate.ToUniversalTime(),
                    CustomerName = dto.CustomerName,
                    ReferenceNo = proformaNo, // Link to Proforma No
                    CreatedBy = userId,
                    CreatedAt = DateTimeOffset.UtcNow
                };
                _context.StockOutwards.Add(stockOutward);

                // 3. Create ProformaInvoice header
                var proforma = new ProformaInvoice
                {
                    Id = Guid.NewGuid(),
                    CustomerId = dto.CustomerId,
                    ProformaNo = proformaNo,
                    ProformaDate = dto.ProformaDate.ToUniversalTime(),
                    CustomerName = dto.CustomerName,
                    MobileNo = dto.MobileNo,
                    Address = dto.Address,
                    GSTIN = dto.GSTIN,
                    State = dto.State,
                    TaxType = dto.TaxType,
                    TotalQty = dto.Details.Sum(d => d.Quantity),
                    TotalTaxableValue = dto.Details.Sum(d => d.TaxableValue),
                    IsConverted = true, // Submitted immediately
                    ConvertedDate = DateTimeOffset.UtcNow,
                    ConvertedStockOutwardId = stockOutward.Id,
                    CreatedBy = userId,
                    CreatedAt = DateTimeOffset.UtcNow
                };

                // Tax summaries
                decimal totalCGST = 0;
                decimal totalSGST = 0;
                decimal totalIGST = 0;
                var totalGst = dto.Details.Sum(d => d.GSTAmount);
                if (dto.TaxType == "Intra-State")
                {
                    totalCGST = Math.Round(totalGst / 2, 2);
                    totalSGST = totalGst - totalCGST;
                }
                else
                {
                    totalIGST = totalGst;
                }
                proforma.TotalCGST = totalCGST;
                proforma.TotalSGST = totalSGST;
                proforma.TotalIGST = totalIGST;
                proforma.GrandTotal = proforma.TotalTaxableValue + totalCGST + totalSGST + totalIGST;
                proforma.NetAmount = Math.Round(proforma.GrandTotal, 0);
                proforma.RoundOff = proforma.NetAmount - proforma.GrandTotal;

                _context.ProformaInvoices.Add(proforma);

                // To validate batch balance in this transaction
                var committedOutwardQtyByTracking = new Dictionary<string, decimal>();

                // 4. Process details
                foreach (var detailDto in dto.Details)
                {
                    var proformaDetail = new ProformaInvoiceDetail
                    {
                        Id = Guid.NewGuid(),
                        ProformaInvoiceId = proforma.Id,
                        ItemId = detailDto.ItemId,
                        Particulars = detailDto.Particulars,
                        HSNCode = detailDto.HSNCode,
                        Quantity = detailDto.Quantity,
                        Rate = detailDto.Rate,
                        DiscountPercent = detailDto.DiscountPercent,
                        DiscountAmount = detailDto.DiscountAmount,
                        TaxableValue = detailDto.TaxableValue,
                        GSTPercent = detailDto.GSTPercent,
                        GSTAmount = detailDto.GSTAmount,
                        LineTotal = detailDto.LineTotal,
                        BarcodeList = string.Join(",", detailDto.ScannedBarcodes.Select(b => b.Barcode))
                    };
                    _context.ProformaInvoiceDetails.Add(proformaDetail);

                    // Process each scanned barcode in this detail line
                    foreach (var barcodeDto in detailDto.ScannedBarcodes)
                    {
                        var barcodeDetail = new ProformaInvoiceDetailBarcode
                        {
                            Id = Guid.NewGuid(),
                            ProformaInvoiceDetailId = proformaDetail.Id,
                            Barcode = barcodeDto.Barcode,
                            BatchNo = barcodeDto.BatchNo,
                            TrackingNo = barcodeDto.TrackingNo,
                            Quantity = barcodeDto.Quantity
                        };
                        _context.ProformaInvoiceDetailBarcodes.Add(barcodeDetail);

                        // Look up BarcodeMaster to see if this is a Unique barcode
                        var barcodeMaster = await _context.BarcodeMasters
                            .FirstOrDefaultAsync(b => b.Barcode == barcodeDto.Barcode);

                        bool isUniqueBarcode = barcodeMaster?.Type == "Unique";

                        if (isUniqueBarcode)
                        {
                            if (barcodeMaster!.IsUsed)
                            {
                                return BadRequest($"Unique barcode '{barcodeDto.Barcode}' has already been issued.");
                            }
                            barcodeMaster.IsUsed = true;
                            _context.BarcodeMasters.Update(barcodeMaster);
                        }
                        else
                        {
                            // Validate batch quantity available
                            var inwardQty = await _context.StockLedgers
                                .Where(l => l.ItemId == detailDto.ItemId && l.TrackingNo == barcodeDto.TrackingNo && l.BatchNo == barcodeDto.BatchNo)
                                .SumAsync(l => l.InwardQty);

                            var outwardQty = await _context.StockLedgers
                                .Where(l => l.ItemId == detailDto.ItemId && l.TrackingNo == barcodeDto.TrackingNo && l.BatchNo == barcodeDto.BatchNo)
                                .SumAsync(l => l.OutwardQty);

                            var trackingKey = $"{barcodeDto.TrackingNo}|{barcodeDto.BatchNo}";
                            var alreadyCommitted = committedOutwardQtyByTracking.TryGetValue(trackingKey, out var prev) ? prev : 0m;
                            var currentBatchBalance = inwardQty - outwardQty - alreadyCommitted;

                            if (currentBatchBalance < barcodeDto.Quantity)
                            {
                                return BadRequest($"Insufficient stock for batch {barcodeDto.BatchNo} of item {detailDto.Particulars}. Available: {currentBatchBalance}, Requested: {barcodeDto.Quantity}");
                            }

                            committedOutwardQtyByTracking[trackingKey] = alreadyCommitted + barcodeDto.Quantity;
                        }

                        // Create StockOutwardDetail for reports compatibility
                        var outwardDetail = new StockOutwardDetail
                        {
                            Id = Guid.NewGuid(),
                            StockOutwardId = stockOutward.Id,
                            ItemId = detailDto.ItemId,
                            BatchNo = barcodeDto.BatchNo,
                            TrackingNo = barcodeDto.TrackingNo,
                            Barcode = barcodeDto.Barcode,
                            Quantity = barcodeDto.Quantity,
                            Rate = detailDto.Rate,
                            Amount = Math.Round(barcodeDto.Quantity * detailDto.Rate, 2)
                        };
                        _context.StockOutwardDetails.Add(outwardDetail);

                        // Create StockLedger entry to deduct stock (associated with outwardNo for compatibility)
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
                            TransactionDate = dto.ProformaDate.ToUniversalTime(),
                            TransactionType = "Sales",
                            ReferenceNo = outwardNo, // Compatibility with ledger reports
                            BatchNo = barcodeDto.BatchNo,
                            TrackingNo = barcodeDto.TrackingNo,
                            InwardQty = 0,
                            OutwardQty = barcodeDto.Quantity,
                            BalanceQty = currentOverallBalance - barcodeDto.Quantity,
                            UnitPrice = detailDto.Rate,
                            CreatedAt = DateTimeOffset.UtcNow
                        };
                        _context.StockLedgers.Add(stockLedger);
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return Ok(proforma);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"An error occurred while saving proforma invoice: {ex.Message}");
            }
        }

        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteProforma(Guid id)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var proforma = await _context.ProformaInvoices
                    .Include(p => p.Details)
                        .ThenInclude(d => d.Barcodes)
                    .FirstOrDefaultAsync(p => p.Id == id);

                if (proforma == null)
                {
                    return NotFound("Proforma invoice not found.");
                }

                // 1. Delete associated StockOutward record (which resets BarcodeMaster and deletes StockLedger)
                if (proforma.ConvertedStockOutwardId.HasValue)
                {
                    var outward = await _context.StockOutwards
                        .Include(so => so.Details)
                        .FirstOrDefaultAsync(so => so.Id == proforma.ConvertedStockOutwardId.Value);

                    if (outward != null)
                    {
                        var outwardNo = outward.OutwardNo;

                        // Reset IsUsed flag to false in BarcodeMaster
                        var barcodes = outward.Details.Select(d => d.Barcode).Where(b => !string.IsNullOrEmpty(b)).ToList();
                        if (barcodes.Any())
                        {
                            var barcodeMasters = await _context.BarcodeMasters
                                .Where(bm => barcodes.Contains(bm.Barcode))
                                .ToListAsync();
                            foreach (var bm in barcodeMasters)
                            {
                                bm.IsUsed = false;
                            }
                            _context.BarcodeMasters.UpdateRange(barcodeMasters);
                        }

                        // Delete associated StockLedger records
                        var ledgers = await _context.StockLedgers
                            .Where(l => l.ReferenceNo == outwardNo)
                            .ToListAsync();
                        _context.StockLedgers.RemoveRange(ledgers);

                        // Delete StockOutward
                        _context.StockOutwards.Remove(outward);
                    }
                }

                // 2. Delete ProformaInvoice (cascades to Details and Barcodes)
                _context.ProformaInvoices.Remove(proforma);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                return Ok(new { message = "Proforma invoice and corresponding stock entries deleted successfully." });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, $"An error occurred while deleting proforma invoice: {ex.Message}");
            }
        }

        private async Task<string> GenerateProformaNumberAsync()
        {
            var year = DateTime.UtcNow.Year;
            var prefix = $"PROF-{year}-";
            var lastRecord = await _context.ProformaInvoices
                .Where(p => p.ProformaNo.StartsWith(prefix))
                .OrderByDescending(p => p.ProformaNo)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastRecord != null)
            {
                var parts = lastRecord.ProformaNo.Split('-');
                if (parts.Length == 3 && int.TryParse(parts[2], out var lastNum))
                {
                    nextNum = lastNum + 1;
                }
            }
            return $"{prefix}{nextNum:D6}";
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
