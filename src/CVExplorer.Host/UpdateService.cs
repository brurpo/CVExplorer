using Velopack;
using Velopack.Exceptions;
using Velopack.Sources;

namespace CVExplorer.Host;

/// <summary>
/// Atualizacao automatica pelas releases do GitHub (Velopack).
/// A distribuicao e portatil: o zip extraido se atualiza sozinho, sem instalador.
/// Fora de um pacote Velopack (build de desenvolvimento) apenas informa que nao ha canal.
/// </summary>
public sealed class UpdateService
{
    private readonly SemaphoreSlim _trava = new(1, 1);
    private UpdateManager? _manager;
    private UpdateInfo? _pendente;

    /// <summary>Versao atual e se ela consegue se atualizar (pacote portatil ou instalado).</summary>
    public object Estado()
    {
        try
        {
            var gerenciador = Gerenciador();
            return new
            {
                versao = AppInfo.Versao,
                atualizavel = gerenciador.IsInstalled,
                portatil = gerenciador.IsPortable,
                pendente = gerenciador.UpdatePendingRestart is not null,
            };
        }
        catch (NotInstalledException)
        {
            return new { versao = AppInfo.Versao, atualizavel = false, portatil = false, pendente = false };
        }
    }

    public async Task<object> CheckAsync(bool baixar)
    {
        if (!AppInfo.RepositorioConfigurado)
        {
            return new { disponivel = false, motivo = "repositorio-nao-configurado" };
        }

        // Duas janelas podem pedir a verificacao ao mesmo tempo: a trava evita
        // baixar o mesmo pacote em paralelo.
        await _trava.WaitAsync().ConfigureAwait(false);
        try
        {
            var gerenciador = Gerenciador();
            if (!gerenciador.IsInstalled)
            {
                return new { disponivel = false, motivo = "nao-instalado", versao = AppInfo.Versao };
            }

            var update = await gerenciador.CheckForUpdatesAsync().ConfigureAwait(false);
            if (update is null)
            {
                return new { disponivel = false, motivo = "atualizado", versao = AppInfo.Versao };
            }

            _pendente = update;

            var versao = update.TargetFullRelease.Version.ToString();
            if (!baixar)
            {
                return new { disponivel = true, versao, atual = AppInfo.Versao, baixado = false };
            }

            await gerenciador.DownloadUpdatesAsync(update).ConfigureAwait(false);
            return new { disponivel = true, versao, atual = AppInfo.Versao, baixado = true };
        }
        catch (NotInstalledException)
        {
            return new { disponivel = false, motivo = "nao-instalado", versao = AppInfo.Versao };
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao verificar atualizacoes", ex);
            return new { disponivel = false, motivo = "erro", mensagem = ex.Message };
        }
        finally
        {
            _trava.Release();
        }
    }

    public async Task<object> ApplyAsync()
    {
        await _trava.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_manager is null || _pendente is null)
            {
                return new { ok = false, erro = "Nenhuma atualizacao pronta para aplicar." };
            }

            await _manager.DownloadUpdatesAsync(_pendente).ConfigureAwait(false);

            // Portatil: o Velopack extrai a versao nova ao lado e reinicia o app.
            _manager.ApplyUpdatesAndRestart(_pendente);
            return new { ok = true };
        }
        catch (Exception ex)
        {
            Log.Error("Falha ao aplicar atualizacao", ex);
            return new { ok = false, erro = ex.Message };
        }
        finally
        {
            _trava.Release();
        }
    }

    private UpdateManager Gerenciador() =>
        _manager ??= new UpdateManager(
            new GithubSource(AppInfo.RepoUrl, accessToken: null, prerelease: false));
}
