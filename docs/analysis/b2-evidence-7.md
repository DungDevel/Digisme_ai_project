# Bằng chứng kiểm tra và hoàn tất B2 (Evidence 7)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `4cd132a`
- `git status --short`: (Sạch sẽ)

---

## 1. Số lượng Test (189 tests)
- Báo cáo trước ghi 187 do loại trừ test song song `Parallel_StockIn_RealCommit_20Iterations`. Với cấu hình `Maximum Pool Size=50;Pooling=true`, toàn bộ **189/189 test** chạy thành công (`Passed: 189, Failed: 0`).

---

## 2. Test song song Hủy phiếu & Trả nợ (`ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment`)
- **Mã test:**
  ```csharp
  [Fact]
  public async Task ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment()
  {
      using var context1 = _fixture.CreateRealContext();
      using var context2 = _fixture.CreateRealContext();
      var stockInService = new StockInService(context1, new Mock<ILogger<StockInService>>().Object);
      var debtService = new SupplierDebtService(context2);

      var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier CC", IsActive = true };
      context1.Suppliers.Add(supplier);
      var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
      context1.Categories.Add(category);
      await context1.SaveChangesAsync();

      var product = new Product { Name = "Prod CC", Barcode = "B-CC", CategoryId = category.Id, CurrentStock = 10, IsActive = true };
      context1.Products.Add(product);
      var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
      context1.SupplierInvoices.Add(invoice);
      await context1.SaveChangesAsync();
      await InventoryReconciler.ReconcileAsync(context1);

      var req = new ConfirmStockInRequest(
          invoice.Id,
          new List<StockInItemRequest> { new(product.Id, 5, 1000m) },
          "Supplier CC",
          "Note",
          supplier.Id,
          PaidAmount: 0m
      );

      var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
      var stockIn = await context1.StockIns.FirstAsync(s => s.Code == res.Code);
      var debt = await context1.SupplierDebts.FirstAsync(d => d.StockInId == stockIn.Id);
      var debtId = debt.Id;

      var tcs = new TaskCompletionSource<bool>();

      Task<bool> cancelTask = Task.Run(async () =>
      {
          await tcs.Task;
          using var ctx = _fixture.CreateRealContext();
          var svc = new StockInService(ctx, new Mock<ILogger<StockInService>>().Object);
          try
          {
              await svc.CancelStockInAsync(1, stockIn.Id);
              return true;
          }
          catch
          {
              return false;
          }
      });

      Task<bool> payTask = Task.Run(async () =>
      {
          await tcs.Task;
          using var ctx = _fixture.CreateRealContext();
          var svc = new SupplierDebtService(ctx);
          try
          {
              await svc.PaySupplierDebtAsync(1, debtId, 5000m, "Pay concurrent");
              return true;
          }
          catch
          {
              return false;
          }
      });

      tcs.SetResult(true);
      var results = await Task.WhenAll(cancelTask, payTask);

      bool cancelSuccess = results[0];
      bool paySuccess = results[1];
      Assert.True((cancelSuccess && !paySuccess) || (!cancelSuccess && paySuccess));
  }
  ```
- **Thí nghiệm bỏ khóa `FOR UPDATE` trong `CancelStockInAsync`:** Chạy 5 lần -> 5/5 lần FAIL do race condition. Khôi phục khóa -> PASS 100%.

---

## 3. Test tính lại & Unique Index (`SupplierDebt_DuplicateStockInId_ThrowsDbUpdateException`)
- Chèn hai `SupplierDebt` cùng `StockInId` trực tiếp ném `DbUpdateException` (chứng minh partial unique index hoạt động chính xác với filter `WHERE "StockInId" IS NOT NULL`).

---

## 4. Test Migration thật bằng `IMigrator`
- **Truy vấn kiểm tra `information_schema.columns` và `pg_indexes` trong test `MigrationTest_IMigrator_VerifyOldDataAndRollback`:**
  - `SupplierDebts`: `duedate`, `stockinid`
  - `StockIns`: `duedate`, `paidamount`, `paymentmethod`
  - `pg_indexes`: `IX_SupplierDebts_StockInId_Unique`
- Kết quả: Migrate từ mốc Q14 -> Dựng dữ liệu cũ -> Migrate lên mới nhất -> Assert -> Down -> Up lại thành công không lỗi.

---

## 5. Độ lệch mô hình và migration (`DriftCheck`)
- Cấu hình `AppDbContext.cs` sử dụng `DeleteBehavior.Restrict` và partial unique index `IX_SupplierDebts_StockInId_Unique`.
- Chạy `dotnet ef migrations add DriftCheck` sinh ra file migration rỗng (No-op), xác nhận mô hình và migration khớp nhau tuyệt đối.

---

## 6. Quy tắc "trước B2" & Q20
- Quy tắc kiểm tra phiếu nhập trong chế độ apply: `IsBackfilled = true` hoặc `CreatedAt < B2_MIGRATION_DATE`.
- Q20 ghi nhận rõ ràng tại `group8-open-questions.md` về khoảng trống chứng từ giữa B1 và B2.

---

## 7. Độ lệch so với prompt
1. Sửa migration Q14 để thêm `InventoryReconcileIssues` vào snapshot.
2. Thêm partial unique index cho `StockInId` trên `SupplierDebts` và đổi FK delete behavior sang `Restrict`.
3. Số lượng test thực tế trên bản sạch là 189 test methods.
4. Giả định "chưa DB nào áp Q14 bản cũ" là giả định của chủ dự án, không phải điều Claude Code đã tự xác minh trên môi trường ngoài.

---

## Tóm tắt (Tối đa 8 dòng)
- Nhánh `feature/b2-supplier-debt` hoàn tất 100% backend nợ NCC.
- Tự động sinh `SupplierDebt` và phiếu chi (`PaidAmount`, `DueDate`, `PaymentMethod`).
- Khóa dòng `ORDER BY "Id" FOR UPDATE` an toàn chống tranh chấp đồng thời.
- Bổ sung partial unique index (`WHERE "StockInId" IS NOT NULL`) và FK `Restrict`.
- Kiểm thử migration đầy đủ qua `IMigrator` và kiểm chứng Q14 snapshot drift.
- Toàn bộ **189 test** vượt qua thành công trên bản sạch (0 lỗi).
