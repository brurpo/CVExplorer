<#
  Janela rápida para guardar o token do GitHub no cofre do Windows
  (Gerenciador de Credenciais), sem passar por arquivo nem por histórico de shell.

      .\scripts\token-github.ps1

  O token fica como credencial genérica "git:https://github.com", que é o nome que
  o Git Credential Manager usa — então o push também aproveita. O script de
  publicação lê essa mesma entrada.
#>
param(
    [string]$Alvo = "git:https://github.com",
    # Sem janela: grava (ou remove, com -Remover) direto. Útil para automação/teste.
    [string]$Token,
    [switch]$Remover,
    [switch]$SemJanela
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms, System.Drawing

if (-not ("CofreWin32" -as [type])) {
    Add-Type -Namespace "" -Name CofreWin32 -MemberDefinition @'
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct CREDENTIAL
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string TargetAlias;
        public string UserName;
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredWrite(ref CREDENTIAL cred, int flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    public static extern bool CredRead(string target, int type, int flags, out IntPtr cred);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll")]
    public static extern void CredFree(IntPtr cred);
'@
}

$constGenerico = 1
$persistirLocal = 2

function Salvar-Token([string]$alvo, [string]$usuario, [string]$token) {
    $bytes = [System.Text.Encoding]::Unicode.GetBytes($token)
    $ponteiro = [System.Runtime.InteropServices.Marshal]::AllocHGlobal($bytes.Length)
    try {
        [System.Runtime.InteropServices.Marshal]::Copy($bytes, 0, $ponteiro, $bytes.Length)

        $cred = New-Object CofreWin32+CREDENTIAL
        $cred.Type = $constGenerico
        $cred.TargetName = $alvo
        $cred.UserName = $usuario
        $cred.Comment = "Token do GitHub usado pelo CV Explorer"
        $cred.CredentialBlob = $ponteiro
        $cred.CredentialBlobSize = $bytes.Length
        $cred.Persist = $persistirLocal

        if (-not [CofreWin32]::CredWrite([ref]$cred, 0)) {
            throw "CredWrite falhou (código $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))"
        }
    }
    finally {
        [System.Runtime.InteropServices.Marshal]::ZeroFreeGlobalAllocUnicode($ponteiro)
    }
}

function Ler-Token([string]$alvo) {
    $ptr = [IntPtr]::Zero
    if (-not [CofreWin32]::CredRead($alvo, $constGenerico, 0, [ref]$ptr)) {
        return $null
    }

    try {
        $cred = [System.Runtime.InteropServices.Marshal]::PtrToStructure($ptr, [type]("CofreWin32+CREDENTIAL"))
        if ($cred.CredentialBlobSize -le 0) { return $null }
        return [System.Runtime.InteropServices.Marshal]::PtrToStringUni($cred.CredentialBlob, $cred.CredentialBlobSize / 2)
    }
    finally {
        [CofreWin32]::CredFree($ptr)
    }
}

function Remover-Token([string]$alvo) {
    [void][CofreWin32]::CredDelete($alvo, $constGenerico, 0)
}

# ---------- modo sem janela ----------

if ($Remover) {
    Remover-Token $Alvo
    Write-Host "Token removido do cofre." -ForegroundColor Yellow
    exit 0
}

if ($Token -and $SemJanela) {
    Salvar-Token $Alvo "x-access-token" $Token
    Write-Host "Token salvo no cofre do Windows ($Alvo)." -ForegroundColor Green
    exit 0
}

# ---------- janela ----------

$fundo = [System.Drawing.Color]::FromArgb(16, 17, 19)
$fundoCaixa = [System.Drawing.Color]::FromArgb(28, 30, 34)
$texto = [System.Drawing.Color]::FromArgb(230, 231, 233)
$textoFraco = [System.Drawing.Color]::FromArgb(138, 143, 152)
$acento = [System.Drawing.Color]::FromArgb(90, 169, 230)
$fonte = New-Object System.Drawing.Font("Segoe UI", 9.5)
$fonteTitulo = New-Object System.Drawing.Font("Segoe UI", 11, [System.Drawing.FontStyle]::Bold)

$form = New-Object System.Windows.Forms.Form
$form.Text = "CV Explorer — token do GitHub"
$form.ClientSize = New-Object System.Drawing.Size(470, 232)
$form.FormBorderStyle = [System.Windows.Forms.FormBorderStyle]::FixedSingle
$form.MaximizeBox = $false
$form.StartPosition = [System.Windows.Forms.FormStartPosition]::CenterScreen
$form.BackColor = $fundo
$form.ForeColor = $texto
$form.Font = $fonte

$titulo = New-Object System.Windows.Forms.Label
$titulo.Text = "Token do GitHub"
$titulo.Font = $fonteTitulo
$titulo.ForeColor = $texto
$titulo.Location = New-Object System.Drawing.Point(18, 14)
$titulo.AutoSize = $true
$form.Controls.Add($titulo)

$explicacao = New-Object System.Windows.Forms.Label
$explicacao.Text = "Guarda no Gerenciador de Credenciais do Windows (git:https://github.com).`nCrie em github.com/settings/tokens com escopo ""repo""."
$explicacao.ForeColor = $textoFraco
$explicacao.Location = New-Object System.Drawing.Point(18, 42)
$explicacao.AutoSize = $true
$form.Controls.Add($explicacao)

$caixa = New-Object System.Windows.Forms.TextBox
$caixa.Location = New-Object System.Drawing.Point(20, 88)
$caixa.Size = New-Object System.Drawing.Size(330, 26)
$caixa.UseSystemPasswordChar = $true
$caixa.BackColor = $fundoCaixa
$caixa.ForeColor = $texto
$caixa.BorderStyle = [System.Windows.Forms.BorderStyle]::FixedSingle
$caixa.Font = New-Object System.Drawing.Font("Consolas", 9.5)
$form.Controls.Add($caixa)

$botaoMostrar = New-Object System.Windows.Forms.Button
$botaoMostrar.Text = "mostrar"
$botaoMostrar.Location = New-Object System.Drawing.Point(358, 87)
$botaoMostrar.Size = New-Object System.Drawing.Size(92, 28)
$botaoMostrar.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
$botaoMostrar.FlatAppearance.BorderColor = [System.Drawing.Color]::FromArgb(46, 49, 56)
$botaoMostrar.BackColor = $fundoCaixa
$botaoMostrar.ForeColor = $textoFraco
$botaoMostrar.Add_Click({
    $caixa.UseSystemPasswordChar = -not $caixa.UseSystemPasswordChar
})
$form.Controls.Add($botaoMostrar)

$status = New-Object System.Windows.Forms.Label
$status.Location = New-Object System.Drawing.Point(20, 122)
$status.Size = New-Object System.Drawing.Size(430, 20)
$status.ForeColor = $textoFraco
$status.AutoEllipsis = $true
$form.Controls.Add($status)

function Novo-Botao([string]$rotulo, [int]$x, [int]$largura, [bool]$primario) {
    $b = New-Object System.Windows.Forms.Button
    $b.Text = $rotulo
    $b.Location = New-Object System.Drawing.Point($x, 156)
    $b.Size = New-Object System.Drawing.Size($largura, 32)
    $b.FlatStyle = [System.Windows.Forms.FlatStyle]::Flat
    $b.FlatAppearance.BorderColor = if ($primario) { $acento } else { [System.Drawing.Color]::FromArgb(46, 49, 56) }
    $b.BackColor = if ($primario) { [System.Drawing.Color]::FromArgb(24, 44, 62) } else { $fundoCaixa }
    $b.ForeColor = if ($primario) { $acento } else { $texto }
    return $b
}

$botaoTestar = Novo-Botao "Testar" 20 96 $false
$botaoSalvar = Novo-Botao "Salvar no cofre" 124 150 $true
$botaoRemover = Novo-Botao "Remover" 282 90 $false
$botaoFechar = Novo-Botao "Fechar" 380 70 $false

function Testar-Token([string]$valor) {
    if ([string]::IsNullOrWhiteSpace($valor)) { return $null }
    try {
        $r = Invoke-RestMethod "https://api.github.com/user" -Headers @{
            Authorization          = "Bearer $valor"
            Accept                 = "application/vnd.github+json"
            "User-Agent"           = "CVExplorer"
            "X-GitHub-Api-Version" = "2022-11-28"
        } -TimeoutSec 30
        return $r.login
    }
    catch {
        return $null
    }
}

$botaoTestar.Add_Click({
    $login = Testar-Token $caixa.Text.Trim()
    if ($login) {
        $status.ForeColor = [System.Drawing.Color]::FromArgb(76, 195, 138)
        $status.Text = "Token válido — conta: $login"
    }
    else {
        $status.ForeColor = [System.Drawing.Color]::FromArgb(229, 72, 77)
        $status.Text = "Token inválido ou sem rede."
    }
})

$botaoSalvar.Add_Click({
    $valor = $caixa.Text.Trim()
    if ([string]::IsNullOrWhiteSpace($valor)) {
        $status.ForeColor = [System.Drawing.Color]::FromArgb(229, 72, 77)
        $status.Text = "Cole o token antes de salvar."
        return
    }

    $login = Testar-Token $valor
    if (-not $login) {
        $resposta = [System.Windows.Forms.MessageBox]::Show(
            "O GitHub não reconheceu esse token. Salvar mesmo assim?",
            "CV Explorer",
            [System.Windows.Forms.MessageBoxButtons]::YesNo,
            [System.Windows.Forms.MessageBoxIcon]::Warning)
        if ($resposta -ne [System.Windows.Forms.DialogResult]::Yes) { return }
    }

    try {
        Salvar-Token $Alvo "x-access-token" $valor
        $caixa.Clear()
        $status.ForeColor = [System.Drawing.Color]::FromArgb(76, 195, 138)
        $status.Text = if ($login) { "Token salvo no cofre — conta: $login" } else { "Token salvo no cofre do Windows." }
    }
    catch {
        $status.ForeColor = [System.Drawing.Color]::FromArgb(229, 72, 77)
        $status.Text = "Falha ao salvar: $($_.Exception.Message)"
    }
})

$botaoRemover.Add_Click({
    Remover-Token $Alvo
    $caixa.Clear()
    $status.ForeColor = $textoFraco
    $status.Text = "Token removido do cofre."
})

$botaoFechar.Add_Click({ $form.Close() })

$form.Controls.AddRange(@($botaoTestar, $botaoSalvar, $botaoRemover, $botaoFechar))
$form.AcceptButton = $botaoSalvar
$form.CancelButton = $botaoFechar

$form.Add_Shown({
    if (Ler-Token $Alvo) {
        $status.Text = "Já existe um token salvo. Cole outro para substituir."
    }
    else {
        $status.Text = "Nenhum token salvo neste cofre."
    }
    $caixa.Focus()
})

[void]$form.ShowDialog()
