namespace GameNest.Overlay;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var pipeIndex = Array.FindIndex(args, static argument => argument.Equals("--pipe", StringComparison.Ordinal));
        if (pipeIndex < 0 || pipeIndex + 1 >= args.Length || string.IsNullOrWhiteSpace(args[pipeIndex + 1]))
        {
            return 2;
        }

        try
        {
            using var host = new OverlayHost(args[pipeIndex + 1]);
            return host.Run();
        }
        catch
        {
            return 1;
        }
    }
}
