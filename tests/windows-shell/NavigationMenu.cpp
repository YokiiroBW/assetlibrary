#include "NavigationMenu.h"
#include "SnapshotPidl.h"
#include <shlwapi.h>
#include <shlguid.h>
#include <strsafe.h>
#include <shellapi.h>
#include <new>

namespace {
// This is a namespace item's menu, not a registry-loaded context-menu extension.
// Owning the folder keeps the existing DLL object counter alive for this menu.
class NavigationMenu final : public IContextMenu, public IObjectWithSite {
    ULONG refs_=1;
    IUnknown* owner_;
    IUnknown* site_=nullptr;
    PIDLIST_ABSOLUTE target_;
public:
    NavigationMenu(IUnknown* owner,PIDLIST_ABSOLUTE target):owner_(owner),target_(target){owner_->AddRef();}
    ~NavigationMenu(){if(site_)site_->Release();CoTaskMemFree(target_);owner_->Release();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown||iid==IID_IContextMenu)*result=static_cast<IContextMenu*>(this);
        else if(iid==IID_IObjectWithSite)*result=static_cast<IObjectWithSite*>(this);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {auto refs=--refs_;if(!refs)delete this;return refs;}
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override {
        if(site)site->AddRef();auto previous=site_;site_=site;if(previous)previous->Release();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;return site_?site_->QueryInterface(iid,result):E_FAIL;
    }
    HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU menu,UINT position,UINT first,UINT last,UINT flags) override {
        if(!menu||first>last)return E_INVALIDARG;
        MENUITEMINFOW item{sizeof(item)};
        item.fMask=MIIM_ID|MIIM_STRING|MIIM_STATE;item.wID=first;
        wchar_t text[]=L"打开(&O)";item.dwTypeData=text;
        // CMF_DEFAULTONLY must include our default verb for double-click/Enter.
        item.fState=(flags&(CMF_NODEFAULT|CMF_DONOTPICKDEFAULT))?MFS_ENABLED:MFS_DEFAULT;
        if(!InsertMenuItemW(menu,position,TRUE,&item))return HRESULT_FROM_WIN32(GetLastError());
        return MAKE_HRESULT(SEVERITY_SUCCESS,0,1);
    }
    HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR id,UINT type,UINT*,LPSTR text,UINT characters) override {
        if(id!=0)return E_INVALIDARG;
        if(type==GCS_VALIDATEA||type==GCS_VALIDATEW)return S_OK;
        if(!text||!characters)return E_POINTER;
        if(type==GCS_VERBW)return StringCchCopyW(reinterpret_cast<LPWSTR>(text),characters,L"open");
        if(type==GCS_VERBA)return StringCchCopyA(text,characters,"open");
        if(type==GCS_HELPTEXTW)return StringCchCopyW(reinterpret_cast<LPWSTR>(text),characters,L"在当前窗口打开");
        if(type==GCS_HELPTEXTA)return StringCchCopyA(text,characters,"Open in this window");
        return E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE InvokeCommand(CMINVOKECOMMANDINFO* info) override {
        if(!info||info->cbSize<sizeof(CMINVOKECOMMANDINFO))return E_INVALIDARG;
        LPCWSTR wideVerb=nullptr;
        if(info->fMask&CMIC_MASK_UNICODE){
            if(info->cbSize<sizeof(CMINVOKECOMMANDINFOEX))return E_INVALIDARG;
            wideVerb=reinterpret_cast<const CMINVOKECOMMANDINFOEX*>(info)->lpVerbW;
        }
        const bool ansiString=!IS_INTRESOURCE(info->lpVerb);
        bool open=false;
        if(wideVerb&&!IS_INTRESOURCE(wideVerb)){
            // A Unicode string selects the verb; lpVerb's numeric value is then unused.
            open=_wcsnicmp(wideVerb,L"open",5)==0;
            if(ansiString&&_strnicmp(info->lpVerb,"open",5)!=0)return E_FAIL;
        }else if(ansiString)open=_strnicmp(info->lpVerb,"open",5)==0;
        // An identifier offset is carried only by lpVerb, never by lpVerbW.
        else open=reinterpret_cast<ULONG_PTR>(info->lpVerb)==0;
        if(!open)return E_FAIL;
        if(!site_)return E_NOINTERFACE;
        // QueryService/BrowseObject may tear down the view and its last menu reference.
        struct InvocationReference { NavigationMenu* menu;~InvocationReference(){menu->Release();} };
        AddRef();const InvocationReference invocation{this};
        auto site=site_;site->AddRef();IShellBrowser* browser=nullptr;
        auto hr=IUnknown_QueryService(site,SID_STopLevelBrowser,IID_PPV_ARGS(&browser));site->Release();
        if(SUCCEEDED(hr)&&browser){
            // Absolute identity is captured at GetUIObjectOf, never parsed from a name.
            hr=browser->BrowseObject(target_,SBSP_ABSOLUTE|SBSP_SAMEBROWSER);browser->Release();
        }else if(SUCCEEDED(hr))hr=E_NOINTERFACE;
        return hr;
    }
};
}
HRESULT CreateNavigationMenu(IUnknown* owner,PCIDLIST_ABSOLUTE parent,UINT count,
    PCUITEMID_CHILD_ARRAY items,REFIID iid,void** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;
    if(iid!=IID_IContextMenu||count!=1)return E_NOINTERFACE;
    if(!owner||!parent||!items||!items[0])return E_INVALIDARG;
    try {
        UINT parentBytes=0,parentCount=0,childBytes=0,childCount=0;snapshot::Entry entry;
        if(!snapshot::BoundedList(parent,parentBytes,parentCount)||!snapshot::BoundedList(items[0],childBytes,childCount)
            ||childCount!=1||parentCount>=64||parentBytes+childBytes-2>snapshot::MaxPayload
            ||!snapshot::ReadPidl(items[0],entry))return E_INVALIDARG;
        if(!snapshot::Navigable(entry.kind))return E_NOINTERFACE;
        auto target=ILCombine(parent,items[0]);if(!target)return E_OUTOFMEMORY;
        auto menu=new(std::nothrow) NavigationMenu(owner,target);
        if(!menu){CoTaskMemFree(target);return E_OUTOFMEMORY;}
        auto hr=menu->QueryInterface(iid,result);menu->Release();return hr;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
