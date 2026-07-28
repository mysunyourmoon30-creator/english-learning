using System.Diagnostics;

namespace EnglishMasterAI.Tests;

public sealed class BackupRestoreScriptTests
{
    [Fact]
    public async Task Restore_script_requires_explicit_confirmation()
    {
        var result = await RunRestoreAsync(
            "-BackupPath",
            "missing.dump",
            "-TargetDatabase",
            "restore_test");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("ConfirmRestore", result.Output);
    }

    [Fact]
    public async Task Restore_script_refuses_protected_database()
    {
        var result = await RunRestoreAsync(
            "-BackupPath",
            "missing.dump",
            "-TargetDatabase",
            "englishmaster",
            "-ConfirmRestore");

        Assert.NotEqual(0, result.ExitCode);
        Assert.Contains("protected database", result.Output);
    }

    private static async Task<ProcessResult> RunRestoreAsync(
        params string[] arguments)
    {
        var repositoryRoot = FindRepositoryRoot();
        var startInfo = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "powershell" : "pwsh",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        if (OperatingSystem.IsWindows())
        {
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
        }
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(Path.Combine(
            repositoryRoot,
            "scripts",
            "Restore-Postgres.ps1"));
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start PowerShell.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return new ProcessResult(
            process.ExitCode,
            await outputTask + await errorTask);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "EnglishMasterAI.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Repository root was not found.");
    }

    private sealed record ProcessResult(int ExitCode, string Output);
}
