using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using InventoryManagement.Api.Data;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Services
{
    public class ValuationResult
    {
        public decimal TotalQuantity { get; set; }
        public decimal TotalValue { get; set; }
        public decimal UnitCost { get; set; }
    }

    public class ValuationService
    {
        private readonly InventoryDbContext _context;

        public ValuationService(InventoryDbContext context)
        {
            _context = context;
        }

        public async Task<ValuationResult> CalculateValuationAsync(Guid itemId, string method)
        {
            // Get all ledger entries for the item in chronological order
            var ledgerEntries = await _context.StockLedgers
                .Where(l => l.ItemId == itemId)
                .OrderBy(l => l.TransactionDate)
                .ThenBy(l => l.CreatedAt)
                .ToListAsync();

            if (!ledgerEntries.Any())
            {
                return new ValuationResult { TotalQuantity = 0, TotalValue = 0, UnitCost = 0 };
            }

            if (method.Equals("FIFO", StringComparison.OrdinalIgnoreCase))
            {
                return CalculateFifo(ledgerEntries);
            }
            else // Default to Weighted Average
            {
                return CalculateWeightedAverage(ledgerEntries);
            }
        }

        private ValuationResult CalculateFifo(System.Collections.Generic.List<StockLedger> entries)
        {
            // Collect all inward transactions (purchases)
            var purchases = entries
                .Where(e => e.InwardQty > 0)
                .Select(e => new FifoBatch
                {
                    Quantity = e.InwardQty,
                    RemainingQuantity = e.InwardQty,
                    Rate = e.UnitPrice
                })
                .ToList();

            // Calculate total outward quantity (sales/issues)
            decimal totalOutward = entries.Where(e => e.OutwardQty > 0).Sum(e => e.OutwardQty);

            // Deduct outward quantity from purchases in FIFO order
            decimal remainingOutward = totalOutward;
            foreach (var purchase in purchases)
            {
                if (remainingOutward <= 0) break;

                if (purchase.RemainingQuantity <= remainingOutward)
                {
                    remainingOutward -= purchase.RemainingQuantity;
                    purchase.RemainingQuantity = 0;
                }
                else
                {
                    purchase.RemainingQuantity -= remainingOutward;
                    remainingOutward = 0;
                }
            }

            // Value the remaining stock
            decimal totalQty = 0;
            decimal totalValue = 0;

            foreach (var purchase in purchases)
            {
                if (purchase.RemainingQuantity > 0)
                {
                    totalQty += purchase.RemainingQuantity;
                    totalValue += purchase.RemainingQuantity * purchase.Rate;
                }
            }

            return new ValuationResult
            {
                TotalQuantity = totalQty,
                TotalValue = totalValue,
                UnitCost = totalQty > 0 ? Math.Round(totalValue / totalQty, 4) : 0
            };
        }

        private ValuationResult CalculateWeightedAverage(System.Collections.Generic.List<StockLedger> entries)
        {
            decimal currentQty = 0;
            decimal currentAvgCost = 0;

            foreach (var entry in entries)
            {
                if (entry.InwardQty > 0)
                {
                    decimal newQty = currentQty + entry.InwardQty;
                    if (newQty > 0)
                    {
                        currentAvgCost = ((currentQty * currentAvgCost) + (entry.InwardQty * entry.UnitPrice)) / newQty;
                    }
                    currentQty = newQty;
                }
                else if (entry.OutwardQty > 0)
                {
                    currentQty = Math.Max(0, currentQty - entry.OutwardQty);
                    // Average cost does not change when issuing stock
                }
            }

            return new ValuationResult
            {
                TotalQuantity = currentQty,
                TotalValue = Math.Round(currentQty * currentAvgCost, 2),
                UnitCost = Math.Round(currentAvgCost, 4)
            };
        }

        private class FifoBatch
        {
            public decimal Quantity { get; set; }
            public decimal RemainingQuantity { get; set; }
            public decimal Rate { get; set; }
        }
    }
}
