CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" TEXT NOT NULL CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY,
    "ProductVersion" TEXT NOT NULL
);

BEGIN TRANSACTION;
CREATE TABLE "AuditLogs" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_AuditLogs" PRIMARY KEY,
    "UserId" TEXT NOT NULL,
    "Action" TEXT NOT NULL,
    "TableName" TEXT NOT NULL,
    "RecordId" TEXT NOT NULL,
    "OldValue" TEXT NULL,
    "NewValue" TEXT NULL,
    "Timestamp" TEXT NOT NULL
);

CREATE TABLE "Categories" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Categories" PRIMARY KEY,
    "Name" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);

CREATE TABLE "StockOutward" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StockOutward" PRIMARY KEY,
    "OutwardNo" TEXT NOT NULL,
    "OutwardDate" TEXT NOT NULL,
    "CustomerName" TEXT NULL,
    "ReferenceNo" TEXT NULL,
    "CreatedBy" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);

CREATE TABLE "Suppliers" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Suppliers" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "ContactPerson" TEXT NULL,
    "MobileNo" TEXT NULL,
    "GSTNo" TEXT NULL,
    "Address" TEXT NULL,
    "Email" TEXT NULL,
    "Status" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);

CREATE TABLE "Units" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Units" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);

CREATE TABLE "Users" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Users" PRIMARY KEY,
    "Email" TEXT NOT NULL,
    "FullName" TEXT NULL,
    "Role" TEXT NOT NULL,
    "Status" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL
);

CREATE TABLE "StockInward" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StockInward" PRIMARY KEY,
    "InwardNo" TEXT NOT NULL,
    "InwardDate" TEXT NOT NULL,
    "SupplierId" TEXT NOT NULL,
    "InvoiceNo" TEXT NULL,
    "InvoiceDate" TEXT NULL,
    "CreatedBy" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_StockInward_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "Items" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_Items" PRIMARY KEY,
    "Code" TEXT NOT NULL,
    "Name" TEXT NOT NULL,
    "CategoryId" TEXT NOT NULL,
    "UnitId" TEXT NOT NULL,
    "Brand" TEXT NULL,
    "HSNCode" TEXT NULL,
    "MinimumStock" decimal(12, 2) NOT NULL,
    "ReorderLevel" decimal(12, 2) NOT NULL,
    "BarcodeType" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_Items_Categories_CategoryId" FOREIGN KEY ("CategoryId") REFERENCES "Categories" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_Items_Units_UnitId" FOREIGN KEY ("UnitId") REFERENCES "Units" ("Id") ON DELETE CASCADE
);

CREATE TABLE "BarcodeMaster" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_BarcodeMaster" PRIMARY KEY,
    "Barcode" TEXT NOT NULL,
    "ItemId" TEXT NOT NULL,
    "BatchNo" TEXT NOT NULL,
    "TrackingNo" TEXT NOT NULL,
    "Type" TEXT NOT NULL,
    "ImageUrl" TEXT NULL,
    "IsUsed" INTEGER NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_BarcodeMaster_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE
);

CREATE TABLE "QRCodeMaster" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_QRCodeMaster" PRIMARY KEY,
    "QRCode" TEXT NOT NULL,
    "TrackingNo" TEXT NOT NULL,
    "SupplierId" TEXT NOT NULL,
    "ItemId" TEXT NOT NULL,
    "BatchNo" TEXT NOT NULL,
    "Quantity" decimal(12, 2) NOT NULL,
    "InwardDate" TEXT NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_QRCodeMaster_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_QRCodeMaster_Suppliers_SupplierId" FOREIGN KEY ("SupplierId") REFERENCES "Suppliers" ("Id") ON DELETE CASCADE
);

CREATE TABLE "StockInwardDetails" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StockInwardDetails" PRIMARY KEY,
    "StockInwardId" TEXT NOT NULL,
    "ItemId" TEXT NOT NULL,
    "Color" TEXT NULL,
    "Design" TEXT NULL,
    "Size" TEXT NULL,
    "BatchNo" TEXT NOT NULL,
    "Quantity" decimal(12, 2) NOT NULL,
    "Rate" decimal(12, 4) NOT NULL,
    "Amount" decimal(12, 2) NOT NULL,
    "TrackingNo" TEXT NOT NULL,
    CONSTRAINT "FK_StockInwardDetails_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_StockInwardDetails_StockInward_StockInwardId" FOREIGN KEY ("StockInwardId") REFERENCES "StockInward" ("Id") ON DELETE CASCADE
);

