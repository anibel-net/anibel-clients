using Velopack;
namespace Anibel.App;
public static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // Hooks must run before WinUI, the core, or user data are opened.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        XamlGeneratedProgram.XamlGeneratedMain();
    }
}
