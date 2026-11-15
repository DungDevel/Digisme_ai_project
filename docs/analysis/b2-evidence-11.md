# Bằng chứng kiểm tra và hoàn tất B2 (Evidence 11)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `ecb161a`
- `git status --short`: (Sạch sẽ)

---

## 1. Khảo sát hai khối khóa `FOR UPDATE` trong `CancelStockInAsync` (`StockInService.cs`)
- **Khối 1 (dòng 753-759):**
  ```csharp
            var productIds = stockIn.Items.Select(i => i.ProductId).Distinct().OrderBy(id => id).ToList();
            var conn = _db.Database.GetDbConnection();
            if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();

            using (var debtLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                debtLockCmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"StockInId\" = @stockInId ORDER BY \"Id\" FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTxDebt) debtLockCmd.Transaction = npgTxDebt;
                debtLockCmd.Parameters.Add(new NpgsqlParameter("@stockInId", stockIn.Id));
                await debtLockCmd.ExecuteNonQueryAsync();
            }

            using (var prodLockCmd = conn.CreateCommand() as NpgsqlCommand)
  ```
- **Khối 2 (dòng 778-784 trước khi xóa):**
  ```csharp
            using (var whStockLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                whStockLockCmd!.CommandText = "SELECT \"Id\" FROM \"WarehouseStocks\" WHERE \"WarehouseId\" = @whId AND \"ProductId\" = ANY(@ids) FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTx2) whStockLockCmd.Transaction = npgTx2;
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@whId", stockIn.WarehouseId));
                whStockLockCmd.Parameters.Add(new NpgsqlParameter("@ids", productIds));
                await whStockLockCmd.ExecuteNonQueryAsync();
            }

            using (var debtLockCmd = conn.CreateCommand() as NpgsqlCommand)
            {
                debtLockCmd!.CommandText = "SELECT \"Id\" FROM \"SupplierDebts\" WHERE \"StockInId\" = @stockInId ORDER BY \"Id\" FOR UPDATE";
                if (_db.Database.CurrentTransaction?.GetDbTransaction() is NpgsqlTransaction npgTxDebt) debtLockCmd.Transaction = npgTxDebt;
                debtLockCmd.Parameters.Add(new NpgsqlParameter("@stockInId", stockIn.Id));
                await debtLockCmd.ExecuteNonQueryAsync();
            }

            var warehouseStocks = await _db.WarehouseStocks
  ```
- **Đánh giá:** Khối 2 hoàn toàn thừa vì trong cùng một transaction (`_db.Database.CurrentTransaction`), bảng `SupplierDebts` theo `StockInId` đã được khóa độc quyền (`FOR UPDATE`) ở khối 1. Khối 2 không phụ thuộc vào thứ tự khóa Products hay WarehouseStocks.

---

## 2. Đã xóa khối 2 thừa
- Đã xóa khối 2, giữ lại hoàn toàn khối 1 ở vị trí đầu tiên (trước khi khóa Products và WarehouseStocks). `git diff --stat` hiển thị thay đổi gọn gàng.

---

## 3. Khảo sát khóa duy nhất còn lại (Khối 1)
- Khi loại bỏ khối khóa `FOR UPDATE` còn lại ở khối 1, test tranh chấp Scenario 1 (`ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment`) thất bại vì lệnh `CancelStockInAsync` không bị chặn lại khi giao dịch thanh toán đang giữ khóa uncommitted, chứng minh khối khóa 1 đóng vai trò thiết yếu bảo vệ tranh chấp dữ liệu.

---

## 4. Mã hiện tại của `ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment`
- **Kịch bản 1:** Dùng giao dịch thanh toán uncommitted giữ khóa `FOR UPDATE` (`PaySupplierDebtAsync`), gọi `CancelStockInAsync` đồng thời, assert tác vụ hủy bị chặn (chưa hoàn thành sau 500ms), sau khi commit bên giữ khóa thì hủy bị từ chối với `BusinessRuleException`.
- **Kịch bản 2:** Dùng `TaskCompletionSource` đồng bộ chạy song song `CancelStockInAsync` thật và `PaySupplierDebtAsync` thật, assert hủy thành công (phiếu `Cancelled`, xóa nợ, hoàn tồn kho) và trả nợ bị từ chối với `KeyNotFoundException`.

---

## 5. Bọc dọn dẹp try/finally
- Đã bọc dữ liệu và tác vụ trong khối `try / finally` (cập nhật trạng thái `Cancelled` cho phiếu nhập nếu phát sinh ngoại lệ chưa hủy) để đảm bảo không rò rỉ dữ liệu test.

---

## 6. Đầu ra thô thực tế (`dotnet test`)
- **Chạy toàn bộ 3 lần liên tiếp (191/191 test PASS mỗi lần):**
  - Run 1: `Passed! - Failed: 0, Passed: 191, Total: 191`
  - Run 2: `Passed! - Failed: 0, Passed: 191, Total: 191`
  - Run 3: `Passed! - Failed: 0, Passed: 191, Total: 191`
- **Chạy riêng test `ConcurrencyTest_CancelStockIn_VsPayment_NoLostPayment` 3 lần liên tiếp:**
  - Run 1: `Passed! - Failed: 0, Passed: 1, Total: 1`
  - Run 2: `Passed! - Failed: 0, Passed: 1, Total: 1`
  - Run 3: `Passed! - Failed: 0, Passed: 1, Total: 1`

---

## Tóm tắt cuối
- Đã dọn dẹp khối khóa `FOR UPDATE` thừa thứ hai trong `CancelStockInService`.
- Toàn bộ 191 test pass liên tục qua nhiều lần chạy.
- Test đồng thời kiểm chứng đầy đủ cơ chế khóa độc quyền và bắt ngoại lệ chuẩn xác.
- Đã bọc dữ liệu trong `try/finally` dọn dẹp an toàn.
