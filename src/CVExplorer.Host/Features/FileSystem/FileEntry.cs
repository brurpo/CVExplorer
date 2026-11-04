using System.Globalization;

namespace CVExplorer.Host.Features.FileSystem;

public sealed class FileEntry
{
    public string Nome { get; set; } = string.Empty;

    public string Caminho { get; set; } = string.Empty;

    public bool Pasta { get; set; }

    public long Tamanho { get; set; }

    public DateTimeOffset Modificado { get; set; }

    public DateTimeOffset Criacao { get; set; }

    public string Ext { get; set; } = string.Empty;

    public bool Oculto { get; set; }
}

/// <summary>
/// Ordena nomes de forma natural: "Shot 2" vem antes de "Shot 10".
/// </summary>
public sealed class NaturalComparer : IComparer<string>
{
    public static readonly NaturalComparer Instance = new();

    private static readonly CompareInfo PtBr =
        CultureInfo.GetCultureInfo("pt-BR").CompareInfo;

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x is null) return -1;
        if (y is null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                var startX = i;
                var startY = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;

                var nx = x.AsSpan(startX, i - startX);
                var ny = y.AsSpan(startY, j - startY);
                while (nx.Length > 1 && nx[0] == '0') nx = nx[1..];
                while (ny.Length > 1 && ny[0] == '0') ny = ny[1..];

                if (nx.Length != ny.Length) return nx.Length.CompareTo(ny.Length);
                var numeric = nx.SequenceCompareTo(ny);
                if (numeric != 0) return numeric;
                continue;
            }

            var cmp = PtBr.Compare(x, i, 1, y, j, 1, CompareOptions.IgnoreCase);
            if (cmp != 0) return cmp;
            i++;
            j++;
        }

        return (x.Length - i).CompareTo(y.Length - j);
    }
}
