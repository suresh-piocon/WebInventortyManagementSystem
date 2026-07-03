using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Api.Data;
using InventoryManagement.Api.Services;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ReportsController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly ValuationService _valuationService;
        private readonly ReportingService _reportingService;

        public ReportsController(
            InventoryDbContext context,
            ValuationService valuationService,
            ReportingService reportingService)
        {
            _context = context;
            _valuationService = valuationService;
            _reportingService = reportingService;
        }

        // ==========================================
        // DASHBOARD ENDPOINT
        // ==========================================
        [HttpGet("dashboard")]
        public async Task<IActionResult> GetDashboardData([FromQuery] string valuationMethod = "WeightedAverage")
        {
            var today = DateTimeOffset.UtcNow.Date;

            var totalSuppliers = await _context.Suppliers.CountAsync();
            var totalItems = await _context.Items.CountAsync();

            var todayInward = await _context.StockLedgers
                .Where(l => l.TransactionDate >= today && l.InwardQty > 0)
                .SumAsync(l => l.InwardQty);

            var todayOutward = await _context.StockLedgers
                .Where(l => l.TransactionDate >= today && l.OutwardQty > 0)
                .SumAsync(l => l.OutwardQty);

            // Calculate stock valuation for all items
            decimal currentStockValue = 0;
            var items = await _context.Items.Select(i => i.Id).ToListAsync();
            foreach (var itemId in items)
            {
                var val = await _valuationService.CalculateValuationAsync(itemId, valuationMethod);
                currentStockValue += val.TotalValue;
            }

            // Low Stock Items (Balance < ReorderLevel)
            var lowStockItems = new List<LowStockDto>();
            var allItems = await _context.Items.Include(i => i.Unit).ToListAsync();
            foreach (var item in allItems)
            {
                var ledgerBalance = await _context.StockLedgers
                    .Where(l => l.ItemId == item.Id)
                    .OrderByDescending(l => l.TransactionDate)
                    .ThenByDescending(l => l.CreatedAt)
                    .Select(l => l.BalanceQty)
                    .FirstOrDefaultAsync();

                if (ledgerBalance < item.ReorderLevel || ledgerBalance < item.MinimumStock)
                {
                    lowStockItems.Add(new LowStockDto
                    {
                        ItemId = item.Id,
                        ItemCode = item.Code,
                        ItemName = item.Name,
                        UnitCode = item.Unit?.Code ?? "PCS",
                        CurrentStock = ledgerBalance,
                        MinStock = item.MinimumStock,
                        ReorderLevel = item.ReorderLevel
                    });
                }
            }

            // Monthly Inward / Outward Chart Data (Last 6 Months)
            var monthlyData = new List<MonthlyChartDto>();
            for (int i = 5; i >= 0; i--)
            {
                var monthDate = DateTime.UtcNow.AddMonths(-i);
                var year = monthDate.Year;
                var month = monthDate.Month;
                var monthStart = new DateTimeOffset(year, month, 1, 0, 0, 0, TimeSpan.Zero);
                var monthEnd = monthStart.AddMonths(1);

                var inward = await _context.StockLedgers
                    .Where(l => l.TransactionDate >= monthStart && l.TransactionDate < monthEnd && l.InwardQty > 0)
                    .SumAsync(l => l.InwardQty);

                var outward = await _context.StockLedgers
                    .Where(l => l.TransactionDate >= monthStart && l.TransactionDate < monthEnd && l.OutwardQty > 0)
                    .SumAsync(l => l.OutwardQty);

                monthlyData.Add(new MonthlyChartDto
                {
                    MonthName = monthStart.ToString("MMM yyyy"),
                    InwardQty = inward,
                    OutwardQty = outward
                });
            }

            // Top Suppliers (by volume received)
            var topSuppliers = await _context.StockInwards
                .Include(si => si.Supplier)
                .SelectMany(si => si.Details)
                .GroupBy(d => d.StockInward!.Supplier!.Name)
                .Select(g => new TopSupplierDto
                {
                    SupplierName = g.Key,
                    TotalQty = g.Sum(d => d.Quantity)
                })
                .OrderByDescending(g => g.TotalQty)
                .Take(5)
                .ToListAsync();

            var result = new DashboardDto
            {
                TotalSuppliers = totalSuppliers,
                TotalItems = totalItems,
                TodayInward = todayInward,
                TodayOutward = todayOutward,
                CurrentStockValue = currentStockValue,
                LowStockItems = lowStockItems,
                MonthlyChartData = monthlyData,
                TopSuppliers = topSuppliers
            };

            return Ok(result);
        }

        // ==========================================
        // SUPPLIER WISE STOCK REPORT
        // ==========================================
        [HttpGet("supplier-stock")]
        public async Task<IActionResult> GetSupplierStockReport([FromQuery] string valuationMethod = "WeightedAverage")
        {
            // Join StockInwardDetails to retrieve tracking image and original purchase cost
            var inwardDetails = await _context.StockInwardDetails
                .Include(d => d.StockInward)
                    .ThenInclude(si => si!.Supplier)
                .Include(d => d.Item)
                .ToListAsync();

            var report = new List<SupplierStockReportDto>();

            // Group by Supplier, Item, Batch, TrackingNo to evaluate actual remaining quantities
            foreach (var detail in inwardDetails)
            {
                var itemId = detail.ItemId;
                var trackingNo = detail.TrackingNo;
                var batchNo = detail.BatchNo;

                // Total inward qty for this batch/tracking
                var totalIn = detail.Quantity;

                // Sum outward qty in stock ledger matching this tracking number
                var totalOut = await _context.StockLedgers
                    .Where(l => l.ItemId == itemId && l.TrackingNo == trackingNo && l.BatchNo == batchNo)
                    .SumAsync(l => l.OutwardQty);

                var balance = totalIn - totalOut;

                if (balance <= 0) continue; // Skip items with no stock left

                // Load ImageUrl from BarcodeMaster (prioritize non-null ImageUrl)
                var barcodeMaster = await _context.BarcodeMasters
                    .Where(b => b.TrackingNo == trackingNo && b.BatchNo == batchNo && b.ImageUrl != null && b.ImageUrl != "")
                    .FirstOrDefaultAsync()
                    ?? await _context.BarcodeMasters
                    .Where(b => b.TrackingNo == trackingNo && b.BatchNo == batchNo)
                    .FirstOrDefaultAsync();

                var valResult = await _valuationService.CalculateValuationAsync(itemId, valuationMethod);
                var cost = valResult.UnitCost > 0 ? valResult.UnitCost : detail.Rate;

                report.Add(new SupplierStockReportDto
                {
                    SupplierName = detail.StockInward?.Supplier?.Name ?? "N/A",
                    ItemName = detail.Item?.Name ?? "N/A",
                    ItemCode = detail.Item?.Code ?? "N/A",
                    BatchNo = batchNo,
                    TrackingNo = trackingNo,
                    InwardQty = totalIn,
                    OutwardQty = totalOut,
                    BalanceQty = balance,
                    UnitCost = cost,
                    Value = balance * cost,
                    ImageUrl = barcodeMaster?.ImageUrl // Webcam/mobile photo URL
                });
            }

            return Ok(report);
        }

        // ==========================================
        // SUPPLIER PURCHASE REPORT (DATE WISE)
        // ==========================================
        [HttpGet("supplier-purchase")]
        public async Task<IActionResult> GetSupplierPurchaseReport(
            [FromQuery] DateTimeOffset? startDate, 
            [FromQuery] DateTimeOffset? endDate)
        {
            var query = _context.StockInwardDetails
                .Include(d => d.StockInward)
                    .ThenInclude(si => si!.Supplier)
                .Include(d => d.Item)
                .AsQueryable();

            if (startDate.HasValue)
                query = query.Where(d => d.StockInward!.InwardDate >= startDate.Value);

            if (endDate.HasValue)
                query = query.Where(d => d.StockInward!.InwardDate <= endDate.Value);

            var list = await query
                .OrderByDescending(d => d.StockInward!.InwardDate)
                .Select(d => new SupplierPurchaseReportDto
                {
                    Id = d.Id,
                    StockInwardId = d.StockInwardId,
                    InwardDate = d.StockInward!.InwardDate,
                    SupplierName = d.StockInward.Supplier!.Name,
                    InvoiceNo = d.StockInward.InvoiceNo ?? "N/A",
                    ItemCode = d.Item!.Code,
                    ItemName = d.Item.Name,
                    Quantity = d.Quantity,
                    Rate = d.Rate,
                    Amount = d.Amount
                })
                .ToListAsync();

            return Ok(list);
        }

        // ==========================================
        // STOCK LEDGER REPORT
        // ==========================================
        [HttpGet("ledger/{itemId}")]
        public async Task<IActionResult> GetStockLedger(Guid itemId, [FromQuery] DateTimeOffset? upToDate)
        {
            var query = _context.StockLedgers
                .Where(l => l.ItemId == itemId)
                .AsQueryable();

            if (upToDate.HasValue)
            {
                // Include all records up to end of the selected date (23:59:59 UTC)
                var endOfDay = upToDate.Value.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
                query = query.Where(l => l.TransactionDate <= new DateTimeOffset(endOfDay, TimeSpan.Zero));
            }

            var data = await query
                .OrderBy(l => l.TransactionDate)
                .ThenBy(l => l.CreatedAt)
                .ToListAsync();

            // Recompute running balance from InwardQty/OutwardQty in order
            // (stored BalanceQty may be stale/wrong for multi-row same-transaction outwards)
            decimal running = 0;
            foreach (var entry in data)
            {
                running += entry.InwardQty - entry.OutwardQty;
                entry.BalanceQty = running;
            }

            return Ok(data);
        }

        // ==========================================
        // ALL-ITEMS STOCK LEDGER (with optional upToDate filter)
        // ==========================================
        [HttpGet("ledger/all")]
        public async Task<IActionResult> GetAllItemsLedger([FromQuery] DateTimeOffset? upToDate)
        {
            var query = _context.StockLedgers
                .Include(l => l.Item)
                .AsQueryable();

            if (upToDate.HasValue)
            {
                var endOfDay = upToDate.Value.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
                query = query.Where(l => l.TransactionDate <= new DateTimeOffset(endOfDay, TimeSpan.Zero));
            }

            var data = await query
                .OrderBy(l => l.ItemId)
                .ThenBy(l => l.TransactionDate)
                .ThenBy(l => l.CreatedAt)
                .ToListAsync();

            // Recompute running balance per-item
            var runningByItem = new Dictionary<Guid, decimal>();
            foreach (var entry in data)
            {
                if (!runningByItem.ContainsKey(entry.ItemId))
                    runningByItem[entry.ItemId] = 0;
                runningByItem[entry.ItemId] += entry.InwardQty - entry.OutwardQty;
                entry.BalanceQty = runningByItem[entry.ItemId];
            }

            // Project to include item name/code for the all-items view
            var result = data.Select(l => new AllItemsLedgerDto
            {
                Id = l.Id,
                ItemId = l.ItemId,
                ItemCode = l.Item?.Code ?? string.Empty,
                ItemName = l.Item?.Name ?? string.Empty,
                TransactionDate = l.TransactionDate,
                TransactionType = l.TransactionType,
                ReferenceNo = l.ReferenceNo,
                BatchNo = l.BatchNo,
                TrackingNo = l.TrackingNo,
                InwardQty = l.InwardQty,
                OutwardQty = l.OutwardQty,
                BalanceQty = l.BalanceQty,
                UnitPrice = l.UnitPrice
            }).ToList();

            return Ok(result);
        }

        // ==========================================
        // BARCODE TRACKING / MOVEMENT HISTORY
        // ==========================================
        [HttpGet("barcode-tracking/{code}")]
        public async Task<IActionResult> TrackBarcode(string code)
        {
            string? trackingNo = null;

            // Search BarcodeMaster
            var barcode = await _context.BarcodeMasters
                .Include(b => b.Item)
                .FirstOrDefaultAsync(b => b.Barcode == code || b.TrackingNo == code);

            if (barcode != null)
            {
                trackingNo = barcode.TrackingNo;
            }
            else
            {
                // Try finding directly in inward detail
                var detail = await _context.StockInwardDetails
                    .FirstOrDefaultAsync(d => d.TrackingNo == code || d.BatchNo == code);
                if (detail != null)
                {
                    trackingNo = detail.TrackingNo;
                }
            }

            if (string.IsNullOrEmpty(trackingNo))
            {
                return NotFound("No history found for this barcode, QR, or tracking number.");
            }

            // Get Inward Info
            var inwardDetail = await _context.StockInwardDetails
                .Include(d => d.StockInward)
                    .ThenInclude(si => si!.Supplier)
                .Include(d => d.Item)
                .FirstOrDefaultAsync(d => d.TrackingNo == trackingNo);

            if (inwardDetail == null) return NotFound("Inward details missing.");

            // Fetch all supporting data
            var barcodeInfo = await _context.BarcodeMasters
                .Where(b => b.TrackingNo == trackingNo)
                .ToListAsync();

            var allOutwardDetails = await _context.StockOutwardDetails
                .Include(od => od.StockOutward)
                .Where(od => od.TrackingNo == trackingNo)
                .ToListAsync();

            // Get Ledger Movement Logs
            var ledgerLogs = await _context.StockLedgers
                .Where(l => l.TrackingNo == trackingNo)
                .OrderBy(l => l.TransactionDate)
                .ToListAsync();

            // Determine if this is a specific unique barcode search
            var specificBarcode = barcodeInfo.FirstOrDefault(b => b.Barcode == code);
            bool isUniqueBarcodeLookup = specificBarcode?.Type == "Unique";

            // For unique barcode: only show outward records specific to THIS barcode
            // For tracking/batch search: show all outward records under the tracking number
            List<StockOutwardDetail> filteredOutwardDetails;
            if (isUniqueBarcodeLookup)
            {
                filteredOutwardDetails = allOutwardDetails
                    .Where(od => od.Barcode == code)
                    .ToList();
            }
            else
            {
                filteredOutwardDetails = allOutwardDetails;
            }

            var trackingReport = new BarcodeTrackingReportDto
            {
                TrackingNo = trackingNo,
                BatchNo = inwardDetail.BatchNo,
                ItemCode = inwardDetail.Item!.Code,
                ItemName = inwardDetail.Item.Name,
                SupplierName = inwardDetail.StockInward!.Supplier!.Name,
                InwardDate = inwardDetail.StockInward.InwardDate,
                InvoiceNo = inwardDetail.StockInward.InvoiceNo,
                // For unique barcode searches, inward qty is always 1 per barcode
                QuantityInward = isUniqueBarcodeLookup ? 1 : inwardDetail.Quantity,
                Rate = inwardDetail.Rate,
                // Prefer the specific searched barcode's image, then fall back to any non-null image in the batch
                PhotoUrl = barcodeInfo.FirstOrDefault(b => b.Barcode == code && !string.IsNullOrEmpty(b.ImageUrl))?.ImageUrl
                           ?? barcodeInfo.FirstOrDefault(b => !string.IsNullOrEmpty(b.ImageUrl))?.ImageUrl,
                RegisteredBarcodes = barcodeInfo.Select(b => b.Barcode).ToList(),
                Issues = filteredOutwardDetails.Select(o => new BarcodeIssueDto
                {
                    OutwardNo = o.StockOutward!.OutwardNo,
                    OutwardDate = o.StockOutward.OutwardDate,
                    CustomerName = o.StockOutward.CustomerName ?? "N/A",
                    QuantityIssued = o.Quantity,
                    Rate = o.Rate
                }).ToList(),
                LedgerEntries = ledgerLogs
            };

            return Ok(trackingReport);
        }

        [HttpGet("barcodes/item/{itemId}")]
        public async Task<IActionResult> GetBarcodesByItem(Guid itemId)
        {
            var data = await _context.BarcodeMasters
                .Where(b => b.ItemId == itemId)
                .OrderByDescending(b => b.CreatedAt)
                .ToListAsync();
            return Ok(data);
        }

        [HttpGet("barcodes/inward/{inwardId}")]
        public async Task<IActionResult> GetBarcodesByInward(Guid inwardId)
        {
            var trackingNumbers = await _context.StockInwardDetails
                .Where(d => d.StockInwardId == inwardId)
                .Select(d => d.TrackingNo)
                .ToListAsync();

            if (trackingNumbers == null || !trackingNumbers.Any())
            {
                return Ok(new List<BarcodeMaster>());
            }

            var data = await _context.BarcodeMasters
                .Include(b => b.Item)
                .Where(b => trackingNumbers.Contains(b.TrackingNo))
                .OrderBy(b => b.Barcode)
                .ToListAsync();

            return Ok(data);
        }

        [HttpGet("barcodes/detail")]
        public async Task<IActionResult> GetBarcodeDetails(
            [FromQuery] string? batchNo,
            [FromQuery] string? trackingNo,
            [FromQuery] DateTimeOffset? fromDate,
            [FromQuery] DateTimeOffset? toDate)
        {
            if (string.IsNullOrEmpty(batchNo) && string.IsNullOrEmpty(trackingNo))
            {
                return BadRequest("Batch number or tracking number is required.");
            }

            var query = _context.BarcodeMasters
                .Include(b => b.Item)
                .AsQueryable();

            if (!string.IsNullOrEmpty(batchNo))
                query = query.Where(b => b.BatchNo == batchNo);

            if (!string.IsNullOrEmpty(trackingNo))
                query = query.Where(b => b.TrackingNo == trackingNo);

            // Filter by inward date range (CreatedAt = inward date of the barcode)
            if (fromDate.HasValue)
            {
                var start = fromDate.Value.ToUniversalTime().Date;
                query = query.Where(b => b.CreatedAt >= new DateTimeOffset(start, TimeSpan.Zero));
            }
            if (toDate.HasValue)
            {
                var end = toDate.Value.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
                query = query.Where(b => b.CreatedAt <= new DateTimeOffset(end, TimeSpan.Zero));
            }

            var barcodes = await query.OrderBy(b => b.Barcode).ToListAsync();
            var barcodeList = barcodes.Select(b => b.Barcode).ToList();

            // Fetch outward details for these barcodes
            var outwardDetails = await _context.StockOutwardDetails
                .Include(od => od.StockOutward)
                .Where(od => barcodeList.Contains(od.Barcode))
                .ToListAsync();

            var result = barcodes.Select(b => {
                var outward = outwardDetails.FirstOrDefault(od => od.Barcode == b.Barcode);
                // Use IsUsed flag as source of truth for Unique barcodes;
                // For batch items also cross-check outward records
                bool isIssued = b.IsUsed == true || outward != null;
                return new BarcodeDetailReportDto
                {
                    ItemName = b.Item?.Name ?? "Unknown Item",
                    BarcodeNo = b.Barcode,
                    BatchNo = b.BatchNo,
                    TrackingNo = b.TrackingNo,
                    Type = b.Type,
                    InwardDate = b.CreatedAt,
                    OutwardDate = outward?.StockOutward?.OutwardDate,
                    Status = isIssued ? "Issued" : "In Stock",
                    ImageUrl = b.ImageUrl
                };
            }).ToList();


            return Ok(result);
        }

        // ==========================================
        // AUDIT LOG REPORT
        // ==========================================
        [HttpGet("audit-logs")]
        public async Task<IActionResult> GetAuditLogs()
        {
            var logs = await _context.AuditLogs
                .OrderByDescending(l => l.Timestamp)
                .ToListAsync();

            var users = await _context.UserProfiles.ToDictionaryAsync(u => u.Id, u => u.Email);

            var report = logs.Select(l => new AuditLogReportDto
            {
                Id = l.Id,
                Action = l.Action,
                TableName = l.TableName,
                RecordId = l.RecordId,
                OldValue = l.OldValue,
                NewValue = l.NewValue,
                Timestamp = l.Timestamp,
                UserEmail = users.TryGetValue(l.UserId, out var email) ? email : "System / Unknown"
            }).ToList();

            return Ok(report);
        }

        // ==========================================
        // EXPORT TO EXCEL / CSV
        // ==========================================
        [HttpGet("export/supplier-stock")]
        public async Task<IActionResult> ExportSupplierStock([FromQuery] string format, [FromQuery] string valuationMethod = "WeightedAverage")
        {
            var res = await GetSupplierStockReport(valuationMethod);
            if (res is OkObjectResult okResult && okResult.Value is List<SupplierStockReportDto> list)
            {
                var headers = new[] { "Supplier", "Item Code", "Item Name", "Batch No", "Tracking No", "Inward Qty", "Outward Qty", "Balance Qty", "Unit Cost", "Total Value" };
                Func<SupplierStockReportDto, object?[]> mapper = item => new object?[]
                {
                    item.SupplierName, item.ItemCode, item.ItemName, item.BatchNo, item.TrackingNo,
                    item.InwardQty, item.OutwardQty, item.BalanceQty, item.UnitCost, item.Value
                };

                if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
                {
                    var fileBytes = _reportingService.ExportToCsv(headers, list, mapper);
                    return File(fileBytes, "text/csv", "SupplierStockReport.csv");
                }
                else
                {
                    var fileBytes = _reportingService.ExportToExcel("StockReport", headers, list, mapper);
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "SupplierStockReport.xlsx");
                }
            }
            return BadRequest("Error retrieving report data.");
        }

        [HttpGet("export/supplier-purchase")]
        public async Task<IActionResult> ExportSupplierPurchase([FromQuery] string format, [FromQuery] DateTimeOffset? startDate, [FromQuery] DateTimeOffset? endDate)
        {
            var res = await GetSupplierPurchaseReport(startDate, endDate);
            if (res is OkObjectResult okResult && okResult.Value is List<SupplierPurchaseReportDto> list)
            {
                var headers = new[] { "Date", "Supplier", "Invoice No", "Item Code", "Item Name", "Quantity", "Rate", "Amount" };
                Func<SupplierPurchaseReportDto, object?[]> mapper = item => new object?[]
                {
                    item.InwardDate, item.SupplierName, item.InvoiceNo, item.ItemCode, item.ItemName,
                    item.Quantity, item.Rate, item.Amount
                };

                if (format.Equals("csv", StringComparison.OrdinalIgnoreCase))
                {
                    var fileBytes = _reportingService.ExportToCsv(headers, list, mapper);
                    return File(fileBytes, "text/csv", "SupplierPurchaseReport.csv");
                }
                else
                {
                    var fileBytes = _reportingService.ExportToExcel("PurchaseReport", headers, list, mapper);
                    return File(fileBytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "SupplierPurchaseReport.xlsx");
                }
            }
            return BadRequest("Error retrieving report data.");
        }
    }
}
