using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using InventoryManagement.Shared;

namespace InventoryManagement.Api.Data
{
    public interface ICurrentUserService
    {
        Guid UserId { get; }
    }

    public class InventoryDbContext : DbContext
    {
        private readonly ICurrentUserService _currentUserService;

        public InventoryDbContext(DbContextOptions<InventoryDbContext> options, ICurrentUserService currentUserService)
            : base(options)
        {
            _currentUserService = currentUserService;
        }

        public DbSet<Category> Categories => Set<Category>();
        public DbSet<Unit> Units => Set<Unit>();
        public DbSet<Supplier> Suppliers => Set<Supplier>();
        public DbSet<Item> Items => Set<Item>();
        public DbSet<StockInward> StockInwards => Set<StockInward>();
        public DbSet<StockInwardDetail> StockInwardDetails => Set<StockInwardDetail>();
        public DbSet<StockOutward> StockOutwards => Set<StockOutward>();
        public DbSet<StockOutwardDetail> StockOutwardDetails => Set<StockOutwardDetail>();
        public DbSet<BarcodeMaster> BarcodeMasters => Set<BarcodeMaster>();
        public DbSet<QRCodeMaster> QRCodeMasters => Set<QRCodeMaster>();
        public DbSet<StockLedger> StockLedgers => Set<StockLedger>();
        public DbSet<UserProfile> UserProfiles => Set<UserProfile>();
        public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
        public DbSet<ProformaInvoice> ProformaInvoices => Set<ProformaInvoice>();
        public DbSet<ProformaInvoiceDetail> ProformaInvoiceDetails => Set<ProformaInvoiceDetail>();
        public DbSet<ProformaInvoiceDetailBarcode> ProformaInvoiceDetailBarcodes => Set<ProformaInvoiceDetailBarcode>();
        public DbSet<Customer> Customers => Set<Customer>();
        public DbSet<Firm> Firms => Set<Firm>();
        public DbSet<CustomerCollection> CustomerCollections => Set<CustomerCollection>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure unique constraints
            modelBuilder.Entity<Category>().HasIndex(c => c.Name).IsUnique();
            modelBuilder.Entity<Unit>().HasIndex(u => u.Code).IsUnique();
            modelBuilder.Entity<Supplier>().HasIndex(s => s.Code).IsUnique();
            modelBuilder.Entity<Item>().HasIndex(i => i.Code).IsUnique();
            modelBuilder.Entity<StockInward>().HasIndex(si => si.InwardNo).IsUnique();
            modelBuilder.Entity<StockInwardDetail>().HasIndex(sid => sid.TrackingNo);
            modelBuilder.Entity<StockOutward>().HasIndex(so => so.OutwardNo).IsUnique();
            modelBuilder.Entity<BarcodeMaster>().HasIndex(bm => bm.Barcode).IsUnique();
            modelBuilder.Entity<QRCodeMaster>().HasIndex(qm => qm.QRCode).IsUnique();
            modelBuilder.Entity<QRCodeMaster>().HasIndex(qm => qm.TrackingNo);
            modelBuilder.Entity<Customer>().HasIndex(c => c.MobileNo).IsUnique();
            modelBuilder.Entity<Customer>().HasIndex(c => c.CustomerCode).IsUnique();
            modelBuilder.Entity<Firm>().HasIndex(f => f.FirmCode).IsUnique();
            modelBuilder.Entity<CustomerCollection>().HasIndex(cc => cc.CollectionNo).IsUnique();

