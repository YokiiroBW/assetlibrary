#include "SettingsCommand.h"
#include "ProductIdentity.h"
#include <shellapi.h>
#include <new>
#include <vector>

namespace settings {
HRESULT BuildCommand(const std::wstring& modulePath,Command& command) noexcept {
    command=Command{};
    // The installer accepts a local absolute directory. Reject relative/UNC paths
    // instead of allowing current-directory or network executable resolution.
    const bool drive=modulePath.size()>3&&((modulePath[0]>=L'A'&&modulePath[0]<=L'Z')||(modulePath[0]>=L'a'&&modulePath[0]<=L'z'));
    if(!drive||modulePath[1]!=L':'||modulePath[2]!=L'\\'||modulePath.size()>=32767
        ||modulePath.find_first_of(L"\"\r\n/")!=std::wstring::npos
        ||modulePath.find(L'\0')!=std::wstring::npos)return E_INVALIDARG;
    const auto separator=modulePath.find_last_of(L'\\');
    if(separator==std::wstring::npos||separator+1==modulePath.size())return E_INVALIDARG;
    try {
        Command built;
        built.directory=modulePath.substr(0,separator+1);
        built.executable=built.directory+product::SettingsExecutable;
        built.commandLine=L"\""+built.executable+L"\"";
        if(built.commandLine.size()>=32767)return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
        command=std::move(built);return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
HRESULT ValidateVerb(const CMINVOKECOMMANDINFO* info) noexcept {
    if(!info||info->cbSize<sizeof(CMINVOKECOMMANDINFO))return E_INVALIDARG;
    LPCWSTR wide=nullptr;
    if(info->fMask&CMIC_MASK_UNICODE){
        if(info->cbSize<sizeof(CMINVOKECOMMANDINFOEX))return E_INVALIDARG;
        wide=reinterpret_cast<const CMINVOKECOMMANDINFOEX*>(info)->lpVerbW;
    }
    const bool ansiString=!IS_INTRESOURCE(info->lpVerb);
    constexpr char verb[]="assetlibrary.settings";
    constexpr wchar_t wideVerb[]=L"assetlibrary.settings";
    if(wide&&!IS_INTRESOURCE(wide)){
        if(_wcsnicmp(wide,wideVerb,_countof(wideVerb))!=0)return E_FAIL;
        if(ansiString&&_strnicmp(info->lpVerb,verb,_countof(verb))!=0)return E_FAIL;
        return S_OK;
    }
    if(ansiString)return _strnicmp(info->lpVerb,verb,_countof(verb))==0?S_OK:E_FAIL;
    return reinterpret_cast<ULONG_PTR>(info->lpVerb)==0?S_OK:E_FAIL;
}
HRESULT Launch() noexcept {
    HMODULE module=nullptr;
    const auto address=reinterpret_cast<LPCWSTR>(&Launch);
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,address,&module))
        return HRESULT_FROM_WIN32(GetLastError());
    try {
        std::vector<wchar_t> path(32768);
        const auto length=GetModuleFileNameW(module,path.data(),static_cast<DWORD>(path.size()));
        if(!length)return HRESULT_FROM_WIN32(GetLastError());
        if(length>=path.size())return HRESULT_FROM_WIN32(ERROR_FILENAME_EXCED_RANGE);
        Command command;auto hr=BuildCommand(std::wstring(path.data(),length),command);if(FAILED(hr))return hr;
        STARTUPINFOW startup{sizeof(startup)};PROCESS_INFORMATION process{};
        // lpApplicationName is exact; no shell association, search path, external
        // parameters, inherited handles, input-idle wait or in-process UI load.
        if(!CreateProcessW(command.executable.c_str(),command.commandLine.data(),nullptr,nullptr,FALSE,
            0,nullptr,command.directory.c_str(),&startup,&process))return HRESULT_FROM_WIN32(GetLastError());
        CloseHandle(process.hThread);CloseHandle(process.hProcess);return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
}
