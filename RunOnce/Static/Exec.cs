/*
 * 脚本执行管理
 * 提供在指定目录生成临时脚本文件并通过终端执行的功能，执行完成后自动清理临时文件
 * 
 * @author: WaterRun
 * @file: Static/Exec.cs
 * @date: 2026-01-30
 */

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace RunOnce.Static;

/// <summary>
/// 脚本执行管理静态类，提供临时脚本文件生成与终端执行功能。
/// </summary>
/// <remarks>
/// 不变量：执行方法会在指定工作目录生成临时文件，并通过配置的终端执行脚本，执行命令包含清理指令以确保临时文件被删除。
/// 线程安全：所有公开方法为线程安全，内部字典为只读。
/// 副作用：会在文件系统创建临时文件，启动外部终端进程；调用后程序可能立即退出，清理由终端自行完成。
/// </remarks>
public static class Exec
{
    /// <summary>
    /// 语言标识符到文件扩展名的映射字典，键为小写语言标识符，值为包含点号的扩展名。
    /// </summary>
    private static readonly Dictionary<string, string> _languageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bat"] = ".bat",
        ["powershell"] = ".ps1",
        ["pwsh"] = ".ps1",
        ["python"] = ".py",
        ["lua"] = ".lua",
        ["nim"] = ".nim",
        ["php"] = ".php",
        ["javascript"] = ".js",
        ["typescript"] = ".ts",
        ["go"] = ".go",
        ["vbscript"] = ".vbs",
    };

    /// <summary>
    /// 获取指定语言对应的文件扩展名。
    /// </summary>
    /// <param name="language">脚本语言标识符，必须是 Config.SupportedLanguages 中定义的有效值，不区分大小写，不允许为 null 或空白字符串。</param>
    /// <returns>该语言对应的文件扩展名，包含前导点号（如 ".py"）。</returns>
    /// <exception cref="ArgumentNullException">当 language 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当 language 为空白字符串或不在支持列表中时抛出。</exception>
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

    /// <summary>
    /// 获取所有支持语言的文件扩展名映射副本。
    /// </summary>
    /// <returns>包含所有语言标识符及其对应文件扩展名的字典副本。</returns>
    /// <remarks>
    /// 返回的是映射数据的深拷贝，修改返回值不会影响内部存储。
    /// </remarks>
    public static Dictionary<string, string> GetAllFileExtensions()
    {
        return new Dictionary<string, string>(_languageExtensions, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 在指定工作目录生成临时脚本文件，启动终端执行脚本，执行完成后自动清理临时文件。
    /// </summary>
    /// <param name="code">要执行的脚本代码内容，不允许为 null 或空字符串。</param>
    /// <param name="language">脚本语言标识符，必须是 Config.SupportedLanguages 中定义的有效值，不区分大小写，不允许为 null 或空白字符串。</param>
    /// <param name="workingDirectory">脚本执行的工作目录路径，临时文件将在此目录生成，不允许为 null 或空白字符串，目录必须存在。</param>
    /// <exception cref="ArgumentNullException">当 code、language 或 workingDirectory 为 null 时抛出。</exception>
    /// <exception cref="ArgumentException">当参数为空白字符串、language 不在支持列表中、或工作目录不存在时抛出。</exception>
    /// <exception cref="IOException">当无法创建临时文件时抛出。</exception>
    /// <exception cref="InvalidOperationException">当无法启动终端进程时抛出。</exception>
    /// <remarks>
    /// 此方法为 fire-and-forget 模式，启动终端进程后立即返回。
    /// 临时文件的清理由终端命令自行完成，不依赖本程序的后续执行。
    /// 终端窗口执行完成后保持打开状态，允许用户查看输出结果。
    /// </remarks>
    public static void Execute(string code, string language, string workingDirectory)
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

        string tempFilePath = CreateTempFile(code, language, workingDirectory);
        string executeCommand = BuildExecuteCommand(language, tempFilePath);
        string cleanupCommand = BuildCleanupCommand(tempFilePath);
        string fullCommand = CombineCommands(executeCommand, cleanupCommand);

        StartTerminal(fullCommand, workingDirectory);
    }

    /// <summary>
    /// 在指定工作目录创建临时脚本文件。
    /// </summary>
    /// <param name="code">脚本代码内容，已验证非空。</param>
    /// <param name="language">脚本语言标识符，已验证有效。</param>
    /// <param name="workingDirectory">工作目录路径，已验证存在。</param>
    /// <returns>创建的临时文件的完整路径。</returns>
    /// <exception cref="IOException">当文件创建失败时抛出。</exception>
    private static string CreateTempFile(string code, string language, string workingDirectory)
    {
        string extension = GetFileExtension(language);
        string fileName = Config.TempFilePrefix + extension;
        string filePath = Path.Combine(workingDirectory, fileName);

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

    /// <summary>
    /// 构建执行脚本的命令字符串。
    /// </summary>
    /// <param name="language">脚本语言标识符，已验证有效。</param>
    /// <param name="tempFilePath">临时脚本文件的完整路径。</param>
    /// <returns>用于执行脚本的命令字符串。</returns>
    private static string BuildExecuteCommand(string language, string tempFilePath)
    {
        string languageCommand = Config.GetLanguageCommand(language);
        string quotedFilePath = $"\"{tempFilePath}\"";

        return $"{languageCommand} {quotedFilePath}";
    }

    /// <summary>
    /// 构建清理临时文件的命令字符串。
    /// </summary>
    /// <param name="tempFilePath">要删除的临时文件的完整路径。</param>
    /// <returns>用于删除临时文件的命令字符串。</returns>
    private static string BuildCleanupCommand(string tempFilePath)
    {
        string quotedFilePath = $"\"{tempFilePath}\"";
        return $"del /f /q {quotedFilePath}";
    }

    /// <summary>
    /// 组合执行命令和清理命令，生成完整的终端命令。
    /// </summary>
    /// <param name="executeCommand">执行脚本的命令。</param>
    /// <param name="cleanupCommand">清理临时文件的命令。</param>
    /// <returns>组合后的完整命令字符串，包含暂停提示。</returns>
    /// <remarks>
    /// 命令执行顺序：执行脚本 -> 显示暂停提示等待用户确认 -> 删除临时文件。
    /// 使用 cmd 的 pause 命令让用户有机会查看脚本输出。
    /// </remarks>
    private static string CombineCommands(string executeCommand, string cleanupCommand)
    {
        return $"{executeCommand} & pause & {cleanupCommand}";
    }

    /// <summary>
    /// 根据配置的终端类型启动终端进程执行命令。
    /// </summary>
    /// <param name="command">要在终端中执行的完整命令。</param>
    /// <param name="workingDirectory">终端的工作目录。</param>
    /// <exception cref="InvalidOperationException">当终端进程启动失败时抛出。</exception>
    private static void StartTerminal(string command, string workingDirectory)
    {
        ProcessStartInfo startInfo = Config.Terminal switch
        {
            TerminalType.WindowsTerminal => CreateWindowsTerminalStartInfo(command, workingDirectory),
            TerminalType.Cmd => CreateCmdStartInfo(command, workingDirectory),
            _ => CreateWindowsTerminalStartInfo(command, workingDirectory)
        };

        try
        {
            Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or FileNotFoundException)
        {
            throw new InvalidOperationException(Text.Localize("无法启动终端进程。"), ex);
        }
    }

    /// <summary>
    /// 创建 Windows Terminal 的进程启动信息。
    /// </summary>
    /// <param name="command">要执行的命令。</param>
    /// <param name="workingDirectory">工作目录路径。</param>
    /// <returns>配置完成的 ProcessStartInfo 实例。</returns>
    private static ProcessStartInfo CreateWindowsTerminalStartInfo(string command, string workingDirectory)
    {
        string escapedCommand = command.Replace("\"", "\\\"");

        return new ProcessStartInfo
        {
            FileName = Config.WindowsTerminalExecutable,
            Arguments = $"-d \"{workingDirectory}\" cmd /k \"{escapedCommand}\"",
            UseShellExecute = true,
            CreateNoWindow = false,
        };
    }

    /// <summary>
    /// 创建命令提示符的进程启动信息。
    /// </summary>
    /// <param name="command">要执行的命令。</param>
    /// <param name="workingDirectory">工作目录路径。</param>
    /// <returns>配置完成的 ProcessStartInfo 实例。</returns>
    private static ProcessStartInfo CreateCmdStartInfo(string command, string workingDirectory)
    {
        return new ProcessStartInfo
        {
            FileName = Config.CmdExecutable,
            Arguments = $"/k \"{command}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            CreateNoWindow = false,
        };
    }
}