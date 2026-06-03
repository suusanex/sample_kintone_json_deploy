using System.Diagnostics;
using KintoneOAuthTokenCli;

try
{
    // 既存の同一プロセスを強制終了（クリーンアップ失敗後の二重起動を防ぐ）
    KillExistingProcess();

    var options = OAuthCliOptions.Parse(args);
    var acquirer = new OAuthTokenAcquirer(new HttpClient());

    Console.Error.WriteLine("Starting OAuth authorization flow...");
    Console.Error.WriteLine($"Redirect URI: {options.RedirectUri}");

    var token = await acquirer.AcquireTokenAsync(options, CancellationToken.None);

    // 呼び出し元スクリプトが標準出力をトークン値として受け取れるよう、余計な情報は出さない
    Console.Out.WriteLine(token);
    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine("OAuth token acquisition failed.");
    Console.Error.WriteLine(ex.ToString());
    return 1;
}

/// <summary>
/// 既存の KintoneOAuthTokenCli プロセスがあれば強制終了する
/// （アプリケーション異常終了時に localhost リスナーが残る問題回避）
/// </summary>
static void KillExistingProcess()
{
    try
    {
        var currentProcess = Process.GetCurrentProcess();
        var processName = Path.GetFileNameWithoutExtension(currentProcess.ProcessName);
        var otherProcesses = Process.GetProcessesByName(processName)
            .Where(p => p.Id != currentProcess.Id)
            .ToList();

        foreach (var process in otherProcesses)
        {
            try
            {
                Console.Error.WriteLine($"[OAuthCli] Killing existing process (PID: {process.Id})...");
                process.Kill();
                process.WaitForExit(5000);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[OAuthCli] Warning: Failed to kill process {process.Id}: {ex.Message}");
            }
        }
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"[OAuthCli] Warning: Failed to check for existing processes: {ex.Message}");
    }
}