            // Set up cascading deletes
            modelBuilder.Entity<StockInwardDetail>()
                .HasOne(d => d.StockInward)
                .WithMany(m => m.Details)
                .HasForeignKey(d => d.StockInwardId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<StockOutwardDetail>()
                .HasOne(d => d.StockOutward)
                .WithMany(m => m.Details)
                .HasForeignKey(d => d.StockOutwardId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProformaInvoiceDetail>()
                .HasOne(d => d.ProformaInvoice)
                .WithMany(m => m.Details)
                .HasForeignKey(d => d.ProformaInvoiceId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProformaInvoiceDetailBarcode>()
                .HasOne(b => b.ProformaInvoiceDetail)
                .WithMany(d => d.Barcodes)
                .HasForeignKey(b => b.ProformaInvoiceDetailId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProformaInvoice>()
                .HasOne(p => p.Customer)
                .WithMany()
                .HasForeignKey(p => p.CustomerId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<Customer>()
                .HasOne(c => c.Firm)
                .WithMany()
                .HasForeignKey(c => c.FirmId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<CustomerCollection>()
                .HasOne(cc => cc.Customer)
                .WithMany()
                .HasForeignKey(cc => cc.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<CustomerCollection>()
                .HasOne(cc => cc.Firm)
                .WithMany()
                .HasForeignKey(cc => cc.FirmId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ProformaInvoice>()
                .HasOne(p => p.Firm)
                .WithMany()
                .HasForeignKey(p => p.FirmId)
                .OnDelete(DeleteBehavior.SetNull);

            // Precision for decimals
            modelBuilder.Entity<Item>()
                .Property(i => i.MinimumStock).HasPrecision(12, 2);
            modelBuilder.Entity<Item>()
                .Property(i => i.ReorderLevel).HasPrecision(12, 2);

            modelBuilder.Entity<StockInwardDetail>()
                .Property(d => d.Quantity).HasPrecision(12, 2);
            modelBuilder.Entity<StockInwardDetail>()
                .Property(d => d.Rate).HasPrecision(12, 4);
            modelBuilder.Entity<StockInwardDetail>()
                .Property(d => d.Amount).HasPrecision(12, 2);

            modelBuilder.Entity<StockOutwardDetail>()
                .Property(d => d.Quantity).HasPrecision(12, 2);
            modelBuilder.Entity<StockOutwardDetail>()
                .Property(d => d.Rate).HasPrecision(12, 4);
            modelBuilder.Entity<StockOutwardDetail>()
                .Property(d => d.Amount).HasPrecision(12, 2);

            modelBuilder.Entity<QRCodeMaster>()
                .Property(q => q.Quantity).HasPrecision(12, 2);

            modelBuilder.Entity<StockLedger>()
                .Property(sl => sl.InwardQty).HasPrecision(12, 2);
            modelBuilder.Entity<StockLedger>()
                .Property(sl => sl.OutwardQty).HasPrecision(12, 2);
            modelBuilder.Entity<StockLedger>()
                .Property(sl => sl.BalanceQty).HasPrecision(12, 2);
            modelBuilder.Entity<StockLedger>()
                .Property(sl => sl.UnitPrice).HasPrecision(12, 4);

            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.Quantity).HasPrecision(12, 2);
            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.Rate).HasPrecision(12, 4);
            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.DiscountPercent).HasPrecision(5, 2);
            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.DiscountAmount).HasPrecision(12, 2);
            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.TaxableValue).HasPrecision(12, 2);
            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.GSTPercent).HasPrecision(5, 2);
            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.GSTAmount).HasPrecision(12, 2);
            modelBuilder.Entity<ProformaInvoiceDetail>()
                .Property(d => d.LineTotal).HasPrecision(12, 2);

            modelBuilder.Entity<ProformaInvoiceDetailBarcode>()
                .Property(b => b.Quantity).HasPrecision(12, 2);

            modelBuilder.Entity<Customer>()
                .Property(c => c.CreditLimit).HasPrecision(12, 2);

            modelBuilder.Entity<CustomerCollection>()
                .Property(cc => cc.Amount).HasPrecision(12, 2);
        }

        public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            var auditEntries = OnBeforeSaveChanges();
            var result = await base.SaveChangesAsync(cancellationToken);
            await OnAfterSaveChangesAsync(auditEntries, cancellationToken);
            return result;
        }

        public override int SaveChanges()
        {
            var auditEntries = OnBeforeSaveChanges();
            var result = base.SaveChanges();
            OnAfterSaveChangesAsync(auditEntries).GetAwaiter().GetResult();
            return result;
        }

        private List<AuditEntry> OnBeforeSaveChanges()
        {
            ChangeTracker.DetectChanges();
            var auditEntries = new List<AuditEntry>();
            var userId = _currentUserService.UserId;

            // Don't audit AuditLogs table
            foreach (var entry in ChangeTracker.Entries())
            {
                if (entry.Entity is AuditLog || entry.State == EntityState.Detached || entry.State == EntityState.Unchanged)
                    continue;

                var auditEntry = new AuditEntry(entry)
                {
                    TableName = entry.Metadata.GetTableName() ?? entry.Metadata.ClrType.Name,
                    UserId = userId,
                    Action = entry.State.ToString().ToUpper()
                };
                auditEntries.Add(auditEntry);

                foreach (var property in entry.Properties)
                {
                    string propertyName = property.Metadata.Name;
                    if (property.Metadata.IsPrimaryKey())
                    {
                        auditEntry.KeyValues[propertyName] = property.CurrentValue;
                        continue;
                    }

                    switch (entry.State)
                    {
                        case EntityState.Added:
                            auditEntry.NewValues[propertyName] = property.CurrentValue;
                            break;

                        case EntityState.Deleted:
                            auditEntry.OldValues[propertyName] = property.OriginalValue;
                            break;

                        case EntityState.Modified:
                            if (property.IsModified)
                            {
                                auditEntry.OldValues[propertyName] = property.OriginalValue;
                                auditEntry.NewValues[propertyName] = property.CurrentValue;
                            }
                            break;
                    }
                }
            }

            return auditEntries;
        }

        private Task OnAfterSaveChangesAsync(List<AuditEntry> auditEntries, CancellationToken cancellationToken = default)
        {
            if (auditEntries == null || auditEntries.Count == 0)
                return Task.CompletedTask;

            foreach (var auditEntry in auditEntries)
            {
                var auditLog = new AuditLog
                {
                    Id = Guid.NewGuid(),
                    UserId = auditEntry.UserId,
                    Action = auditEntry.Action,
                    TableName = auditEntry.TableName,
                    Timestamp = DateTimeOffset.UtcNow,
                    RecordId = JsonSerializer.Serialize(auditEntry.KeyValues),
                    OldValue = auditEntry.OldValues.Any() ? JsonSerializer.Serialize(auditEntry.OldValues) : null,
                    NewValue = auditEntry.NewValues.Any() ? JsonSerializer.Serialize(auditEntry.NewValues) : null
                };

                AuditLogs.Add(auditLog);
            }

            return base.SaveChangesAsync(cancellationToken);
        }
    }

    internal class AuditEntry
    {
        public AuditEntry(EntityEntry entry)
        {
            Entry = entry;
        }

        public EntityEntry Entry { get; }
        public Guid UserId { get; set; }
        public string TableName { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public Dictionary<string, object?> KeyValues { get; } = new();
        public Dictionary<string, object?> OldValues { get; } = new();
        public Dictionary<string, object?> NewValues { get; } = new();
    }
}
