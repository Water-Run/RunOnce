/*
 * RunOnce Shell Extension
 * 实现 Windows 11 一级右键菜单项，在文件夹空白处显示 "在此运行代码" 与 "在此用AI生成代码"
 * 通过 IExplorerCommand COM 接口向文件资源管理器注册上下文菜单命令
 *
 * 工作原理：
 *   1. MSIX 安装时，系统读取 Package.appxmanifest 中的声明，将本 DLL 注册为 COM 服务器
 *   2. 用户在文件夹空白处右键时，Explorer 加载本 DLL 并创建命令实例
 *   3. Explorer 调用 GetTitle/GetIcon 获取菜单显示信息
 *   4. 用户点击菜单项后，Explorer 调用 Invoke，本 DLL 启动 RunOnce.exe 并传入文件夹路径
 *   5. MSIX 卸载时，系统自动撤销所有注册
 *
 * @author: WaterRun
 * @file: dllmain.cpp
 * @date: 2026-03-22
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
// 内部辅助：从 IShellItemArray 中提取第一个文件夹路径
// ============================================================================

static bool TryGetFolderPath(IShellItemArray* psiItemArray, WCHAR folderPath[MAX_PATH])
{
    folderPath[0] = L'\0';

    if (!psiItemArray)
    {
        return false;
    }

    DWORD itemCount = 0;
    if (FAILED(psiItemArray->GetCount(&itemCount)) || itemCount == 0)
    {
        return false;
    }

    ComPtr<IShellItem> shellItem;
    if (FAILED(psiItemArray->GetItemAt(0, &shellItem)))
    {
        return false;
    }

    PWSTR rawPath = nullptr;
    HRESULT hr = shellItem->GetDisplayName(SIGDN_FILESYSPATH, &rawPath);
    if (SUCCEEDED(hr) && rawPath)
    {
        StringCchCopyW(folderPath, MAX_PATH, rawPath);
        CoTaskMemFree(rawPath);
        return true;
    }

    return false;
}

// ============================================================================
// 内部辅助：启动 RunOnce.exe
// ============================================================================

/// <param name="folderPath">工作目录路径（已验证非空）。</param>
/// <param name="extraArgs">追加到文件夹路径之后的额外命令行参数，如 " --ai"，可为 nullptr。</param>
static HRESULT LaunchRunOnce(PCWSTR folderPath, PCWSTR extraArgs)
{
    WCHAR exePath[MAX_PATH]{};
    if (!GetModuleFileNameW(g_hModule, exePath, MAX_PATH))
    {
        return HRESULT_FROM_WIN32(GetLastError());
    }

    PathRemoveFileSpecW(exePath);
    PathAppendW(exePath, L"RunOnce.exe");

    if (!PathFileExistsW(exePath))
    {
        return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
    }

    WCHAR arguments[MAX_PATH + 32]{};
    HRESULT hr = StringCchPrintfW(
        arguments, ARRAYSIZE(arguments),
        extraArgs ? L"\"%s\"%s" : L"\"%s\"",
        folderPath, extraArgs ? extraArgs : L"");
    if (FAILED(hr))
    {
        return hr;
    }

    SHELLEXECUTEINFOW sei{};
    sei.cbSize       = sizeof(sei);
    sei.fMask        = SEE_MASK_NOASYNC;
    sei.lpVerb       = L"open";
    sei.lpFile       = exePath;
    sei.lpParameters = arguments;
    sei.lpDirectory  = folderPath;
    sei.nShow        = SW_SHOWNORMAL;

    return ShellExecuteExW(&sei) ? S_OK : HRESULT_FROM_WIN32(GetLastError());
}

// ============================================================================
// RunOnceCommand：IExplorerCommand 接口的完整实现（"在此运行代码"）
// ============================================================================

class __declspec(uuid("D4E5F601-A2B3-4C5D-8E9F-0A1B2C3D4E5F"))
RunOnceCommand : public RuntimeClass<
                     RuntimeClassFlags<ClassicCom>,
                     IExplorerCommand>
{
public:
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

    IFACEMETHODIMP GetToolTip(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _Outptr_ LPWSTR *ppszInfotip) override
    {
        *ppszInfotip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(_Out_ GUID *pguidCommandName) override
    {
        *pguidCommandName = __uuidof(RunOnceCommand);
        return S_OK;
    }

    IFACEMETHODIMP GetState(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _In_ BOOL /* fOkToBeSlow */,
        _Out_ EXPCMDSTATE *pCmdState) override
    {
        *pCmdState = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS *pFlags) override
    {
        *pFlags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(_Outptr_ IEnumExplorerCommand **ppEnum) override
    {
        *ppEnum = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP Invoke(
        _In_opt_ IShellItemArray *psiItemArray,
        _In_opt_ IBindCtx * /* pbc */) override
    {
        WCHAR folderPath[MAX_PATH]{};
        if (!TryGetFolderPath(psiItemArray, folderPath))
        {
            return S_OK;
        }
        return LaunchRunOnce(folderPath, nullptr);
    }
};

// ============================================================================
// RunOnceAiCommand：IExplorerCommand 接口实现（"在此用AI生成代码"）
// ============================================================================

class __declspec(uuid("E1C5F602-A3B4-5C6D-9E0F-1A2B3C4D5E6F"))
RunOnceAiCommand : public RuntimeClass<
                       RuntimeClassFlags<ClassicCom>,
                       IExplorerCommand>
{
public:
    IFACEMETHODIMP GetTitle(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _Outptr_ LPWSTR *ppszName) override
    {
        WCHAR localeName[LOCALE_NAME_MAX_LENGTH]{};
        GetUserDefaultLocaleName(localeName, LOCALE_NAME_MAX_LENGTH);

        const bool isChinese = (wcsncmp(localeName, L"zh", 2) == 0);
        // "在此用AI生成代码" / "AI Generate Script Here"
        return SHStrDupW(
            isChinese
                ? L"\x5728\x6B64\x7528" L"AI\x751F\x6210\x4EE3\x7801"
                : L"AI Generate Script Here",
            ppszName);
    }

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

    IFACEMETHODIMP GetToolTip(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _Outptr_ LPWSTR *ppszInfotip) override
    {
        *ppszInfotip = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP GetCanonicalName(_Out_ GUID *pguidCommandName) override
    {
        *pguidCommandName = __uuidof(RunOnceAiCommand);
        return S_OK;
    }

    IFACEMETHODIMP GetState(
        _In_opt_ IShellItemArray * /* psiItemArray */,
        _In_ BOOL /* fOkToBeSlow */,
        _Out_ EXPCMDSTATE *pCmdState) override
    {
        *pCmdState = ECS_ENABLED;
        return S_OK;
    }

    IFACEMETHODIMP GetFlags(_Out_ EXPCMDFLAGS *pFlags) override
    {
        *pFlags = ECF_DEFAULT;
        return S_OK;
    }

    IFACEMETHODIMP EnumSubCommands(_Outptr_ IEnumExplorerCommand **ppEnum) override
    {
        *ppEnum = nullptr;
        return E_NOTIMPL;
    }

    IFACEMETHODIMP Invoke(
        _In_opt_ IShellItemArray *psiItemArray,
        _In_opt_ IBindCtx * /* pbc */) override
    {
        WCHAR folderPath[MAX_PATH]{};
        if (!TryGetFolderPath(psiItemArray, folderPath))
        {
            return S_OK;
        }
        return LaunchRunOnce(folderPath, L" --ai");
    }
};

// ============================================================================
// COM 类注册
// ============================================================================

CoCreatableClass(RunOnceCommand);
CoCreatableClass(RunOnceAiCommand);

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
