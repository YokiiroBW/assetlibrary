#include "ProbeDiagnostics.h"
#include "LoadingRefresh.h"
#include <new>

namespace diagnostics {
namespace {std::atomic<ULONGLONG> nextFolder{1};}
FolderState::FolderState(ULONGLONG sourceInstance) noexcept:instance(nextFolder.fetch_add(1)),source(sourceInstance){}
HRESULT Read(const FolderState& folder,const loading::Signal* signal,VARIANT* value) noexcept {
    if(!value)return E_POINTER;VariantInit(value);
    try {
        std::wstring text=L"{";text.reserve(MaxCharacters);
        const auto field=[&](const wchar_t* name,ULONGLONG number){text+=L"\"";text+=name;text+=L"\":";text+=std::to_wstring(number);text+=L",";};
        field(L"v",1);field(L"pid",GetCurrentProcessId());field(L"tick",GetTickCount64());
        field(L"folder",folder.instance);field(L"source",folder.source);field(L"last_clone",folder.lastClone.load());
        field(L"folder_tid",folder.createdThread);field(L"enum_n",folder.enumerations.load());field(L"enum_tid",folder.enumThread.load());
        field(L"enum_status",static_cast<ULONG>(folder.enumStatus.load()));field(L"last_cb_hr",static_cast<ULONG>(folder.lastCallbackResult.load()));
        field(L"slots",loading::ActiveViews());
        if(!signal)text+=L"\"view\":null}";
        else {
            text+=L"\"view\":{";const auto& d=signal->diagnostic;
            field(L"cookie",signal->cookie);field(L"started",signal->started.load());field(L"published",signal->published.load()>>3);
            snapshot::Status status=snapshot::Status::Unavailable;ULONGLONG generation=0;const bool current=signal->Read(status,&generation);
            field(L"current",current?1:0);field(L"status",static_cast<ULONG>(status));
            field(L"cb_hr",static_cast<ULONG>(d.createResult.load()));field(L"cb_slots",d.createSlots.load());field(L"cb_tid",d.constructorThread.load());
            field(L"site_n",d.siteCalls.load());field(L"site_tid",d.siteThread.load());field(L"site_hr",static_cast<ULONG>(d.siteResult.load()));field(L"site",d.sitePresent.load());
            field(L"window_n",d.windowCalls.load());field(L"window_tid",d.windowThread.load());field(L"owner_tid",d.windowOwnerThread.load());
            field(L"window_hr",static_cast<ULONG>(d.windowResult.load()));field(L"reported_hwnd",d.reportedWindow.load());field(L"attached_hwnd",reinterpret_cast<UINT_PTR>(signal->window.load()));
            field(L"post_n",d.posts.load());field(L"post_error",d.postError.load());field(L"arm_n",d.arms.load());field(L"arm_tid",d.armThread.load());field(L"arm_error",d.armError.load());field(L"timer",d.timerActive.load());
            field(L"tick_n",d.ticks.load());field(L"attempt_n",d.attempts.load());field(L"service_hr",static_cast<ULONG>(d.serviceResult.load()));field(L"active_hr",static_cast<ULONG>(d.activeResult.load()));
            field(L"getwindow_hr",static_cast<ULONG>(d.getWindowResult.load()));field(L"active_hwnd",d.activeWindow.load());field(L"match",d.windowMatch.load());field(L"skip",d.skip.load());
            field(L"refresh_n",d.refreshes.load());field(L"refresh_hr",static_cast<ULONG>(d.refreshResult.load()));field(L"detach_n",d.detaches.load());
            text.pop_back();text+=L"}}";
        }
        if(text.size()>MaxCharacters)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        value->bstrVal=SysAllocStringLen(text.data(),static_cast<UINT>(text.size()));if(!value->bstrVal)return E_OUTOFMEMORY;
        value->vt=VT_BSTR;return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
}
