#pragma once
// Test-only COM recorders shared in shape with the existing navigation tests.
#include "SnapshotTestSupport.h"
#include <shlguid.h>
#include <servprov.h>
#include <functional>

class RefreshBrowser final : public IShellBrowser, public IServiceProvider {
public:
    HWND window=nullptr;
    IShellView* active=nullptr;
    ULONG refs=1,calls=0;UINT flags=0;PIDLIST_ABSOLUTE last=nullptr;
    HRESULT serviceResult=S_OK,browseResult=S_OK;
    std::function<void()> duringQuery,duringBrowse,duringActive,duringRelease;
    ~RefreshBrowser(){CoTaskMemFree(last);}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown||iid==IID_IShellBrowser||iid==IID_IOleWindow)*result=static_cast<IShellBrowser*>(this);
        else if(iid==IID_IServiceProvider)*result=static_cast<IServiceProvider*>(this);else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override {
        auto callback=std::move(duringRelease);if(callback)callback();
        const auto count=--refs;if(!count)delete this;return count;
    }
    HRESULT STDMETHODCALLTYPE QueryService(REFGUID service,REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(service!=SID_STopLevelBrowser||iid!=IID_IShellBrowser)return E_NOINTERFACE;
        if(duringQuery)duringQuery();
        if(FAILED(serviceResult))return serviceResult;return QueryInterface(iid,result);
    }
    HRESULT STDMETHODCALLTYPE BrowseObject(PCUIDLIST_RELATIVE target,UINT requested) override {
        if(duringBrowse)duringBrowse();
        ++calls;flags=requested;CoTaskMemFree(last);last=ILCloneFull(target);return last?browseResult:E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* result) override {if(!result)return E_POINTER;*result=window;return window?S_OK:E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE InsertMenusSB(HMENU,LPOLEMENUGROUPWIDTHS) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetMenuSB(HMENU,HOLEMENU,HWND) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE RemoveMenusSB(HMENU) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetStatusTextSB(LPCWSTR) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE EnableModelessSB(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE TranslateAcceleratorSB(MSG*,WORD) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetViewStateStream(DWORD,IStream** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetControlWindow(UINT,HWND* result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SendControlMsg(UINT,UINT,WPARAM,LPARAM,LRESULT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE QueryActiveShellView(IShellView** result) override {if(!result)return E_POINTER;if(duringActive)duringActive();*result=active;if(active)active->AddRef();return active?S_OK:E_FAIL;}
    HRESULT STDMETHODCALLTYPE OnViewWindowActive(IShellView*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetToolbarItems(LPTBBUTTONSB,UINT,UINT) override {return E_NOTIMPL;}
};