CREATE TABLE "StockLedger" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StockLedger" PRIMARY KEY,
    "ItemId" TEXT NOT NULL,
    "TransactionDate" TEXT NOT NULL,
    "TransactionType" TEXT NOT NULL,
    "ReferenceNo" TEXT NOT NULL,
    "BatchNo" TEXT NOT NULL,
    "TrackingNo" TEXT NOT NULL,
    "InwardQty" decimal(12, 2) NOT NULL,
    "OutwardQty" decimal(12, 2) NOT NULL,
    "BalanceQty" decimal(12, 2) NOT NULL,
    "UnitPrice" decimal(12, 4) NOT NULL,
    "CreatedAt" TEXT NOT NULL,
    CONSTRAINT "FK_StockLedger_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE
);

CREATE TABLE "StockOutwardDetails" (
    "Id" TEXT NOT NULL CONSTRAINT "PK_StockOutwardDetails" PRIMARY KEY,
    "StockOutwardId" TEXT NOT NULL,
    "ItemId" TEXT NOT NULL,
    "BatchNo" TEXT NOT NULL,
    "TrackingNo" TEXT NOT NULL,
    "Barcode" TEXT NOT NULL,
    "Quantity" decimal(12, 2) NOT NULL,
    "Rate" decimal(12, 4) NOT NULL,
    "Amount" decimal(12, 2) NOT NULL,
    CONSTRAINT "FK_StockOutwardDetails_Items_ItemId" FOREIGN KEY ("ItemId") REFERENCES "Items" ("Id") ON DELETE CASCADE,
    CONSTRAINT "FK_StockOutwardDetails_StockOutward_StockOutwardId" FOREIGN KEY ("StockOutwardId") REFERENCES "StockOutward" ("Id") ON DELETE CASCADE
);

CREATE UNIQUE INDEX "IX_BarcodeMaster_Barcode" ON "BarcodeMaster" ("Barcode");

CREATE INDEX "IX_BarcodeMaster_ItemId" ON "BarcodeMaster" ("ItemId");

CREATE UNIQUE INDEX "IX_Categories_Name" ON "Categories" ("Name");

CREATE INDEX "IX_Items_CategoryId" ON "Items" ("CategoryId");

CREATE UNIQUE INDEX "IX_Items_Code" ON "Items" ("Code");

CREATE INDEX "IX_Items_UnitId" ON "Items" ("UnitId");

CREATE INDEX "IX_QRCodeMaster_ItemId" ON "QRCodeMaster" ("ItemId");

CREATE UNIQUE INDEX "IX_QRCodeMaster_QRCode" ON "QRCodeMaster" ("QRCode");

CREATE INDEX "IX_QRCodeMaster_SupplierId" ON "QRCodeMaster" ("SupplierId");

CREATE UNIQUE INDEX "IX_QRCodeMaster_TrackingNo" ON "QRCodeMaster" ("TrackingNo");

CREATE UNIQUE INDEX "IX_StockInward_InwardNo" ON "StockInward" ("InwardNo");

CREATE INDEX "IX_StockInward_SupplierId" ON "StockInward" ("SupplierId");

CREATE INDEX "IX_StockInwardDetails_ItemId" ON "StockInwardDetails" ("ItemId");

CREATE INDEX "IX_StockInwardDetails_StockInwardId" ON "StockInwardDetails" ("StockInwardId");

CREATE UNIQUE INDEX "IX_StockInwardDetails_TrackingNo" ON "StockInwardDetails" ("TrackingNo");

CREATE INDEX "IX_StockLedger_ItemId" ON "StockLedger" ("ItemId");

CREATE UNIQUE INDEX "IX_StockOutward_OutwardNo" ON "StockOutward" ("OutwardNo");

CREATE INDEX "IX_StockOutwardDetails_ItemId" ON "StockOutwardDetails" ("ItemId");

CREATE INDEX "IX_StockOutwardDetails_StockOutwardId" ON "StockOutwardDetails" ("StockOutwardId");

CREATE UNIQUE INDEX "IX_Suppliers_Code" ON "Suppliers" ("Code");

CREATE UNIQUE INDEX "IX_Units_Code" ON "Units" ("Code");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260629140204_InitialCreate', '9.0.2');

COMMIT;

