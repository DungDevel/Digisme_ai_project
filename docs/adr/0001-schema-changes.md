# Schema Changes — ADR 0001 (Separate Supplier from Customer)

Document ghi nhận các thay đổi schema theo từng commit để hỗ trợ gộp nhánh (rebase / sinh lại migration).

## Commit C1: AddSupplier
- **Bảng mới**: `Suppliers`
  - `Id`: `int`, PK, Identity
  - `Code`: `varchar(32)`, NOT NULL, Unique Index (`IX_Suppliers_Code`)
  - `Name`: `varchar(128)`, NOT NULL
  - `Phone`: `varchar(32)`, NULL
  - `Address`: `varchar(256)`, NULL
  - `TaxCode`: `varchar(64)`, NULL
  - `IsActive`: `boolean`, NOT NULL (default `true`)
  - `CreatedAt`: `timestamp with time zone`, NOT NULL
  - `UpdatedAt`: `timestamp with time zone`, NULL
- **Ràng buộc**: Unique index trên `Code`. Unique case-insensitive check trên `Name` (được validate ở tầng Service cho các record active).
