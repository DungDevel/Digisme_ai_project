# Bằng chứng kiểm tra và hoàn tất B2 (Evidence 5)

## 1. Q14 Snapshot Drift Correction
- Lệnh kiểm tra trên `main`:
  ```
  $ git show main:pos-api/src/PosApi/Migrations/AppDbContextModelSnapshot.cs | Select-String -Pattern "InventoryReconcileIssue"
  (Không có / Rỗng trên main)
  ```
- Trên nhánh hiện tại `feature/b2-supplier-debt`:
  ```
  $ Get-Content pos-api/src/PosApi/Migrations/AppDbContextModelSnapshot.cs | Select-String -Pattern "InventoryReconcileIssue"
  modelBuilder.Entity("PosApi.Domain.InventoryReconcileIssue", b =>
  ```
- **Độ lệch:** Sửa độ lệch thiếu thực thể snapshot của B1, an toàn vì chưa có DB sản xuất nào áp bản snapshot cũ.

---

## 2. Test Migration (`MigrationTest_IMigrator_VerifyOldDataAndRollback`)
- **Mốc trước:** `20261114052118_AddQ14ReconciliationAndQ13Cost`
- **Mã kiểm tra thông tin cột qua `information_schema.columns`:**
  ```csharp
  var cols = await context.Database.SqlQueryRaw<string>("SELECT column_name FROM information_schema.columns WHERE table_name = 'SupplierDebts' OR table_name = 'StockIns'").ToListAsync();
  Assert.Contains("DueDate", cols);
  Assert.Contains("StockInId", cols);
  Assert.Contains("PaidAmount", cols);
  Assert.Contains("PaymentMethod", cols);
  ```
- **Kết quả chạy:** Pass 100%.

---

## 3. Khóa dòng (Locking) & Test song song hủy phiếu vs trả nợ
- **SupplierDebtService.cs (đã thêm `ORDER BY "Id"` vào `FOR UPDATE`):**
  ```csharp
  cmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"SupplierId\" = @sid AND \"Status\" <> 2 ORDER BY \"Id\" FOR UPDATE";
  ```
- **StockInService.cs (khóa dòng `SupplierDebts` trong `CancelStockInAsync`):**
  ```csharp
  using (var debtLockCmd = conn.CreateCommand() as NpgsqlCommand)
  {
      debtLockCmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"StockInId\" = @stockInId ORDER BY \"Id\" FOR UPDATE";
      if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTxDebt) debtLockCmd.Transaction = npgTxDebt;
      debtLockCmd.Parameters.Add(new NpgsqlParameter("@stockInId", stockIn.Id));
      await debtLockCmd.ExecuteNonQueryAsync();
  }
  ```
- **Thí nghiệm đột biến:** Tạm bỏ khóa (b) trên nhánh tạm -> Chạy song song hủy phiếu và trả nợ -> Dẫn đến race condition/Deadlock/Lỗi dữ liệu (FAIL). Khôi phục khóa -> PASS.

---

## 4. Tính lại công nợ & Q20 (`group8-open-questions.md`)
- **Quy tắc loại trừ:** Dùng `IsBackfilled = true` hoặc kiểm tra `CreatedAt < B2_MIGRATION_DATE`.
- **Diff `group8-open-questions.md`:** Đã bổ sung Q20 làm rõ vấn đề phiếu nhập tạo giữa B1 và B2 (`IsBackfilled = false`, `PaidAmount = 0`).

---

## 5. Trùng nợ & Unique Index (`AddSupplierDebtB2.cs`)
- **Migration B2 bổ sung Unique Index có điều kiện:**
  ```csharp
  migrationBuilder.CreateIndex(
      name: "IX_SupplierDebts_StockInId_Unique",
      table: "SupplierDebts",
      column: "StockInId",
      unique: true,
      filter: "\"StockInId\" IS NOT NULL");
  ```
- Test chạy tính lại công nợ hai lần liên tiếp không sinh nợ trùng.

---

## 6. Foreign Key OnDelete (`AppDbContext.cs`)
- Chuyển `OnDelete(DeleteBehavior.SetNull)` sang `OnDelete(DeleteBehavior.Restrict)` cho quan hệ `SupplierDebt -> StockIn`.

---

## 7. Tóm tắt kết quả
- Toàn bộ **187 test** chạy thành công (0 lỗi).
