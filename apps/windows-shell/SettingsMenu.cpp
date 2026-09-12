#include "SettingsMenu.h"
#include "SettingsCommand.h"
#include <strsafe.h>
#include <new>

namespace {
class SettingsMenu final : public IContextMenu {
    ULONG refs_=1;
    IUnknown* owner_;
public:
    explicit SettingsMenu(IUnknown* owner):owner_(owner){owner_->AddRef();}
    ~SettingsMenu(){owner_->Release();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid!=IID_IUnknown&&iid!=IID_IContextMenu)return E_NOINTERFACE;
        *result=static_cast<IContextMenu*>(this);AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {const auto refs=--refs_;if(!refs)delete this;return refs;}
    HRESULT STDMETHODCALLTYPE QueryContextMenu(HMENU menu,UINT position,UINT first,UINT last,UINT flags) override {
        if(!menu||first>last)return E_INVALIDARG;
        // Settings is explicit and never becomes a folder's default open action.
        if(flags&CMF_DEFAULTONLY)return MAKE_HRESULT(SEVERITY_SUCCESS,0,0);
        MENUITEMINFOW item{sizeof(item)};
        wchar_t label[]=L"连接设置(&S)";
        item.fMask=MIIM_ID|MIIM_STRING|MIIM_STATE;item.wID=first;
        item.dwTypeData=label;item.fState=MFS_ENABLED;
        if(!InsertMenuItemW(menu,position,TRUE,&item))return HRESULT_FROM_WIN32(GetLastError());
        return MAKE_HRESULT(SEVERITY_SUCCESS,0,1);
    }
    HRESULT STDMETHODCALLTYPE GetCommandString(UINT_PTR id,UINT type,UINT*,LPSTR text,UINT characters) override {
        if(id!=0)return E_INVALIDARG;
        if(type==GCS_VALIDATEA||type==GCS_VALIDATEW)return S_OK;
        if(!text||!characters)return E_POINTER;
        if(type==GCS_VERBW)return StringCchCopyW(reinterpret_cast<LPWSTR>(text),characters,L"assetlibrary.settings");
        if(type==GCS_VERBA)return StringCchCopyA(text,characters,"assetlibrary.settings");
        if(type==GCS_HELPTEXTW)return StringCchCopyW(reinterpret_cast<LPWSTR>(text),characters,L"打开连接设置；登录后重新打开资产库");
        if(type==GCS_HELPTEXTA)return StringCchCopyA(text,characters,"Open connection settings; reopen Asset Library after signing in");
        return E_INVALIDARG;
    }
    HRESULT STDMETHODCALLTYPE InvokeCommand(CMINVOKECOMMANDINFO* info) override {
        const auto hr=settings::ValidateVerb(info);if(FAILED(hr))return hr;
        // The caller's URI, parameters, directory and show flags never select a target.
        return settings::Launch();
    }
};
}
HRESULT CreateSettingsMenu(IUnknown* owner,REFIID iid,void** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;
    if(!owner)return E_INVALIDARG;if(iid!=IID_IContextMenu)return E_NOINTERFACE;
    auto menu=new(std::nothrow) SettingsMenu(owner);if(!menu)return E_OUTOFMEMORY;
    *result=static_cast<IContextMenu*>(menu);return S_OK;
}
