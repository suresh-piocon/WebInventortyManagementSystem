using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagement.Shared
{
    public enum BarcodeType
    {
        Batch,
        Unique
    }

    public enum TransactionType
    {
        Purchase,
        Sales,
        Adjustment,
        Transfer
    }

    public enum UserRole
    {
        Admin,
        StoreManager,
        Viewer
    }

    [Table("Categories")]
    public class Category
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("Units")]
    public class Unit
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(10)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Name { get; set; } = string.Empty;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("Suppliers")]
    public class Supplier
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(20)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [StringLength(20)]
        public string? MobileNo { get; set; }

        [StringLength(15)]
        public string? GSTNo { get; set; }

        public string? Address { get; set; }

        [StringLength(100)]
        [EmailAddress]
        public string? Email { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Active";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("Items")]
    public class Item
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public Guid CategoryId { get; set; }
        
        [ForeignKey(nameof(CategoryId))]
        public Category? Category { get; set; }

        [Required]
        public Guid UnitId { get; set; }

        [ForeignKey(nameof(UnitId))]
        public Unit? Unit { get; set; }

        [StringLength(100)]
        public string? Brand { get; set; }

        [StringLength(20)]
        public string? HSNCode { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal MinimumStock { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal ReorderLevel { get; set; } = 0;

        [Required]
        [StringLength(20)]
        public string BarcodeType { get; set; } = "Batch";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("StockInward")]
    public class StockInward
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(30)]
        public string InwardNo { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset InwardDate { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public Guid SupplierId { get; set; }

        [ForeignKey(nameof(SupplierId))]
        public Supplier? Supplier { get; set; }

        [StringLength(50)]
        public string? InvoiceNo { get; set; }

        public DateTimeOffset? InvoiceDate { get; set; }

        [Required]
        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<StockInwardDetail> Details { get; set; } = new();
    }

    [Table("StockInwardDetails")]
    public class StockInwardDetail
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid StockInwardId { get; set; }

        [ForeignKey(nameof(StockInwardId))]
        public StockInward? StockInward { get; set; }

        [Required]
        public Guid ItemId { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [StringLength(50)]
        public string? Color { get; set; }

        [StringLength(100)]
        public string? Design { get; set; }

        [StringLength(50)]
        public string? Size { get; set; }

        [Required]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(12, 4)")]
        public decimal Rate { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(30)]
        public string TrackingNo { get; set; } = string.Empty;
    }

    [Table("StockOutward")]
    public class StockOutward
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(30)]
        public string OutwardNo { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset OutwardDate { get; set; } = DateTimeOffset.UtcNow;

        [StringLength(150)]
        public string? CustomerName { get; set; }

        [StringLength(50)]
        public string? ReferenceNo { get; set; }

        [Required]
        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<StockOutwardDetail> Details { get; set; } = new();
    }

    [Table("StockOutwardDetails")]
    public class StockOutwardDetail
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid StockOutwardId { get; set; }

        [ForeignKey(nameof(StockOutwardId))]
        public StockOutward? StockOutward { get; set; }

        [Required]
        public Guid ItemId { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Required]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string TrackingNo { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Barcode { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(12, 4)")]
        public decimal Rate { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Amount { get; set; }
    }

    [Table("BarcodeMaster")]
    public class BarcodeMaster
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Barcode { get; set; } = string.Empty;

        [Required]
        public Guid ItemId { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Required]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string TrackingNo { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Type { get; set; } = "Batch";

        [StringLength(500)]
        public string? ImageUrl { get; set; }

        public bool IsUsed { get; set; } = false;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("QRCodeMaster")]
    public class QRCodeMaster
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public string QRCode { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string TrackingNo { get; set; } = string.Empty;

        [Required]
        public Guid SupplierId { get; set; }

        [ForeignKey(nameof(SupplierId))]
        public Supplier? Supplier { get; set; }

        [Required]
        public Guid ItemId { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Required]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Quantity { get; set; }

        [Required]
        public DateTimeOffset InwardDate { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("StockLedger")]
    public class StockLedger
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ItemId { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Required]
        public DateTimeOffset TransactionDate { get; set; }

        [Required]
        [StringLength(20)]
        public string TransactionType { get; set; } = "Purchase";

        [Required]
        [StringLength(50)]
        public string ReferenceNo { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string TrackingNo { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal InwardQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal OutwardQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal BalanceQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 4)")]
        public decimal UnitPrice { get; set; } = 0;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("Users")]
    public class UserProfile
    {
        [Key]
        public Guid Id { get; set; }

        [Required]
        [StringLength(100)]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [StringLength(150)]
        public string? FullName { get; set; }

        [Required]
        [StringLength(20)]
        public string Role { get; set; } = "Viewer";

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Active";

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("AuditLogs")]
    public class AuditLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid UserId { get; set; }

        [Required]
        [StringLength(20)]
        public string Action { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string TableName { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string RecordId { get; set; } = string.Empty;

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        [Required]
        public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    }

    // ==========================================
    // SHARED DTO CONTRACTS
    // ==========================================

    public class UserSyncDto
    {
        public string FullName { get; set; } = string.Empty;
    }

    public class UserRoleUpdateDto
    {
        public Guid UserId { get; set; }
        public string Role { get; set; } = "Viewer";
        public string Status { get; set; } = "Active";
    }

    public class StockInwardPostDto
    {
        public DateTimeOffset InwardDate { get; set; }
        public Guid SupplierId { get; set; }
        public string? InvoiceNo { get; set; }
        public DateTimeOffset? InvoiceDate { get; set; }
        public List<StockInwardDetailDto> Details { get; set; } = new();
    }

    public class StockInwardDetailDto
    {
        public Guid ItemId { get; set; }
        public string? Color { get; set; }
        public string? Design { get; set; }
        public string? Size { get; set; }
        public string BatchNo { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class ScannedItemDto
    {
        public Guid ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string UnitCode { get; set; } = "PCS";
        public string BatchNo { get; set; } = string.Empty;
        public string TrackingNo { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal AvailableQuantity { get; set; }
        public decimal Rate { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class StockOutwardPostDto
    {
        public DateTimeOffset OutwardDate { get; set; }
        public string? CustomerName { get; set; }
        public string? ReferenceNo { get; set; }
        public List<StockOutwardDetailDto> Details { get; set; } = new();
    }

    public class StockOutwardDetailDto
    {
        public Guid ItemId { get; set; }
        public string BatchNo { get; set; } = string.Empty;
        public string TrackingNo { get; set; } = string.Empty;
        public string Barcode { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
    }

    public class DashboardDto
    {
        public int TotalSuppliers { get; set; }
        public int TotalItems { get; set; }
        public decimal TodayInward { get; set; }
        public decimal TodayOutward { get; set; }
        public decimal CurrentStockValue { get; set; }
        public List<LowStockDto> LowStockItems { get; set; } = new();
        public List<MonthlyChartDto> MonthlyChartData { get; set; } = new();
        public List<TopSupplierDto> TopSuppliers { get; set; } = new();
    }

    public class LowStockDto
    {
        public Guid ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string UnitCode { get; set; } = string.Empty;
        public decimal CurrentStock { get; set; }
        public decimal MinStock { get; set; }
        public decimal ReorderLevel { get; set; }
    }

    public class MonthlyChartDto
    {
        public string MonthName { get; set; } = string.Empty;
        public decimal InwardQty { get; set; }
        public decimal OutwardQty { get; set; }
    }

    public class TopSupplierDto
    {
        public string SupplierName { get; set; } = string.Empty;
        public decimal TotalQty { get; set; }
    }

    public class SupplierStockReportDto
    {
        public string SupplierName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string TrackingNo { get; set; } = string.Empty;
        public decimal InwardQty { get; set; }
        public decimal OutwardQty { get; set; }
        public decimal BalanceQty { get; set; }
        public decimal UnitCost { get; set; }
        public decimal Value { get; set; }
        public string? ImageUrl { get; set; }
    }

    public class SupplierPurchaseReportDto
    {
        public Guid Id { get; set; } // StockInwardDetail Id
        public Guid StockInwardId { get; set; } // StockInward Id
        public DateTimeOffset InwardDate { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public string InvoiceNo { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
        public decimal Amount { get; set; }
    }

    public class BarcodeTrackingReportDto
    {
        public string TrackingNo { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string SupplierName { get; set; } = string.Empty;
        public DateTimeOffset InwardDate { get; set; }
        public string? InvoiceNo { get; set; }
        public decimal QuantityInward { get; set; }
        public decimal Rate { get; set; }
        public string? PhotoUrl { get; set; }
        public List<string> RegisteredBarcodes { get; set; } = new();
        public List<BarcodeIssueDto> Issues { get; set; } = new();
        public List<StockLedger> LedgerEntries { get; set; } = new();
    }

    public class BarcodeIssueDto
    {
        public string OutwardNo { get; set; } = string.Empty;
        public DateTimeOffset OutwardDate { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public decimal QuantityIssued { get; set; }
        public decimal Rate { get; set; }
    }

    public class AuditLogReportDto
    {
        public Guid Id { get; set; }
        public string Action { get; set; } = string.Empty;
        public string TableName { get; set; } = string.Empty;
        public string RecordId { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string? NewValue { get; set; }
        public DateTimeOffset Timestamp { get; set; }
        public string UserEmail { get; set; } = string.Empty;
    }
}
