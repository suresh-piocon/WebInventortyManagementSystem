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

        [Column(TypeName = "decimal(5, 2)")]
        public decimal GSTPercent { get; set; } = 18.00M;

        [StringLength(100)]
        public string? WarpType { get; set; }

        [StringLength(100)]
        public string? WeftType { get; set; }

        public Guid? WarpTypeId { get; set; }
        [ForeignKey(nameof(WarpTypeId))]
        public WarpTypeMaster? WarpTypeSpec { get; set; }

        public Guid? WeftTypeId { get; set; }
        [ForeignKey(nameof(WeftTypeId))]
        public WeftTypeMaster? WeftTypeSpec { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Wages { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal WarpWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal WeftWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal ZariWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal TotalWeight { get; set; } = 0;

        [StringLength(50)]
        public string? Reed { get; set; }

        [StringLength(50)]
        public string? Thread { get; set; }

        public int NoOfCards { get; set; } = 0;

        public int NoOfMarks { get; set; } = 0;

        public string? BodyImage { get; set; }

        public string? PalluImage { get; set; }

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

        public bool IsPrinted { get; set; } = false;

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

        [Column(TypeName = "decimal(12, 3)")]
        public decimal InwardWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal OutwardWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal BalanceWeight { get; set; } = 0;

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
        public string Type { get; set; } = "Batch";
        public decimal GSTPercent { get; set; }
        public string? HSNCode { get; set; }
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

    public class BarcodeDetailReportDto
    {
        public string SupplierName { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string BarcodeNo { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string TrackingNo { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTimeOffset InwardDate { get; set; }
        public DateTimeOffset? OutwardDate { get; set; }
        public string Status { get; set; } = "In Stock";
        public string? ImageUrl { get; set; }
    }

    public class BarcodeStockImageReportDto
    {
        public string BarcodeNo { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string? ImageUrl { get; set; }
        public DateTimeOffset InwardDate { get; set; }
        public string SupplierName { get; set; } = string.Empty;
        public int StockAgeDays { get; set; }
    }

    public class AllItemsLedgerDto
    {
        public Guid Id { get; set; }
        public Guid ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public DateTimeOffset TransactionDate { get; set; }
        public string TransactionType { get; set; } = string.Empty;
        public string ReferenceNo { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string TrackingNo { get; set; } = string.Empty;
        public decimal InwardQty { get; set; }
        public decimal OutwardQty { get; set; }
        public decimal BalanceQty { get; set; }
        public decimal InwardWeight { get; set; }
        public decimal OutwardWeight { get; set; }
        public decimal BalanceWeight { get; set; }
        public decimal UnitPrice { get; set; }
    }

    [Table("ProformaInvoices")]
    public class ProformaInvoice
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid? FirmId { get; set; }

        [ForeignKey(nameof(FirmId))]
        public Firm? Firm { get; set; }

        [StringLength(30)]
        public string? FirmCode { get; set; }

        [StringLength(150)]
        public string? FirmName { get; set; }

        public Guid? CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        [Required]
        [StringLength(30)]
        public string ProformaNo { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset ProformaDate { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [StringLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [StringLength(20)]
        public string? MobileNo { get; set; }

        public string? Address { get; set; }

        [StringLength(15)]
        public string? GSTIN { get; set; }

        [StringLength(100)]
        public string? State { get; set; }

        [Required]
        [StringLength(20)]
        public string TaxType { get; set; } = "Intra-State"; // "Intra-State" or "Inter-State"

        [Column(TypeName = "decimal(12, 2)")]
        public decimal TotalQty { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal TotalTaxableValue { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal TotalCGST { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal TotalSGST { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal TotalIGST { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal GrandTotal { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal RoundOff { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal NetAmount { get; set; }

        public bool IsConverted { get; set; } = false;

        public DateTimeOffset? ConvertedDate { get; set; }

        public Guid? ConvertedStockOutwardId { get; set; }

        [Required]
        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<ProformaInvoiceDetail> Details { get; set; } = new();
    }

    [Table("ProformaInvoiceDetails")]
    public class ProformaInvoiceDetail
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ProformaInvoiceId { get; set; }

        [ForeignKey(nameof(ProformaInvoiceId))]
        public ProformaInvoice? ProformaInvoice { get; set; }

        [Required]
        public Guid ItemId { get; set; }

        [ForeignKey(nameof(ItemId))]
        public Item? Item { get; set; }

        [Required]
        [StringLength(150)]
        public string Particulars { get; set; } = string.Empty;

        [StringLength(20)]
        public string? HSNCode { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Quantity { get; set; }

        [Column(TypeName = "decimal(12, 4)")]
        public decimal Rate { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal DiscountPercent { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal DiscountAmount { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal TaxableValue { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal GSTPercent { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal GSTAmount { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal LineTotal { get; set; }

        [Required]
        public string BarcodeList { get; set; } = string.Empty;

        public List<ProformaInvoiceDetailBarcode> Barcodes { get; set; } = new();
    }

    [Table("ProformaInvoiceDetailBarcodes")]
    public class ProformaInvoiceDetailBarcode
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ProformaInvoiceDetailId { get; set; }

        [ForeignKey(nameof(ProformaInvoiceDetailId))]
        public ProformaInvoiceDetail? ProformaInvoiceDetail { get; set; }

        [Required]
        [StringLength(50)]
        public string Barcode { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string BatchNo { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string TrackingNo { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Quantity { get; set; }
    }

    public class ProformaInvoicePostDto
    {
        public Guid? FirmId { get; set; }
        public string? FirmCode { get; set; }
        public string? FirmName { get; set; }
        public Guid? CustomerId { get; set; }
        public DateTimeOffset ProformaDate { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string? MobileNo { get; set; }
        public string? Address { get; set; }
        public string? GSTIN { get; set; }
        public string? State { get; set; }
        public string TaxType { get; set; } = "Intra-State";
        public List<ProformaInvoiceDetailPostDto> Details { get; set; } = new();
    }

    public class ProformaInvoiceDetailPostDto
    {
        public Guid ItemId { get; set; }
        public string Particulars { get; set; } = string.Empty;
        public string? HSNCode { get; set; }
        public decimal Quantity { get; set; }
        public decimal Rate { get; set; }
        public decimal DiscountPercent { get; set; }
        public decimal DiscountAmount { get; set; }
        public decimal TaxableValue { get; set; }
        public decimal GSTPercent { get; set; }
        public decimal GSTAmount { get; set; }
        public decimal LineTotal { get; set; }
        public List<ProformaInvoiceBarcodePostDto> ScannedBarcodes { get; set; } = new();
    }

    public class ProformaInvoiceBarcodePostDto
    {
        public string Barcode { get; set; } = string.Empty;
        public string BatchNo { get; set; } = string.Empty;
        public string TrackingNo { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
    }

    [Table("CustomerMaster")]
    public class Customer
    {
        [Key]
        public Guid CustomerId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid FirmId { get; set; }

        [ForeignKey(nameof(FirmId))]
        public Firm? Firm { get; set; }

        [Required]
        [StringLength(30)]
        public string FirmCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FirmName { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string CustomerCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [Required]
        [StringLength(20)]
        public string MobileNo { get; set; } = string.Empty;

        [StringLength(20)]
        public string? WhatsappNo { get; set; }

        [StringLength(100)]
        public string? Email { get; set; }

        [StringLength(15)]
        public string? GSTIN { get; set; }

        [StringLength(10)]
        public string? PANNo { get; set; }

        public string? Address1 { get; set; }
        public string? Address2 { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(100)]
        public string? State { get; set; }

        [StringLength(10)]
        public string? Pincode { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        [Required]
        [StringLength(20)]
        public string CustomerType { get; set; } = "Unregistered"; // "Registered" or "Unregistered"

        public int CreditDays { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal CreditLimit { get; set; } = 0;

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Active"; // "Active" or "Inactive"

        public string? Remarks { get; set; }

        public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ModifiedDate { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("FirmMaster")]
    public class Firm
    {
        [Key]
        public Guid FirmId { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(30)]
        public string FirmCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FirmName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ContactPerson { get; set; }

        [StringLength(20)]
        public string? MobileNo { get; set; }

        [StringLength(100)]
        public string? Email { get; set; }

        [StringLength(15)]
        public string? GSTIN { get; set; }

        [StringLength(10)]
        public string? PANNo { get; set; }

        public string? Address1 { get; set; }
        public string? Address2 { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(100)]
        public string? State { get; set; }

        [StringLength(10)]
        public string? Pincode { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Active"; // "Active" or "Inactive"

        public string? Remarks { get; set; }

        public DateTimeOffset CreatedDate { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset ModifiedDate { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("CustomerCollections")]
    public class CustomerCollection
    {
        [Key]
        public Guid CollectionId { get; set; } = Guid.NewGuid();

        [Required]
        public Guid CustomerId { get; set; }

        [ForeignKey(nameof(CustomerId))]
        public Customer? Customer { get; set; }

        [Required]
        [StringLength(150)]
        public string CustomerName { get; set; } = string.Empty;

        [Required]
        public Guid FirmId { get; set; }

        [ForeignKey(nameof(FirmId))]
        public Firm? Firm { get; set; }

        [Required]
        [StringLength(30)]
        public string FirmCode { get; set; } = string.Empty;

        [Required]
        [StringLength(150)]
        public string FirmName { get; set; } = string.Empty;

        [Required]
        [StringLength(30)]
        public string CollectionNo { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset CollectionDate { get; set; } = DateTimeOffset.UtcNow;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(20)]
        public string PaymentMode { get; set; } = "Cash"; // "Cash", "Bank", "UPI", etc.

        [StringLength(50)]
        public string? ReferenceNo { get; set; }

        public string? Remarks { get; set; }

        [Required]
        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public class FirmCustomerCountDto
    {
        public string FirmCode { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public int TotalCustomers { get; set; }
    }

    public class FirmCustomerListDto
    {
        public string FirmCode { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string MobileNo { get; set; } = string.Empty;
        public string? GSTIN { get; set; }
        public string? City { get; set; }
        public string? State { get; set; }
    }

    public class LedgerEntryDto
    {
        public DateTimeOffset Date { get; set; }
        public string Type { get; set; } = string.Empty; // "Invoice" / "Collection"
        public string ReferenceNo { get; set; } = string.Empty;
        public string Particulars { get; set; } = string.Empty;
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal Balance { get; set; }
    }

    public class OutstandingReportDto
    {
        public string FirmCode { get; set; } = string.Empty;
        public string FirmName { get; set; } = string.Empty;
        public string CustomerCode { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string MobileNo { get; set; } = string.Empty;
        public string? GSTIN { get; set; }
        public decimal TotalInvoiced { get; set; }
        public decimal TotalCollected { get; set; }
        public decimal OutstandingBalance { get; set; }
    }

    public class ProfitReportDto
    {
        public string ProformaNo { get; set; } = string.Empty;
        public DateTimeOffset ProformaDate { get; set; }
        public string CustomerName { get; set; } = string.Empty;
        public string Particulars { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal Revenue { get; set; }
        public decimal Cost { get; set; }
        public decimal Profit { get; set; }
        public decimal MarginPercent { get; set; }
    }

    // ==========================================
    // JOB WORK MODULE ENTITIES
    // ==========================================

    [Table("JobWorkMaster")]
    public class JobWorkMaster
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string Type { get; set; } = "Other"; // "Dyer", "Weaver", "Zari Worker", "Finishing Worker", "Other"

        public string? Address { get; set; }

        [StringLength(20)]
        public string? Mobile { get; set; }

        [StringLength(15)]
        public string? GSTIN { get; set; }

        [StringLength(100)]
        public string? LedgerAccount { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal WastePercentage { get; set; } = 0;

        public bool Active { get; set; } = true;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("LoomMaster")]
    public class LoomMaster
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string LoomNo { get; set; } = string.Empty;

        [Required]
        public Guid WeaverId { get; set; }

        [ForeignKey(nameof(WeaverId))]
        public JobWorkMaster? Weaver { get; set; }

        public bool Active { get; set; } = true;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("LoomAllocation")]
    public class LoomAllocation
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid LoomId { get; set; }

        [ForeignKey(nameof(LoomId))]
        public LoomMaster? Loom { get; set; }

        [Required]
        public Guid ItemId { get; set; } // Allocated Design

        [ForeignKey(nameof(ItemId))]
        public Item? Design { get; set; }

        [StringLength(100)]
        public string? SubWeaver { get; set; }

        [StringLength(50)]
        public string? WarpRefNo { get; set; }

        public DateTimeOffset StartDate { get; set; } = DateTimeOffset.UtcNow;

        public bool Active { get; set; } = true;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("DyeingIssues")]
    public class DyeingIssue
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string IssueNo { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset IssueDate { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public Guid DyerId { get; set; }

        [ForeignKey(nameof(DyerId))]
        public JobWorkMaster? Dyer { get; set; }

        public string? Narration { get; set; }

        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<DyeingIssueDetail> Details { get; set; } = new();
    }

    [Table("DyeingIssueDetails")]
    public class DyeingIssueDetail
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid DyeingIssueId { get; set; }

        [ForeignKey(nameof(DyeingIssueId))]
        public DyeingIssue? DyeingIssue { get; set; }

        public Guid? DesignId { get; set; }

        [ForeignKey(nameof(DesignId))]
        public Item? Design { get; set; }

        public Guid? WarpTypeId { get; set; }
        [ForeignKey(nameof(WarpTypeId))]
        public WarpTypeMaster? WarpTypeSpec { get; set; }

        public Guid? WeftTypeId { get; set; }
        [ForeignKey(nameof(WeftTypeId))]
        public WeftTypeMaster? WeftTypeSpec { get; set; }

        [Required]
        [StringLength(20)]
        public string YarnType { get; set; } = "Warp"; // "Warp" or "Weft"

        [StringLength(100)]
        public string? WarpYarn { get; set; }

        [StringLength(100)]
        public string? WeftYarn { get; set; }

        [StringLength(50)]
        public string? Color { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Qty { get; set; }

        [Column(TypeName = "decimal(12, 3)")]
        public decimal WeightKgs { get; set; }

        [Column(TypeName = "decimal(12, 4)")]
        public decimal Rate { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Amount { get; set; }
    }

    [Table("DyeingReceives")]
    public class DyeingReceive
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string ReceiveNo { get; set; } = string.Empty;

        [Required]
        public DateTimeOffset ReceiveDate { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        public Guid DyerId { get; set; }

        [ForeignKey(nameof(DyerId))]
        public JobWorkMaster? Dyer { get; set; }

        [StringLength(50)]
        public string? IssueReferenceNo { get; set; }

        [Column(TypeName = "decimal(5, 2)")]
        public decimal WastePercentage { get; set; } = 0;

        public bool AllowManualWaste { get; set; } = false;

        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        public List<DyeingReceiveDetail> Details { get; set; } = new();
    }

    [Table("DyeingReceiveDetails")]
    public class DyeingReceiveDetail
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid DyeingReceiveId { get; set; }

        [ForeignKey(nameof(DyeingReceiveId))]
        public DyeingReceive? DyeingReceive { get; set; }

        public Guid? DesignId { get; set; }

        [ForeignKey(nameof(DesignId))]
        public Item? Design { get; set; }

        public Guid? WarpTypeId { get; set; }
        [ForeignKey(nameof(WarpTypeId))]
        public WarpTypeMaster? WarpTypeSpec { get; set; }

        public Guid? WeftTypeId { get; set; }
        [ForeignKey(nameof(WeftTypeId))]
        public WeftTypeMaster? WeftTypeSpec { get; set; }

        [Required]
        [StringLength(20)]
        public string YarnType { get; set; } = "Warp"; // "Warp" or "Weft"

        [StringLength(50)]
        public string? DyedColor { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal IssuedQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal IssuedWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 4)")]
        public decimal Rate { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal QtyReceived { get; set; }

        [Column(TypeName = "decimal(12, 3)")]
        public decimal WeightReceived { get; set; }

        [Column(TypeName = "decimal(12, 3)")]
        public decimal WasteWeight { get; set; }
    }

    [Table("WeavingLedger")]
    public class WeavingLedgerEntry
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid LoomAllocationId { get; set; }

        [ForeignKey(nameof(LoomAllocationId))]
        public LoomAllocation? LoomAllocation { get; set; }

        [Required]
        public DateTimeOffset Date { get; set; } = DateTimeOffset.UtcNow;

        [Required]
        [StringLength(50)]
        public string EntryType { get; set; } = string.Empty; // "Dyed Warp", "Dyed Weft", "Zari", "Saree", "Cash", "NEFT", "DebitTransfer", "CreditTransfer", "TDS", "Requirement", "LnoChange", "WtAdjust"

        [StringLength(250)]
        public string? Details { get; set; }

        [Column(TypeName = "decimal(12, 2)")]
        public decimal WarpQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal IssuedWt { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal RodQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal RodWt { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Debit { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Credit { get; set; } = 0;

        [StringLength(500)]
        public string? Narration { get; set; }

        [Required]
        [StringLength(10)]
        public string Status { get; set; } = "S";

        public Guid CreatedBy { get; set; }

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("JobLedger")]
    public class JobLedger
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid JobWorkerId { get; set; }

        [ForeignKey(nameof(JobWorkerId))]
        public JobWorkMaster? JobWorker { get; set; }

        [Required]
        public DateTimeOffset TransactionDate { get; set; }

        [Required]
        [StringLength(50)]
        public string VoucherNo { get; set; } = string.Empty;

        [Required]
        [StringLength(250)]
        public string Particulars { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal IssueQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal ReceiveQty { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal IssueWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal ReceiveWeight { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Debit { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Credit { get; set; } = 0;

        [Column(TypeName = "decimal(12, 2)")]
        public decimal Balance { get; set; } = 0;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    // ==========================================
    // JOB WORK DTOs
    // ==========================================

    public class WeaverAccountDto
    {
        public Guid LoomAllocationId { get; set; }
        public string LoomNo { get; set; } = string.Empty;
        public string WeaverName { get; set; } = string.Empty;
        public string DesignName { get; set; } = string.Empty;
        public decimal TotalDebit { get; set; }
        public decimal TotalCredit { get; set; }
        public decimal OutstandingBalance { get; set; }
    }

    public class LoomBalanceDto
    {
        public string LoomNo { get; set; } = string.Empty;
        public string WeaverName { get; set; } = string.Empty;
        public string DesignName { get; set; } = string.Empty;
        public decimal IssuedWarpQty { get; set; }
        public decimal ReceivedSareeQty { get; set; }
        public decimal BalanceSareeQty { get; set; }
        public decimal IssuedWeight { get; set; }
        public decimal ReceivedWeight { get; set; }
        public decimal BalanceWeight { get; set; }
    }

    public class DesignBalanceDto
    {
        public string DesignName { get; set; } = string.Empty;
        public decimal PendingDyeingQty { get; set; }
        public decimal PendingWeavingQty { get; set; }
        public decimal FinishedSareeStock { get; set; }
    }

    public class JobWorkDashboardDto
    {
        public decimal DyerOutstanding { get; set; }
        public decimal WeaverOutstanding { get; set; }
        public List<LoomBalanceDto> LoomBalances { get; set; } = new();
        public List<DesignBalanceDto> DesignBalances { get; set; } = new();
        public decimal PendingDyeingQty { get; set; }
        public decimal PendingWeavingQty { get; set; }
        public decimal FinishedSareeStock { get; set; }
    }

    [Table("WarpTypeMaster")]
    public class WarpTypeMaster
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string WarpType { get; set; } = string.Empty;

        public int EndsCount { get; set; }

        public int YarnCount { get; set; }

        public string? Description { get; set; }

        public bool Active { get; set; } = true;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    [Table("WeftTypeMaster")]
    public class WeftTypeMaster
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string WeftType { get; set; } = string.Empty;

        [Column(TypeName = "decimal(12, 3)")]
        public decimal WeftPart1 { get; set; }

        [Column(TypeName = "decimal(12, 3)")]
        public decimal WeftPart2 { get; set; }

        public string? Description { get; set; }

        public bool Active { get; set; } = true;

        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
