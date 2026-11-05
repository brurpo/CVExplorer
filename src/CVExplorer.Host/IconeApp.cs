using System.Drawing;

namespace CVExplorer.Host;

/// <summary>
/// Logo do app embutido no assembly. O arquivo app.ico traz todos os tamanhos
/// (16 a 256), entao a barra de tarefas escolhe o que precisa para o DPI atual.
/// </summary>
internal static class IconeApp
{
    private const string Recurso = "CVExplorer.app.ico";
    private static Icon? _icone;

    public static Icon? Carregar()
    {
        if (_icone is not null)
        {
            return _icone;
        }

        var fluxo = typeof(IconeApp).Assembly.GetManifestResourceStream(Recurso);
        if (fluxo is null)
        {
            return null;
        }

        using (fluxo)
        {
            _icone = new Icon(fluxo);
        }

        return _icone;
    }
}
