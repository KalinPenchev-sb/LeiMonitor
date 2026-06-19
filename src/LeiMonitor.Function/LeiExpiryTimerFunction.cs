using LeiMonitor.Core.Services;
using Microsoft.Azure.Functions.Worker;

namespace LeiMonitor.Function;

public class LeiExpiryTimerFunction
{
    private readonly LeiExpiryChecker _checker;

    public LeiExpiryTimerFunction(LeiExpiryChecker checker)
    {
        _checker = checker;
    }

    [Function("LeiExpiryCheck")]
    public async Task Run(
        [TimerTrigger("0 0 8 * * *")] TimerInfo timer,
        FunctionContext context,
        CancellationToken ct)
    {
        await _checker.RunAsync(ct);
    }
}
