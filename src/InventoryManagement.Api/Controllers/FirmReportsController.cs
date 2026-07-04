using System;
using System.Collections.Generic;
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
    public class FirmReportsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public FirmReportsController(InventoryDbContext context)
        {
            _context = context;
        }

        [HttpGet("summary")]
        public async Task<IActionResult> GetSummary()
        {
            var summary = await _context.Firms
                .Select(f => new FirmCustomerCountDto
                {
                    FirmCode = f.FirmCode,
                    FirmName = f.FirmName,
                    TotalCustomers = _context.Customers.Count(c => c.FirmId == f.FirmId)
                })
                .OrderBy(s => s.FirmName)
                .ToListAsync();

            return Ok(summary);
        }

        [HttpGet("customerlist")]
        public async Task<IActionResult> GetCustomerList([FromQuery] Guid? firmId)
        {
            var query = _context.Customers.AsQueryable();

            if (firmId.HasValue)
            {
                query = query.Where(c => c.FirmId == firmId.Value);
            }

            var list = await query
                .Select(c => new FirmCustomerListDto
                {
                    FirmCode = c.FirmCode,
                    FirmName = c.FirmName,
                    CustomerCode = c.CustomerCode,
                    CustomerName = c.CustomerName,
                    MobileNo = c.MobileNo,
                    GSTIN = c.GSTIN,
                    City = c.City,
                    State = c.State
                })
                .OrderBy(c => c.FirmName)
                .ThenBy(c => c.CustomerName)
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("ledger")]
        public async Task<IActionResult> GetLedger([FromQuery] Guid customerId)
        {
            var customer = await _context.Customers.FindAsync(customerId);
            if (customer == null) return BadRequest("Customer not found.");

            // 1. Get all Invoices (Debits)
            var invoices = await _context.ProformaInvoices
                .Where(p => p.CustomerId == customerId)
                .Select(p => new LedgerEntryDto
                {
                    Date = p.ProformaDate,
                    Type = "Invoice",
                    ReferenceNo = p.ProformaNo,
                    Particulars = "Proforma Invoice Sales",
                    Debit = p.NetAmount,
                    Credit = 0
                })
                .ToListAsync();

            // 2. Get all Collections (Credits)
            var collections = await _context.CustomerCollections
                .Where(cc => cc.CustomerId == customerId)
                .Select(cc => new LedgerEntryDto
                {
                    Date = cc.CollectionDate,
                    Type = "Collection",
                    ReferenceNo = cc.CollectionNo,
                    Particulars = $"Received via {cc.PaymentMode}" + (string.IsNullOrEmpty(cc.ReferenceNo) ? "" : $" (Ref: {cc.ReferenceNo})"),
                    Debit = 0,
                    Credit = cc.Amount
                })
                .ToListAsync();

            // Merge and sort
            var entries = invoices.Concat(collections)
                .OrderBy(e => e.Date)
                .ToList();

            // Calculate running balance
            decimal balance = 0;
            foreach (var entry in entries)
            {
                balance += (entry.Debit - entry.Credit);
                entry.Balance = balance;
            }

            return Ok(entries);
        }

        [HttpGet("outstanding")]
        public async Task<IActionResult> GetOutstanding([FromQuery] Guid? firmId)
        {
            var query = _context.Customers.AsQueryable();

            if (firmId.HasValue)
            {
                query = query.Where(c => c.FirmId == firmId.Value);
            }

            var customersList = await query.ToListAsync();
            var result = new List<OutstandingReportDto>();

            foreach (var cust in customersList)
            {
                var totalInvoiced = await _context.ProformaInvoices
                    .Where(p => p.CustomerId == cust.CustomerId)
                    .SumAsync(p => p.NetAmount);

                var totalCollected = await _context.CustomerCollections
                    .Where(cc => cc.CustomerId == cust.CustomerId)
                    .SumAsync(cc => cc.Amount);

                var outstanding = totalInvoiced - totalCollected;

                result.Add(new OutstandingReportDto
                {
                    FirmCode = cust.FirmCode,
                    FirmName = cust.FirmName,
                    CustomerCode = cust.CustomerCode,
                    CustomerName = cust.CustomerName,
                    MobileNo = cust.MobileNo,
                    GSTIN = cust.GSTIN,
                    TotalInvoiced = totalInvoiced,
                    TotalCollected = totalCollected,
                    OutstandingBalance = outstanding
                });
            }

            return Ok(result.OrderByDescending(r => r.OutstandingBalance).ToList());
        }

        [HttpGet("profit")]
        public async Task<IActionResult> GetProfitAnalysis([FromQuery] Guid? firmId, [FromQuery] DateTimeOffset? startDate, [FromQuery] DateTimeOffset? endDate)
        {
            var query = _context.ProformaInvoices
                .Include(p => p.Details)
                    .ThenInclude(d => d.Barcodes)
                .AsQueryable();

            if (firmId.HasValue)
            {
                query = query.Where(p => p.FirmId == firmId.Value);
            }

            if (startDate.HasValue)
            {
                query = query.Where(p => p.ProformaDate >= startDate.Value);
            }

            if (endDate.HasValue)
            {
                query = query.Where(p => p.ProformaDate <= endDate.Value);
            }

            var invoices = await query.ToListAsync();

            // Retrieve all cost rates from inward entries
            var trackingNos = invoices.SelectMany(p => p.Details)
                .SelectMany(d => d.Barcodes)
                .Select(b => b.TrackingNo)
                .Distinct()
                .ToList();

            var costRatesMap = await _context.StockInwardDetails
                .Where(d => trackingNos.Contains(d.TrackingNo))
                .Select(d => new { d.TrackingNo, d.Rate })
                .ToDictionaryAsync(d => d.TrackingNo, d => d.Rate);

            var profitList = new List<ProfitReportDto>();

            foreach (var inv in invoices)
            {
                foreach (var det in inv.Details)
                {
                    decimal totalCost = 0;
                    foreach (var bar in det.Barcodes)
                    {
                        if (costRatesMap.TryGetValue(bar.TrackingNo, out var costRate))
                        {
                            totalCost += bar.Quantity * costRate;
                        }
                    }

                    var revenue = det.TaxableValue; // Business income (excl GST)
                    var profit = revenue - totalCost;
                    var margin = revenue > 0 ? (profit / revenue) * 100 : 0;

                    profitList.Add(new ProfitReportDto
                    {
                        ProformaNo = inv.ProformaNo,
                        ProformaDate = inv.ProformaDate,
                        CustomerName = inv.CustomerName,
                        Particulars = det.Particulars,
                        Quantity = det.Quantity,
                        Revenue = revenue,
                        Cost = totalCost,
                        Profit = profit,
                        MarginPercent = margin
                    });
                }
            }

            return Ok(profitList.OrderByDescending(p => p.ProformaDate).ToList());
        }
    }
}
