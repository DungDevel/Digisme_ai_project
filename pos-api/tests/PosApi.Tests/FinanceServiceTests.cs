using Microsoft.EntityFrameworkCore;
using PosApi.Data;
using PosApi.Domain;
using PosApi.Dto;
using PosApi.Services;
using Xunit;

namespace PosApi.Tests;

public class FinanceServiceTests
{
    private readonly DatabaseFixture _fixture = new();

    [Fact]
    public async Task CashVoucher_CreateAndConfirm_UpdatesFundLedger_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var voucherService = new CashVoucherService(context);
            var fundBookService = new CashFundBookService(context);

            // 1. Create Receipt Voucher (Phiếu thu 500.000đ)
            var receiptReq = new CashVoucherCreateRequest(
                CashVoucherType.Receipt,
                PartnerType.Other,
                null,
                500000m,
                PaymentMethod.Cash,
                "Manual",
                null,
                null,
                "Thu tiền mặt bán hàng ngoài"
            );

            var createdReceipt = await voucherService.CreateVoucherAsync(1, receiptReq);
            Assert.Equal(CashVoucherStatus.Draft, createdReceipt.Status);
            Assert.Equal(500000m, createdReceipt.Amount);

            // Confirm receipt
            var confirmedReceipt = await voucherService.ConfirmVoucherAsync(1, createdReceipt.Id);
            Assert.Equal(CashVoucherStatus.Confirmed, confirmedReceipt.Status);

            // Verify fund book summary
            var summary1 = await fundBookService.GetFundBookSummaryAsync(null, null);
            Assert.Equal(500000m, summary1.TotalReceipts);
            Assert.Equal(0m, summary1.TotalPayments);
            Assert.Equal(500000m, summary1.ClosingBalance);

            // 2. Create Payment Voucher (Phiếu chi 200.000đ)
            var paymentReq = new CashVoucherCreateRequest(
                CashVoucherType.Payment,
                PartnerType.Other,
                null,
                200000m,
                PaymentMethod.Cash,
                "Manual",
                null,
                null,
                "Chi mua văn phòng phẩm"
            );

            var createdPayment = await voucherService.CreateVoucherAsync(1, paymentReq);
            var confirmedPayment = await voucherService.ConfirmVoucherAsync(1, createdPayment.Id);
            Assert.Equal(CashVoucherStatus.Confirmed, confirmedPayment.Status);

            // Verify final fund book summary (500k in, 200k out -> 300k balance)
            var summary2 = await fundBookService.GetFundBookSummaryAsync(null, null);
            Assert.Equal(500000m, summary2.TotalReceipts);
            Assert.Equal(200000m, summary2.TotalPayments);
            Assert.Equal(300000m, summary2.ClosingBalance);
        }
    }

    [Fact]
    public async Task SupplierDebt_Pay_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var supplierService = new SupplierService(context);
            var debtService = new SupplierDebtService(context);

            var supplier = await supplierService.CreateSupplierAsync(1, new SupplierCreateRequest(
                "NCC02", "Công ty CP Thực Phẩm Miền Bắc", "0912345678", "Hà Nội", "987654321", null
            ));

            // Create supplier debt
            var supplierDebt = new SupplierDebt
            {
                SupplierId = supplier.Id,
                Amount = 1000000m,
                PaidAmount = 0m,
                Status = DebtStatus.Open,
                ReferenceType = "StockIn",
                ReferenceId = "PHNK-20261106-0001",
                Note = "Nợ nhập hàng"
            };
            context.SupplierDebts.Add(supplierDebt);
            await context.SaveChangesAsync();

            // Pay partial debt (400.000đ)
            var pay1 = await debtService.PaySupplierDebtAsync(1, supplierDebt.Id, 400000m, "Trả bớt tiền nợ NCC");
            Assert.Equal(1000000m, pay1.Amount);
            Assert.Equal(400000m, pay1.PaidAmount);
            Assert.Equal(600000m, pay1.RemainingAmount);
            Assert.Equal("PartiallyPaid", pay1.Status);

            // Pay remaining debt (600.000đ)
            var pay2 = await debtService.PaySupplierDebtAsync(1, supplierDebt.Id, 600000m, "Thanh toán hết nợ NCC");
            Assert.Equal(1000000m, pay2.Amount);
            Assert.Equal(1000000m, pay2.PaidAmount);
            Assert.Equal(0m, pay2.RemainingAmount);
            Assert.Equal("Settled", pay2.Status);
        }
    }

    [Fact]
    public async Task GeneralJournal_RetrievesEntries_Success()
    {
        var (context, tx) = _fixture.CreateContextWithTransaction();
        using (context)
        using (tx)
        {
            var journalService = new GeneralJournalService(context);
            var voucherService = new CashVoucherService(context);

            // Create and confirm a receipt voucher
            var receipt = await voucherService.CreateVoucherAsync(1, new CashVoucherCreateRequest(
                CashVoucherType.Receipt, PartnerType.Other, null, 150000m, PaymentMethod.Cash, "Manual", null, null, "Thu test nhật ký"
            ));
            await voucherService.ConfirmVoucherAsync(1, receipt.Id);

            var journal = await journalService.GetJournalEntriesAsync(null, null, 1, 10);
            Assert.NotNull(journal);
            Assert.True(journal.Total >= 1);
        }
    }
}
