/*
 * RunOnce Shell Extension
 * 实现 Windows 11 一级右键菜单项，在文件夹空白处显示 "在此运行代码" / "Run Code Here"
 * 通过 IExplorerCommand COM 接口向文件资源管理器注册上下文菜单命令
 *
 * 工作原理：
 *   1. MSIX 安装时，系统读取 Package.appxmanifest 中的声明，将本 DLL 注册为 COM 服务器
 *   2. 用户在文件夹空白处右键时，Explorer 加载本 DLL 并创建 RunOnceCommand 实例
 *   3. Explorer 调用 GetTitle/GetIcon 获取菜单显示信息
 *   4. 用户点击菜单项后，Explorer 调用 Invoke，本 DLL 启动 RunOnce.exe 并传入文件夹路径
 *   5. MSIX 卸载时，系统自动撤销所有注册
 *
 * @author: WaterRun
 * @file: dllmain.cpp
 * @date: 2026-03-14
 */

#include "pch.h"

using namespace Microsoft::WRL;

// ============================================================================
// 全局状态
// ============================================================================

/// DLL 模块句柄，在 DllMain 中获取。
/// 用于定位 DLL 自身所在目录，进而找到同目录下的 RunOnce.exe 和图标文件。
static HMODULE g_hModule = nullptr;

// ============================================================================
// RunOnceCommand：IExplorerCommand 接口的完整实现
// ============================================================================

