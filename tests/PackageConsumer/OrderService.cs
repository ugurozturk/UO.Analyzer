using Microsoft.Extensions.Logging;

namespace PackageConsumer;

public partial class OrderService(ILogger<OrderService> logger)
{
    public void Process(int orderId) => LogProcessingOrder(logger, orderId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Processing order {OrderId}", SkipEnabledCheck = true)]
    private static partial void LogProcessingOrder(ILogger logger, int orderId);
}
