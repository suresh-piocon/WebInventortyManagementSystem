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
        public async Task<IActionResult> GetSupplierStockReport(
            [FromQuery] string valuationMethod = "WeightedAverage",
            [FromQuery] DateTimeOffset? upToDate = null)
        {
            // Compute end-of-day cutoff for the selected date
            DateTimeOffset? cutoff = null;
            if (upToDate.HasValue)
            {
                var endOfDay = upToDate.Value.ToUniversalTime().Date.AddDays(1).AddTicks(-1);
                cutoff = new DateTimeOffset(endOfDay, TimeSpan.Zero);
            }

            // Join StockInwardDetails to retrieve tracking image and original purchase cost
            var inwardQuery = _context.StockInwardDetails
                .Include(d => d.StockInward)
                    .ThenInclude(si => si!.Supplier)
                .Include(d => d.Item)
                .AsQueryable();

            // Filter inward records up to date
            if (cutoff.HasValue)
                inwardQuery = inwardQuery.Where(d => d.StockInward!.InwardDate <= cutoff.Value);

            var inwardDetails = await inwardQuery.ToListAsync();

            var report = new List<SupplierStockReportDto>();

            // Group by Supplier, Item, Batch, TrackingNo to evaluate actual remaining quantities
            foreach (var detail in inwardDetails)
            {
                var itemId = detail.ItemId;
                var trackingNo = detail.TrackingNo;
                var batchNo = detail.BatchNo;

                // Total inward qty for this batch/tracking
                var totalIn = detail.Quantity;

                // Sum outward qty scoped up to the cutoff date
                var outwardQuery = _context.StockLedgers
                    .Where(l => l.ItemId == itemId && l.TrackingNo == trackingNo && l.BatchNo == batchNo);

                if (cutoff.HasValue)
                    outwardQuery = outwardQuery.Where(l => l.TransactionDate <= cutoff.Value);

                var totalOut = await outwardQuery.SumAsync(l => l.OutwardQty);

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

            // Recompute running balance per-item (quantity and weight)
            var runningByItem = new Dictionary<Guid, decimal>();
            var runningWtByItem = new Dictionary<Guid, decimal>();
            foreach (var entry in data)
            {
                if (!runningByItem.ContainsKey(entry.ItemId))
                {
                    runningByItem[entry.ItemId] = 0;
                    runningWtByItem[entry.ItemId] = 0;
                }
                runningByItem[entry.ItemId] += entry.InwardQty - entry.OutwardQty;
                runningWtByItem[entry.ItemId] += entry.InwardWeight - entry.OutwardWeight;
                entry.BalanceQty = runningByItem[entry.ItemId];
                entry.BalanceWeight = runningWtByItem[entry.ItemId];
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
                InwardWeight = l.InwardWeight,
                OutwardWeight = l.OutwardWeight,
                BalanceWeight = l.BalanceWeight,
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

            // Resolve supplier name via StockInwardDetails → StockInward → Supplier
            // (use the first matching inward detail for this batch/tracking)
            var supplierName = "N/A";
            if (!string.IsNullOrEmpty(trackingNo) || !string.IsNullOrEmpty(batchNo))
            {
                var inwardDetail = await _context.StockInwardDetails
                    .Include(d => d.StockInward)
                        .ThenInclude(si => si!.Supplier)
                    .Where(d =>
                        (string.IsNullOrEmpty(trackingNo) || d.TrackingNo == trackingNo) &&
                        (string.IsNullOrEmpty(batchNo) || d.BatchNo == batchNo))
                    .FirstOrDefaultAsync();
                supplierName = inwardDetail?.StockInward?.Supplier?.Name ?? "N/A";
            }

            // ──────────────────────────────────────────────────────────────────────
            // Determine Issued / In Stock status correctly for BOTH barcode types:
            //
            // UNIQUE barcodes: each barcode has a 1:1 relationship with a
            //   StockOutwardDetail.Barcode entry — exact match determines status.
            //
            // BATCH barcodes: during outward, only ONE barcode value is stored in
            //   StockOutwardDetail.Barcode (the batch barcode that was scanned),
            //   but Quantity can be > 1.  We must NOT mark all barcodes with that
            //   same barcode value as "Issued" — instead we:
            //     1. Sum total OutwardQty from StockLedger for this batch/tracking.
            //     2. Mark the first [outwardQty] barcodes (sorted ascending) as Issued
            //        and the remaining as In Stock.
            //
            // This guarantees that Issued count == Stock Ledger outward qty.
            // ──────────────────────────────────────────────────────────────────────

            // Build a lookup of outward details by exact barcode value (for Unique type)
            var barcodeValues = barcodes.Select(b => b.Barcode).ToList();
            var outwardDetailsByBarcode = await _context.StockOutwardDetails
                .Include(od => od.StockOutward)
                .Where(od => barcodeValues.Contains(od.Barcode))
                .GroupBy(od => od.Barcode)
                .ToDictionaryAsync(g => g.Key, g => g.OrderBy(od => od.StockOutward!.OutwardDate).First());

            // For batch-type groups: compute total outward qty from the ledger per (trackingNo, batchNo)
            // We group barcodes by (TrackingNo, BatchNo) then apply qty-based assignment.
            var groupedBarcodes = barcodes
                .GroupBy(b => (b.TrackingNo, b.BatchNo))
                .ToList();

            var result = new List<BarcodeDetailReportDto>();

            foreach (var group in groupedBarcodes)
            {
                var groupTrackingNo = group.Key.TrackingNo;
                var groupBatchNo = group.Key.BatchNo;
                var groupBarcodes = group.OrderBy(b => b.Barcode).ToList();

                // Is this group using Unique barcodes?
                bool isUniqueGroup = groupBarcodes.Any(b => b.Type == "Unique");

                if (isUniqueGroup)
                {
                    // Unique barcodes: exact match per barcode value
                    foreach (var b in groupBarcodes)
                    {
                        outwardDetailsByBarcode.TryGetValue(b.Barcode, out var outward);
                        bool isIssued = outward != null;
                        result.Add(new BarcodeDetailReportDto
                        {
                            SupplierName = supplierName,
                            ItemName = b.Item?.Name ?? "Unknown Item",
                            BarcodeNo = b.Barcode,
                            BatchNo = b.BatchNo,
                            TrackingNo = b.TrackingNo,
                            Type = b.Type,
                            InwardDate = b.CreatedAt,
                            OutwardDate = outward?.StockOutward?.OutwardDate,
                            Status = isIssued ? "Issued" : "In Stock",
                            ImageUrl = b.ImageUrl
                        });
                    }
                }
                else
                {
                    // Batch barcodes: use total OutwardQty from StockLedger
                    // to determine how many barcodes are "Issued"
                    var totalOutwardQty = await _context.StockLedgers
                        .Where(l => l.TrackingNo == groupTrackingNo && l.BatchNo == groupBatchNo)
                        .SumAsync(l => l.OutwardQty);

                    // Fetch the first outward event for the date column
                    var firstOutwardDetail = await _context.StockOutwardDetails
                        .Include(od => od.StockOutward)
                        .Where(od => od.TrackingNo == groupTrackingNo && od.BatchNo == groupBatchNo)
                        .OrderBy(od => od.StockOutward!.OutwardDate)
                        .FirstOrDefaultAsync();

                    int issuedCount = (int)Math.Round(totalOutwardQty, MidpointRounding.AwayFromZero);
                    int barcodeIndex = 0;

                    foreach (var b in groupBarcodes)
                    {
                        bool isIssued = barcodeIndex < issuedCount;
                        result.Add(new BarcodeDetailReportDto
                        {
                            SupplierName = supplierName,
                            ItemName = b.Item?.Name ?? "Unknown Item",
                            BarcodeNo = b.Barcode,
                            BatchNo = b.BatchNo,
                            TrackingNo = b.TrackingNo,
                            Type = b.Type,
                            InwardDate = b.CreatedAt,
                            OutwardDate = isIssued ? firstOutwardDetail?.StockOutward?.OutwardDate : null,
                            Status = isIssued ? "Issued" : "In Stock",
                            ImageUrl = b.ImageUrl
                        });
                        barcodeIndex++;
                    }
                }
            }

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
        public async Task<IActionResult> ExportSupplierStock(
            [FromQuery] string format,
            [FromQuery] string valuationMethod = "WeightedAverage",
            [FromQuery] DateTimeOffset? upToDate = null)
        {
            var res = await GetSupplierStockReport(valuationMethod, upToDate);
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

        [HttpGet("barcode-stock-images")]
        public async Task<IActionResult> GetBarcodeStockImages([FromQuery] DateTimeOffset? upToDate)
        {
            var upToDateVal = upToDate?.ToUniversalTime() ?? DateTimeOffset.UtcNow;

            // 1. Fetch all BarcodeMasters created on or before upToDateVal
            var barcodes = await _context.BarcodeMasters
                .Include(b => b.Item)
                .Where(b => b.CreatedAt <= upToDateVal)
                .OrderBy(b => b.Barcode)
                .ToListAsync();

            if (!barcodes.Any())
            {
                return Ok(new List<BarcodeStockImageReportDto>());
            }

            // 2. Fetch all StockOutwardDetails issued on or before upToDateVal
            var outwardDetails = await _context.StockOutwardDetails
                .Include(od => od.StockOutward)
                .Where(od => od.StockOutward!.OutwardDate <= upToDateVal)
                .ToListAsync();

            // 3. Fetch all StockLedger records on or before upToDateVal
            var ledgers = await _context.StockLedgers
                .Where(l => l.TransactionDate <= upToDateVal)
                .ToListAsync();

            // 4. Fetch all StockInwardDetails to resolve Supplier Name and Inward Date
            var inwardDetails = await _context.StockInwardDetails
                .Include(d => d.StockInward)
                    .ThenInclude(si => si!.Supplier)
                .ToListAsync();

            // Create lookups
            var inwardDetailsLookup = inwardDetails
                .GroupBy(d => d.TrackingNo)
                .ToDictionary(g => g.Key, g => g.First());

            var outwardDetailsLookup = outwardDetails
                .GroupBy(od => od.Barcode)
                .ToDictionary(g => g.Key, g => g.First());

            var ledgerOutwardQtyLookup = ledgers
                .Where(l => l.OutwardQty > 0)
                .GroupBy(l => $"{l.TrackingNo}|{l.BatchNo}")
                .ToDictionary(g => g.Key, g => g.Sum(l => l.OutwardQty));

            var result = new List<BarcodeStockImageReportDto>();

            // Group barcodes by (TrackingNo, BatchNo) to accurately apply the "issued" count for batch barcodes
            var grouped = barcodes.GroupBy(b => new { b.TrackingNo, b.BatchNo });

            foreach (var grp in grouped)
            {
                var trackingNo = grp.Key.TrackingNo;
                var batchNo = grp.Key.BatchNo;
                
                // Get supplier & inward date from inward details lookup
                string supplierName = "N/A";
                DateTimeOffset inwardDate = DateTimeOffset.MinValue;
                if (inwardDetailsLookup.TryGetValue(trackingNo, out var inwardDetail))
                {
                    supplierName = inwardDetail.StockInward?.Supplier?.Name ?? "N/A";
                    inwardDate = inwardDetail.StockInward?.InwardDate ?? inwardDetail.StockInward?.CreatedAt ?? DateTimeOffset.MinValue;
                }

                // If inward date is after the upToDate filter, skip this group entirely
                if (inwardDate > upToDateVal) continue;

                var isUnique = grp.First().Type == "Unique";

                if (isUnique)
                {
                    // Unique barcodes: check if each barcode is issued on or before upToDateVal
                    foreach (var b in grp)
                    {
                        var isIssued = outwardDetailsLookup.ContainsKey(b.Barcode);
                        if (!isIssued)
                        {
                            var ageDays = (upToDateVal.Date - inwardDate.Date).Days;
                            result.Add(new BarcodeStockImageReportDto
                            {
                                BarcodeNo = b.Barcode,
                                ItemName = b.Item?.Name ?? "Unknown Item",
                                ImageUrl = b.ImageUrl,
                                InwardDate = inwardDate,
                                SupplierName = supplierName,
                                StockAgeDays = ageDays >= 0 ? ageDays : 0
                            });
                        }
                    }
                }
                else
                {
                    // Batch barcodes: use total OutwardQty from StockLedger to determine how many barcodes are issued
                    var ledgerKey = $"{trackingNo}|{batchNo}";
                    var outwardQty = ledgerOutwardQtyLookup.TryGetValue(ledgerKey, out var qty) ? qty : 0m;
                    int issuedCount = (int)Math.Ceiling(outwardQty);

                    int index = 0;
                    foreach (var b in grp)
                    {
                        var isIssued = index < issuedCount;
                        if (!isIssued)
                        {
                            var ageDays = (upToDateVal.Date - inwardDate.Date).Days;
                            result.Add(new BarcodeStockImageReportDto
                            {
                                BarcodeNo = b.Barcode,
                                ItemName = b.Item?.Name ?? "Unknown Item",
                                ImageUrl = b.ImageUrl,
                                InwardDate = inwardDate,
                                SupplierName = supplierName,
                                StockAgeDays = ageDays >= 0 ? ageDays : 0
                            });
                        }
                        index++;
                    }
                }
            }

            return Ok(result.OrderBy(r => r.BarcodeNo).ToList());
        }
    }
}
