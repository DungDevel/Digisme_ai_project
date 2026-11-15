# Bằng chứng khắc phục Q26 & Q28 (Evidence Q26)

## HEAD Commit & Git Status
- `git rev-parse --short HEAD`: `c0b13bc`
- `git status --short`: (Sạch sẽ)

---

## 1. Điều tra hiện trạng (`OrderService`)
- **(a) Luồng tạo đơn trong `OrderService`:** Có ghi `StockLedgerEntry` (`OrderService.cs:554`), nhưng trước đó `WarehouseId` không được gán (`WarehouseId = null`), chỉ trừ `Product.CurrentStock`.
- **(b) Luồng hủy đơn (`CancelOrderAsync`):** `CancelOrderAsync` (`OrderService.cs:960`) trước đây chỉ hoàn trả `Product.CurrentStock += item.Qty`, hoàn toàn không hoàn lại `WarehouseStock`.
- **(c) Các luồng khác tác động `CurrentStock` mà không đụng `WarehouseStocks`:**
  - `UpdateOrderAsync` (sửa đơn nháp)
  - `ReturnService` (đã có xử lý `WarehouseStock`)
  - Đơn chuyển từ `Pending` sang `Completed` (xử lý khi tạo đơn `CreateOrderAsync`)

| Luồng nghiệp vụ | Ghi `StockLedger`? | Gán `WarehouseId`? | Cập nhật `WarehouseStocks`? |
| --- | --- | --- | --- |
| Tạo đơn (`CreateOrderAsync`) | Có (`OrderService.cs:554`) | Không (trước sửa) | Không (trước sửa) |
| Hủy đơn (`CancelOrderAsync`) | Không | Không | Không (trước sửa) |
| Sửa đơn (`UpdateOrderAsync`) | Không | Không | Không |

---

## 2. Tái hiện Q28 trước khi sửa
- Viết test `Q28_SellThenStockIn_Success_NoInvariantException` (bán rồi chốt phiếu nhập tiếp theo).
- Trước khi sửa `OrderService`, việc bán hàng không trừ `WarehouseStock` khiến `CurrentStock != SUM(WarehouseStock.Stock)` (lệch bất biến Q14), dẫn đến lệnh chốt phiếu nhập tiếp theo ném ngoại lệ `BusinessRuleException` về lệch bất biến tồn kho.

---

## 3. Giải pháp sửa tối thiểu trong `OrderService`
- **Tạo đơn (`CreateOrderAsync`):** Xác định kho mặc định (`IsDefault && IsActive` hoặc `IsActive`), khóa `WarehouseStocks` bằng `SELECT ... FROM "WarehouseStocks" WHERE "WarehouseId" = @whId AND "ProductId" = ANY(@ids) ORDER BY \"Id\" FOR UPDATE`, kiểm tra tồn kho tại kho đó, trừ `WarehouseStock.Stock`, ghi `StockLedgerEntry` kèm `WarehouseId` và `Balance` tồn kho thực tế. Áp dụng tương tự cho nguyên liệu BOM.
- **Hủy đơn (`CancelOrderAsync`):** Đọc `StockLedgerEntry` của đơn để xác định đúng kho đã xuất, hoàn trả chính xác vào `WarehouseStock` và ghi bút toán đảo (`LedgerRefType.Adjustment`).

---

## 4. Test hồi quy bổ sung (`OrderServiceTests.cs`)
1. `Q28_SellThenStockIn_Success_NoInvariantException`: Bán hàng rồi chốt phiếu nhập tiếp theo không lỗi, giữ vững bất biến `CurrentStock == SUM(WarehouseStock.Stock)`.
2. `CancelOrderAsync_RefundsWarehouseStockAndCurrentStock`: Hủy đơn Pending hoàn trả đầy đủ vào `CurrentStock` và `WarehouseStock`.

---

## 5. Thí nghiệm đột biến trên clone tạm
- Xóa dòng trừ `whStock.Stock` trong luồng bán (`OrderService.cs`), chạy test hồi quy 5 lần liên tiếp:
  - **Kết quả (5/5 lần FAIL):**
    ```
    PosApi.Services.BusinessRuleException : Sản phẩm '...' đang lệch bất biến tồn kho (CurrentStock: 7 != Tổng kho: 10.000). Vui lòng chạy công cụ cân bằng tồn kho (Q14) trước khi chốt phiếu nhập.
    ```
- Sau khi khôi phục, `git diff --stat` rỗng và test PASS.

---

## 6. Cập nhật tài liệu `group8-open-questions.md`
- Đã cập nhật **Q26** (Đã sửa).
- Đã cập nhật **Q28** (Đã sửa, có test hồi quy).
- Đã thêm **Q29** (*Xử lý dữ liệu đã bán trước bản sửa*).

---

## 7. Đầu ra thô thực tế (`dotnet test` & DriftCheck)
- **Chạy toàn bộ bộ test 3 lần liên tiếp (193/193 test PASS mỗi lần):**
  - Run 1: `Passed! - Failed: 0, Passed: 193, Total: 193`
  - Run 2: `Passed! - Failed: 0, Passed: 193, Total: 193`
  - Run 3: `Passed! - Failed: 0, Passed: 193, Total: 193`
- **DriftCheck trong clone tạm:** `Up` và `Down` hoàn toàn rỗng (`{ }`).

---

## 8. Git Log & Remote Verification
- `git log --oneline -3`:
  ```
  e3b45bc docs(pos): add q26-evidence.md for Q26/Q28 evidence submission
  c0b13bc fix(pos): resolve Q26 & Q28 by deducting WarehouseStock in OrderService.CreateOrderAsync and refunding in CancelOrderAsync
  5aba6a4 Merge pull request #10 from DungDevel/feature/b2-supplier-debt
  ```
- `git ls-remote origin fix/q26-sale-warehousestock`:
  ```
  e3b45bc6f78332924765bccdee9a1fefd5ffea24	refs/heads/fix/q26-sale-warehousestock
  ```
