#include "ProbeDiagnostics.h"
#include "LoadingRefresh.h"
#include "G3Measurements.h"
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
            field(L"folderview_hr",static_cast<ULONG>(d.folderViewResult.load()));field(L"count_hr",static_cast<ULONG>(d.countResult.load()));field(L"item_hr",static_cast<ULONG>(d.itemResult.load()));
            field(L"rendered_n",d.itemCount.load());field(L"pidl_valid",d.pidlValid.load());field(L"rendered_kind",d.renderedKind.load());field(L"rendered_status",d.renderedStatus.load());
            field(L"refresh_n",d.refreshes.load());field(L"refresh_hr",static_cast<ULONG>(d.refreshResult.load()));field(L"detach_n",d.detaches.load());
            text.pop_back();text+=L"}}";
        }
        if(text.size()>MaxCharacters)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        value->bstrVal=SysAllocStringLen(text.data(),static_cast<UINT>(text.size()));if(!value->bstrVal)return E_OUTOFMEMORY;
        value->vt=VT_BSTR;return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
HRESULT ReadG3(const FolderState& folder,const loading::Signal* signal,LONG objects,LONG locks,VARIANT* value) noexcept {
    if(!value)return E_POINTER;VariantInit(value);
    try {
        std::wstring text=L"{";text.reserve(MaxCharacters);
        const auto field=[&](const wchar_t* name,ULONGLONG number){text+=L"\"";text+=name;text+=L"\":";text+=std::to_wstring(number);text+=L",";};
        field(L"v",1);field(L"pid",GetCurrentProcessId());field(L"qpc",g3::Now());field(L"hz",g3::Frequency());
        field(L"folder",folder.instance);field(L"objects",objects);field(L"locks",locks);
        field(L"callbacks",loading::LiveCallbacks());field(L"slots",loading::ActiveViews());field(L"op_live",snapshot::PendingOperations());
        const auto timing=[&](const wchar_t* prefix,const g3::Timing& measured){
            const auto part=[&](const wchar_t* suffix,ULONGLONG number){field((std::wstring(prefix)+suffix).c_str(),number);};
            part(L"_n",measured.calls.load());part(L"_start",measured.start.load());part(L"_end",measured.end.load());
            part(L"_last",measured.last.load());part(L"_max",measured.maximum.load());part(L"_tid",measured.thread.load());part(L"_live",measured.active.load());
        };
        const auto& m=g3::measurements;timing(L"enum",m.enumeration);timing(L"query",m.query);timing(L"refresh",m.refresh);
        field(L"cancel_n",m.cancels.load());field(L"defer_n",m.deferred.load());field(L"reap_n",m.reaped.load());field(L"cancel_live",m.cancelLive.load());
        field(L"cancel_last",m.cancelLast.load());field(L"cancel_max",m.cancelMaximum.load());
        field(L"pins",m.pins.load());field(L"pin_sync",m.pinSync.load());field(L"pin_pool",m.pinPool.load());
        field(L"view",signal?signal->cookie:0);field(L"obs_start",signal?signal->diagnostic.observationStart.load():0);
        field(L"ready_qpc",signal?signal->diagnostic.renderedReady.load():0);field(L"ready_tid",signal?signal->diagnostic.readyThread.load():0);
        text.pop_back();text+=L"}";
        if(text.size()>MaxCharacters)return HRESULT_FROM_WIN32(ERROR_INSUFFICIENT_BUFFER);
        value->bstrVal=SysAllocStringLen(text.data(),static_cast<UINT>(text.size()));if(!value->bstrVal)return E_OUTOFMEMORY;
        value->vt=VT_BSTR;return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
}
