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

## Commit C2: AddCustomerGroup
- **Bảng mới**: `CustomerGroups`
  - `Id`: `int`, PK, Identity
  - `Code`: `varchar(32)`, NOT NULL, Unique Index (`IX_CustomerGroups_Code`)
  - `Name`: `varchar(128)`, NOT NULL
  - `IsSupplier`: `boolean`, NOT NULL (false = Customer Group, true = Supplier Group)
  - `DefaultDiscountRate`: `numeric(5,2)`, NOT NULL (0..100)
  - `ParentId`: `int`, FK self-referencing (Restrict)
  - `IsActive`: `boolean`, NOT NULL (default `true`)
  - `CreatedAt`: `timestamp with time zone`, NOT NULL
  - `UpdatedAt`: `timestamp with time zone`, NULL
- **Cột mới**: `CustomerGroupId` (`int`, FK tới `CustomerGroups`, NULL) trên bảng `Customers` và `Suppliers`.
- **Quy tắc nghiệp vụ**:
  - Kiểm tra `IsSupplier` khớp giữa nhóm cha và nhóm con, và khớp khi gán vào `Customer` (`IsSupplier=false`) hoặc `Supplier` (`IsSupplier=true`).
  - Không cho phép đổi `IsSupplier` của nhóm nếu nhóm đã có nhóm con hoặc thành viên hoạt động.
  - Không cho phép xóa/xóa mềm nhóm nếu vẫn còn nhóm con hoặc thành viên hoạt động.
- **Migration**: Sinh tự động bằng `dotnet ef migrations add AddCustomerGroup`.

## Ghi chú về Test & Cách ly vùng phiên
- File `OrderServiceTests.cs` (thuộc vùng Order của phiên khác) đã được hoàn tác (`git restore`) hoàn toàn để đảm bảo tuyệt đối không xâm phạm file ngoài phạm vi ADR 0001.
- Các test mới cho C1 & C2 được bổ sung độc lập trong `SupplierServiceTests.cs` và `CustomerGroupServiceTests.cs`.

