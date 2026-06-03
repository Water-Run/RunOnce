/*
 * 脚本执行管理
 * 提供临时脚本文件生成、终端执行及临时文件清理功能，支持命令行参数传递
 *
 * @author: WaterRun
 * @file: Static/Exec.cs
 * @date: 2026-04-09
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;

namespace RunOnce.Static;

/// <summary>一次性脚本执行相关的静态工具集合。</summary>
/// <remarks>
/// 命名空间职责：封装涉及临时脚本创建、命令解释器启动与残留清理的业务逻辑，暴露给 RunOnce 运行期使用。
/// </remarks>
public static class Exec
{
    /// <summary>语言标识符到脚本扩展名的不可变映射字典。</summary>
    /// <remarks>字典键为忽略大小写的语言名，值为包含前导点的扩展名，供 CreateTempFile 和外部查询使用。</remarks>
    private static readonly Dictionary<string, string> _languageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = ".bat",
        ["powershell"] = ".ps1",
        ["python"] = ".py",
        ["lua"] = ".lua",
        ["nim"] = ".nim",
        ["go"] = ".go",
    };

    /// <summary>根据语言标识符获取对应的脚本文件扩展名。</summary>
    /// <param name="language">语言标识符，必须是 <see cref="Config.SupportedLanguages"/> 中的有效值，不能为 null 或空白。</param>
    /// <returns>含前导点的文件扩展名。</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="language"/> 为 null 时。</exception>
    /// <exception cref="ArgumentException">当 <paramref name="language"/> 为空白或不在支持列表内时。</exception>
    public static string GetFileExtension(string language)
    {
        ArgumentNullException.ThrowIfNull(language);

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException(Text.Localize("语言标识符不能为空白字符串。"), nameof(language));
        }

        string normalizedLanguage = language.ToLowerInvariant();

        if (!_languageExtensions.TryGetValue(normalizedLanguage, out string? extension))
        {
            throw new ArgumentException(Text.Localize("不支持的语言标识符: {0}。", language), nameof(language));
        }

        return extension;
    }

    /// <summary>获取所有支持语言扩展名的副本，避免外部修改原始字典。</summary>
    /// <returns>语言到扩展名的字典，对应键大小写忽略。</returns>
    public static Dictionary<string, string> GetAllFileExtensions()
    {
        return new Dictionary<string, string>(_languageExtensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>生成临时脚本文件并在配置终端内执行，执行完成后依赖命令序列自清理。</summary>
    /// <param name="code">脚本内容，不能为空并且已由调用者完成编码与转义。</param>
    /// <param name="language">脚本语言标识符，必须在 <see cref="Config.SupportedLanguages"/> 中并且非空白。</param>
    /// <param name="workingDirectory">执行工作目录，不能为空白且必须存在。</param>
    /// <param name="arguments">传递给脚本的命令行参数，为 null 或空白时不传递参数。</param>
    /// <param name="asAdmin">是否以管理员身份执行，为 true 时根据 <see cref="Config.AdminMode"/> 决定提权方式。</param>
    /// <exception cref="ArgumentNullException">当 code、language 或 workingDirectory 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当任一参数不满足非空白或工作目录不存在时抛出。</exception>
    /// <exception cref="IOException">当临时脚本文件无法在目标目录创建时抛出。</exception>
    /// <exception cref="InvalidOperationException">当终端进程无法启动时抛出。</exception>
    /// <remarks>线程安全：方法无共享可变状态。副作用：在文件系统创建临时文件并启动外部终端进程。</remarks>
    public static void Execute(string code, string language, string workingDirectory, string? arguments = null, bool asAdmin = false)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(language);
        ArgumentNullException.ThrowIfNull(workingDirectory);

        if (string.IsNullOrEmpty(code))
        {
            throw new ArgumentException(Text.Localize("代码内容不能为空。"), nameof(code));
        }

        if (string.IsNullOrWhiteSpace(language))
        {
            throw new ArgumentException(Text.Localize("语言标识符不能为空白字符串。"), nameof(language));
        }

        if (string.IsNullOrWhiteSpace(workingDirectory))
        {
            throw new ArgumentException(Text.Localize("工作目录不能为空白字符串。"), nameof(workingDirectory));
        }

        if (!Directory.Exists(workingDirectory))
        {
            throw new ArgumentException(Text.Localize("工作目录不存在: {0}。", workingDirectory), nameof(workingDirectory));
        }

        string tempFileDirectory = Config.ScriptPlacement switch
        {
            ScriptPlacementBehavior.EnsureCompatibility => workingDirectory,
            _ => Path.GetTempPath(),
        };

        string tempFilePath = CreateTempFile(code, language, tempFileDirectory);
        string languageCommand = Config.GetLanguageCommand(language);
        string? sanitizedArgs = string.IsNullOrWhiteSpace(arguments) ? null : arguments;
        (string shellExe, string shellArgs) = BuildShellLaunchInfo(languageCommand, tempFilePath, sanitizedArgs);

        LaunchInTerminal(shellExe, shellArgs, workingDirectory, asAdmin);
    }

    /// <summary>清理系统临时目录中所有以配置前缀开头的残留临时脚本文件。</summary>
    /// <remarks>静默跳过被占用或无权限的文件，外部不依赖本方法的返回值，建议应用启动时调用。</remarks>
    public static void CleanupStaleTempFiles()
    {
        string tempDir = Path.GetTempPath();
        string prefix = Config.TempFilePrefix;
        IEnumerable<string> candidates = Directory.EnumerateFiles(tempDir, $"{prefix}*").Where(file => !string.IsNullOrEmpty(file));

        foreach (string file in candidates)
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // 不允许抛出，故吞掉
            }
        }
    }

    /// <summary>在指定目录下创建唯一命名的临时脚本文件。</summary>
    /// <param name="code">经过验证非空的脚本内容。</param>
    /// <param name="language">经过验证有效的语言标识符。</param>
    /// <param name="baseDirectory">目标目录路径，由调用者控制位置。</param>
    /// <returns>脚本文件的完整路径。</returns>
    /// <exception cref="IOException">在写入过程中遇到 IO 或权限异常。</exception>
    private static string CreateTempFile(string code, string language, string baseDirectory)
    {
        string extension = GetFileExtension(language);
        string uniqueSuffix = Guid.NewGuid().ToString("N")[..8];
        string fileName = Config.TempFilePrefix + uniqueSuffix + extension;
        string filePath = Path.Combine(baseDirectory, fileName);

        try
        {
            File.WriteAllText(filePath, code, Encoding.UTF8);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PathTooLongException)
        {
            throw new IOException(Text.Localize("无法创建临时文件: {0}。", filePath), ex);
        }

        return filePath;
    }

    /// <summary>构建终端与语言命令组合的 Shell 启动信息。</summary>
    /// <param name="languageCommand">语言命令（例如 python、cmd scripts）。</param>
    /// <param name="tempFilePath">要在终端内执行的脚本路径。</param>
    /// <param name="arguments">传递给脚本的命令行参数，为 null 时不传递。</param>
    /// <returns>Shell 可执行文件与启动参数组成的元组。</returns>
    private static (string ShellExe, string ShellArgs) BuildShellLaunchInfo(string languageCommand, string tempFilePath, string? arguments)
    {
        return Config.Shell switch
        {
            ShellType.PowerShell => BuildPowerShellLaunchInfo("powershell.exe", languageCommand, tempFilePath, false, arguments),
            ShellType.PowerShellUtf8 => BuildPowerShellLaunchInfo("powershell.exe", languageCommand, tempFilePath, true, arguments),
            ShellType.Pwsh => BuildPowerShellLaunchInfo("pwsh.exe", languageCommand, tempFilePath, false, arguments),
            ShellType.PwshUtf8 => BuildPowerShellLaunchInfo("pwsh.exe", languageCommand, tempFilePath, true, arguments),
            ShellType.Cmd => BuildCmdLaunchInfo(languageCommand, tempFilePath, false, arguments),
            ShellType.CmdUtf8 => BuildCmdLaunchInfo(languageCommand, tempFilePath, true, arguments),
            _ => BuildPowerShellLaunchInfo("powershell.exe", languageCommand, tempFilePath, false, arguments),
        };
    }

    /// <summary>为 PowerShell 系列 Shell 构建启动参数，使用 Base64 编码内容避免转义。</summary>
    /// <param name="exe">PowerShell 可执行程序名。</param>
    /// <param name="languageCommand">语言执行命令。</param>
    /// <param name="tempFilePath">临时脚本路径。</param>
    /// <param name="forceUtf8">是否在脚本内设置 UTF-8 编码。</param>
    /// <param name="arguments">传递给脚本的命令行参数，为 null 时不传递。</param>
    /// <returns>Shell 可执行文件名及带参数字符串。</returns>
    private static (string ShellExe, string ShellArgs) BuildPowerShellLaunchInfo(
        string exe,
        string languageCommand,
        string tempFilePath,
        bool forceUtf8,
        string? arguments)
    {
        string escapedPath = tempFilePath.Replace("'", "''");
        string exitPrompt = Text.Localize("按 Enter 键退出").Replace("'", "''");

        bool autoClose = Config.AutoCloseTerminalOnCompletion;

        StringBuilder scriptBuilder = new();

        if (forceUtf8)
        {
            scriptBuilder.Append("[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; ");
            scriptBuilder.Append("[Console]::InputEncoding = [System.Text.Encoding]::UTF8; ");
        }

        scriptBuilder.Append($"{languageCommand} '{escapedPath}'");
        if (!string.IsNullOrEmpty(arguments))
        {
            scriptBuilder.Append($" {arguments}");
        }
        scriptBuilder.Append("; ");

        scriptBuilder.Append("Write-Host ''; ");

        if (!autoClose)
        {
            scriptBuilder.Append($"Read-Host '{exitPrompt}'; ");
        }

        scriptBuilder.Append($"Remove-Item -LiteralPath '{escapedPath}' -Force -ErrorAction SilentlyContinue");

        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(scriptBuilder.ToString()));

        return (exe, $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encoded}");
    }

    /// <summary>为 CMD 系列 Shell 构建启动参数，支持可选 UTF-8 代码页。</summary>
    /// <param name="languageCommand">语言执行命令。</param>
    /// <param name="tempFilePath">临时脚本路径。</param>
    /// <param name="forceUtf8">是否在命令序列前设置 chcp 65001。</param>
    /// <param name="arguments">传递给脚本的命令行参数，为 null 时不传递。</param>
    /// <returns>Shell 可执行文件名与启动参数。</returns>
    private static (string ShellExe, string ShellArgs) BuildCmdLaunchInfo(string languageCommand, string tempFilePath, bool forceUtf8, string? arguments)
    {
        string quotedPath = $"\"{tempFilePath}\"";
        bool autoClose = Config.AutoCloseTerminalOnCompletion;

        StringBuilder commandBuilder = new();

        if (forceUtf8)
        {
            commandBuilder.Append("chcp 65001 >nul & ");
        }

        commandBuilder.Append($"{languageCommand} {quotedPath}");
        if (!string.IsNullOrEmpty(arguments))
        {
            commandBuilder.Append($" {arguments}");
        }
        commandBuilder.Append(" & echo. & ");

        if (!autoClose)
        {
            commandBuilder.Append("pause & ");
        }

        commandBuilder.Append($"del /f /q {quotedPath}");

        return ("cmd.exe", $"/c \"{commandBuilder}\"");
    }

    /// <summary>在配置终端中启动命令解释器。</summary>
    /// <param name="shellExe">Shell 可执行文件名或路径。</param>
    /// <param name="shellArgs">Shell 启动参数。</param>
    /// <param name="workingDirectory">终端工作目录。</param>
    /// <param name="asAdmin">是否以管理员身份启动。</param>
    /// <exception cref="InvalidOperationException">无法启动终端时抛出。</exception>
    private static void LaunchInTerminal(string shellExe, string shellArgs, string workingDirectory, bool asAdmin = false)
    {
        ProcessStartInfo startInfo = CreateWindowsTerminalStartInfo(shellExe, shellArgs, workingDirectory, asAdmin);

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(Text.Localize("无法启动终端进程。"), ex);
        }
    }

    /// <summary>构建 Windows Terminal 启动信息，支持管理员模式。</summary>
    /// <param name="shellExe">Shell 可执行文件名。</param>
    /// <param name="shellArgs">Shell 参数。</param>
    /// <param name="workingDirectory">目标工作目录。</param>
    /// <param name="asAdmin">是否以管理员身份启动。</param>
    /// <returns>配置完成的 <see cref="ProcessStartInfo"/>。</returns>
    private static ProcessStartInfo CreateWindowsTerminalStartInfo(string shellExe, string shellArgs, string workingDirectory, bool asAdmin)
    {
        ProcessStartInfo psi = new()
        {
            FileName = Config.WindowsTerminalExecutable,
            Arguments = $"-d \"{workingDirectory}\" {shellExe} {shellArgs}",
            UseShellExecute = true,
            CreateNoWindow = false,
        };

        if (asAdmin)
        {
            switch (Config.AdminMode)
            {
                case AdminRunMode.WindowsSudo:
                    psi.Arguments = $"-d \"{workingDirectory}\" sudo {shellExe} {shellArgs}";
                    break;
                case AdminRunMode.ElevatedTerminal:
                    psi.Verb = "runas";
                    break;
            }
        }

        return psi;
    }
}
