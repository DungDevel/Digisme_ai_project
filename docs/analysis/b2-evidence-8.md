# Bằng chứng kiểm tra và hoàn tất B2 (Evidence 8)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `2f0fa4c`
- `git status --short`: (Sạch sẽ)

---

## 1. Số lượng Test (190 tests)
- Baseline `main`: 175 tests. Toàn bộ **190/190 test** chạy thành công (`Passed: 190, Failed: 0`) khi chạy trên bản sạch (TEMP). Các định danh unique trong test được sinh qua `Guid.NewGuid()` để tránh xung đột mã code/barcode lặp lại.

---

## 2. Test song song Hủy phiếu & Trả nợ (`ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment`)
- **Mã test (đã lược bỏ `catch { return false; }`, bắt chính xác `BusinessRuleException`):**
  ```csharp
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
      catch (BusinessRuleException)
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
      catch (BusinessRuleException)
      {
          return false;
      }
  });

  tcs.SetResult(true);
  var results = await Task.WhenAll(cancelTask, payTask);
  Assert.True((results[0] && !results[1]) || (!results[0] && results[1]));
  ```

---

## 3. Test xóa phiếu nhập khi có nợ (`DeleteStockIn_WithLinkedSupplierDebt_ThrowsException`)
- **Mã test:**
  ```csharp
  [Fact]
  public async Task DeleteStockIn_WithLinkedSupplierDebt_ThrowsException()
  {
      var context = _fixture.CreateRealContext();
      await using var tx = await context.Database.BeginTransactionAsync();
      var stockInService = new StockInService(context, new Mock<ILogger<StockInService>>().Object);

      var supplier = new Supplier { Code = "NCC-" + Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(), Name = "Supplier Del", IsActive = true };
      context.Suppliers.Add(supplier);
      var category = new Category { Name = "Cat " + Guid.NewGuid().ToString("N") };
      context.Categories.Add(category);
      await context.SaveChangesAsync();

      var product = new Product { Name = "Prod Del", Barcode = "B-" + Guid.NewGuid().ToString("N").Substring(0, 6), CategoryId = category.Id, CurrentStock = 10, IsActive = true };
      context.Products.Add(product);
      var invoice = new SupplierInvoice { Status = SupplierInvoiceStatus.PendingReview, CreatedBy = 1 };
      context.SupplierInvoices.Add(invoice);
      await context.SaveChangesAsync();
      await InventoryReconciler.ReconcileAsync(context);

      var req = new ConfirmStockInRequest(
          invoice.Id,
          new List<StockInItemRequest> { new(product.Id, 5, 1000m) },
          "Supplier Del",
          "Note",
          supplier.Id,
          PaidAmount: 0m
      );

      var res = await stockInService.ConfirmStockInAsync(1, req, Guid.NewGuid().ToString());
      var stockIn = await context.StockIns.FirstAsync(s => s.Code == res.Code);

      await Assert.ThrowsAsync<BusinessRuleException>(async () =>
      {
          await stockInService.DeleteStockInAsync(1, stockIn.Id);
      });

      await tx.RollbackAsync();
  }
  ```

---

## 4. Recalculate & Quy tắc `IsBackfilled`
- `StockInService.cs:739` sử dụng `IsBackfilled` để nhận diện các phiếu nhập lịch sử được backfill từ dữ liệu cũ, chặn thao tác hủy/sửa trực tiếp trên các chứng từ lịch sử đó.
- `Recalculate_ApplyMode_DoesNotChangeSupplierDebts` và `Recalculate_ApplyMode_IgnoresBackfilledOrPreB2StockIns` pass 100%.

---

## 5. Test Migration thật bằng `IMigrator`
- **Mã test:** `MigrationTest_IMigrator_VerifyOldDataAndRollback` thực hiện query riêng biệt `information_schema.columns` cho từng bảng (`SupplierDebts`: `duedate`, `stockinid`; `StockIns`: `duedate`, `paidamount`, `paymentmethod`) và `pg_indexes` cho `IX_SupplierDebts_StockInId_Unique`, đảm bảo Up/Down/Up lại chính xác.

---

## 6. Độ lệch so với prompt
1. Các báo cáo vòng trước có nhầm lẫn về số lượng test (187/189), thực tế hiện tại là 190 test pass hoàn toàn.
2. `b2-evidence-7.md` được commit khi đang hoàn thiện các test song song và xóa phiếu.
3. "Chưa DB nào áp Q14 bản cũ" là giả định của chủ dự án, không phải điều Claude Code đã tự xác minh trên môi trường ngoài.

---

## Tóm tắt (Tối đa 8 dòng)
- Nhánh `feature/b2-supplier-debt` hoàn tất 100% backend nợ NCC.
- Tự động sinh `SupplierDebt` và phiếu chi (`PaidAmount`, `DueDate`, `PaymentMethod`).
- Khóa dòng `ORDER BY "Id" FOR UPDATE` an toàn chống tranh chấp đồng thời.
- Bổ sung partial unique index (`WHERE "StockInId" IS NOT NULL`) và FK `Restrict`.
- Kiểm thử migration đầy đủ qua `IMigrator` và kiểm chứng Q14 snapshot drift.
- Toàn bộ **190 test** vượt qua thành công trên bản sạch (0 lỗi).
