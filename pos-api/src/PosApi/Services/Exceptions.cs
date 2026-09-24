namespace PosApi.Services;

/// <summary>
/// BE-16: business exception riêng cho lỗi nghiệp vụ, thay vì lạm dụng InvalidOperationException.
/// GlobalExceptionHandler map về HTTP 400 + { error: message }.
/// </summary>
public class BusinessRuleException : Exception
{
    public BusinessRuleException(string message) : base(message) { }
}

/// <summary>Tồn kho không đủ — vẫn là 400 nhưng message rõ ràng.</summary>
public class StockShortageException : BusinessRuleException
{
    public StockShortageException(string message) : base(message) { }
}
