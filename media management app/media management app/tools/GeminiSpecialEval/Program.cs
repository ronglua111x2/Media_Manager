namespace GeminiSpecialEval;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        using var cancel = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancel.Cancel();
            Console.Error.WriteLine("Stopping after the current request…");
        };

        try
        {
            var options = Options.Parse(args);
            return options.Command switch
            {
                "list" => Commands.List(options),
                "plan" => Commands.Plan(options),
                "run" => await Commands.RunAsync(options, cancel.Token),
                "replay" => await Commands.ReplayAsync(options, cancel.Token),
                "selftest" => await Commands.SelfTestAsync(options, cancel.Token),
                "quota" => Commands.Quota(options),
                _ => Help()
            };
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine(ex.Message);
            Console.Error.WriteLine("Run without arguments for help.");
            return 2;
        }
    }

    private static int Help()
    {
        Console.WriteLine(Options.Usage);
        return 0;
    }
}
