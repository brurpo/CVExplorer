using Velopack;

namespace CVExplorer.Host;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Precisa ser a primeira coisa a rodar: o Velopack usa esse ponto de entrada
        // para aplicar atualizacoes e hooks de instalacao.
        VelopackApp.Build().Run();

        // Uma instancia por vez: duas copias competindo pelo cache e pela config
        // causariam miniaturas e configuracoes inconsistentes. Janelas multiplas
        // acontecem dentro do mesmo processo.
        using var instancia = new Mutex(initiallyOwned: true, "CVExplorer.SingleInstance", out var primeira);
        if (!primeira)
        {
            Native.Win32.TrazerParaFrente(AppInfo.Nome);
            return;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Log.Error("ThreadException", e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            Log.Error("UnhandledException", e.ExceptionObject as Exception);

        var host = new AppHost();
        var contexto = new ApplicationContext();

        // Fecha o app quando a ultima janela for fechada.
        host.UltimaJanelaFechada += contexto.ExitThread;

        // Pasta opcional na linha de comando (usada pelo "abrir com").
        // Aceita tanto um caminho unico quanto um que tenha sido quebrado por espacos.
        var candidatos = new List<string>();
        var atual = string.Empty;
        foreach (var argumento in args)
        {
            if (argumento.StartsWith('-'))
            {
                continue;
            }

            atual = atual.Length == 0 ? argumento : $"{atual} {argumento}";
            candidatos.Add(atual);
        }

        var inicial = candidatos.LastOrDefault(Directory.Exists);
        host.AbrirJanela(inicial, primaria: true);

        Application.Run(contexto);
    }
}
