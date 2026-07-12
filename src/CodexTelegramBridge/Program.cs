using CodexTelegramCommon;

namespace CodexTelegramBridge;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        if (args.Length == 0)
        {
            return 2;
        }

        var layout = InstallationLayout.FromExecutableBase(AppContext.BaseDirectory);
        return args[0] switch
        {
            "hook" when args.Length == 2 => HookHandler.CreateProduction(Environment.ProcessPath!).Handle(layout, args[1]),
            "worker" when args.Length == 1 => await new WorkerRunner(
                    WorkerEngine.CreateProduction(layout),
                    new RuntimeConfigStore(layout.RuntimeConfigPath),
                    () => DateTimeOffset.UtcNow)
                .RunAsync(CancellationToken.None)
                .ConfigureAwait(false),
            "repair-check" when args.Length == 1 => RepairService.CreateProduction().Run(layout).Outcome switch
            {
                RepairOutcome.Healthy or RepairOutcome.Repaired or RepairOutcome.Pending => 0,
                _ => 3,
            },
            _ => 2,
        };
    }
}