class __declspec(uuid("D4E5F601-A2B3-4C5D-8E9F-0A1B2C3D4E5F"))
RunOnceCommand : public RuntimeClass<
                     RuntimeClassFlags<ClassicCom>,
                     IExplorerCommand>
{
public:
    // ------------------------------------------------------------------------
    // GetTitle：返回菜单项显示的文字
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetTitle(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _Outptr_ LPWSTR *ppszName) override
    {
        WCHAR localeName[LOCALE_NAME_MAX_LENGTH]{};
        GetUserDefaultLocaleName(localeName, LOCALE_NAME_MAX_LENGTH);

        const bool isChinese = (wcsncmp(localeName, L"zh", 2) == 0);

        return SHStrDupW(
            isChinese ? L"\x5728\x6B64\x8FD0\x884C\x4EE3\x7801" : L"Run Code Here",
            ppszName);
    }

    // ------------------------------------------------------------------------
    // GetIcon：返回菜单项图标的文件路径
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetIcon(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _Outptr_ LPWSTR *ppszIcon) override
    {
        WCHAR dllPath[MAX_PATH]{};

        if (!GetModuleFileNameW(g_hModule, dllPath, MAX_PATH))
        {
            *ppszIcon = nullptr;
            return E_FAIL;
        }

        PathRemoveFileSpecW(dllPath);
        PathAppendW(dllPath, L"Assets\\logo.ico");

        return SHStrDupW(dllPath, ppszIcon);
    }

    // ------------------------------------------------------------------------
    // GetToolTip：返回鼠标悬停时的提示文字（不需要）
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetToolTip(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _Outptr_ LPWSTR *ppszInfotip) override
    {
        *ppszInfotip = nullptr;
        return E_NOTIMPL;
    }

    // ------------------------------------------------------------------------
    // GetCanonicalName：返回命令的唯一标识 GUID
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetCanonicalName(_Out_ GUID *pguidCommandName) override
    {
        *pguidCommandName = __uuidof(RunOnceCommand);
        return S_OK;
    }

    // ------------------------------------------------------------------------
    // GetState：控制菜单项的可见性和启用状态
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetState(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _In_ BOOL /* fOkToBeSlow */,
        _Out_ EXPCMDSTATE *pCmdState) override
    {
        *pCmdState = ECS_ENABLED;
        return S_OK;
    }

    // ------------------------------------------------------------------------
    // GetFlags：返回命令的行为标志
    // ------------------------------------------------------------------------
    IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS *pFlags) override
    {
        *pFlags = ECF_DEFAULT;
        return S_OK;
    }

    // ------------------------------------------------------------------------
    // EnumSubCommands：枚举子菜单命令（本命令无子菜单）
    // ------------------------------------------------------------------------
    IFACEMETHODIMP EnumSubCommands(_Outptr_ IEnumExplorerCommand **ppEnum) override
    {
        *ppEnum = nullptr;
        return E_NOTIMPL;
    }

    // ------------------------------------------------------------------------
    // Invoke：用户点击菜单项后执行的核心逻辑
    // ------------------------------------------------------------------------
    IFACEMETHODIMP Invoke(
        _In_opt_ IShellItemArray *psiItemArray,
        _In_opt_ IBindCtx * /* pbc */) override
    {
        // ── 第 1 步：从 Shell 参数中提取文件夹路径 ──

        WCHAR folderPath[MAX_PATH]{};

        if (psiItemArray)
        {
            DWORD itemCount = 0;
            HRESULT hr = psiItemArray->GetCount(&itemCount);

            if (SUCCEEDED(hr) && itemCount > 0)
            {
                ComPtr<IShellItem> shellItem;
                hr = psiItemArray->GetItemAt(0, &shellItem);

                if (SUCCEEDED(hr))
                {
                    PWSTR rawPath = nullptr;
                    hr = shellItem->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);

                    if (SUCCEEDED(hr) && rawPath)
                    {
                        StringCchCopyW(folderPath, MAX_PATH, rawPath);
                        CoTaskMemFree(rawPath);
                    }
                }
            }
        }

        // 无法获取文件夹路径：返回成功并不执行（与原行为一致）
        if (folderPath[0] == L'\0')
        {
            return S_OK;
        }

        // ── 第 2 步：构建 RunOnce.exe 的完整路径 ──

        WCHAR exePath[MAX_PATH]{};

        if (!GetModuleFileNameW(g_hModule, exePath, MAX_PATH))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        PathRemoveFileSpecW(exePath);
        PathAppendW(exePath, L"RunOnce.exe");

        // 明确检查目标 EXE 是否存在，避免“转一下没反应”
        if (!PathFileExistsW(exePath))
        {
            return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
        }

        // ── 第 3 步：构建命令行参数 ──

        WCHAR arguments[MAX_PATH + 8]{};
        HRESULT formatHr = StringCchPrintfW(arguments, ARRAYSIZE(arguments), L"\"%s\"", folderPath);
        if (FAILED(formatHr))
        {
            return formatHr;
        }

        // ── 第 4 步：启动 RunOnce.exe ──

        SHELLEXECUTEINFOW sei{};
        sei.cbSize = sizeof(sei);
        sei.fMask = SEE_MASK_NOASYNC;
        sei.lpVerb = L"open";
        sei.lpFile = exePath;
        sei.lpParameters = arguments;
        sei.lpDirectory = folderPath;
        sei.nShow = SW_SHOWNORMAL;

        if (!ShellExecuteExW(&sei))
        {
            return HRESULT_FROM_WIN32(GetLastError());
        }

        return S_OK;
    }
};

// ============================================================================
// COM 类注册
// ============================================================================

CoCreatableClass(RunOnceCommand);

// ============================================================================
// DLL 入口点
// ============================================================================

BOOL APIENTRY DllMain(
    HMODULE hModule,
    DWORD dwReason,
    LPVOID /* lpReserved */)
{
    if (dwReason == DLL_PROCESS_ATTACH)
    {
        g_hModule = hModule;
        DisableThreadLibraryCalls(hModule);
    }

    return TRUE;
}

// ============================================================================
// COM 导出函数
// ============================================================================

_Check_return_
    STDAPI
    DllGetClassObject(
        _In_ REFCLSID rclsid,
        _In_ REFIID riid,
        _COM_Outptr_ void **ppv)
{
    return Module<InProc>::GetModule().GetClassObject(rclsid, riid, ppv);
}

STDAPI DllCanUnloadNow()
{
    return Module<InProc>::GetModule().Terminate() ? S_OK : S_FALSE;
}