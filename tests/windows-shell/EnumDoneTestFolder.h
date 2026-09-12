#pragma once
#include "SnapshotTestSupport.h"
#include <shlwapi.h>
#include <commctrl.h>
#include <propkey.h>
#include <atomic>

namespace enum_done_test {
// One immutable, synthetic item. No namespace registration, storage or pipe access.
class Enumerator final : public IEnumIDList {
    std::atomic_ulong refs_{1};
    snapshot::Entry entry_;
    bool consumed_=false;
public:
    explicit Enumerator(snapshot::Entry entry):entry_(std::move(entry)){}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid!=IID_IUnknown&&iid!=IID_IEnumIDList)return E_NOINTERFACE;
        *result=static_cast<IEnumIDList*>(this);AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {auto left=--refs_;if(!left)delete this;return left;}
    HRESULT STDMETHODCALLTYPE Next(ULONG count,PITEMID_CHILD* result,ULONG* fetched) override {
        if(!result||(!fetched&&count!=1))return E_POINTER;if(fetched)*fetched=0;
        if(!count)return S_OK;if(consumed_)return S_FALSE;
        *result=snapshot::MakePidl(entry_);if(!*result)return E_OUTOFMEMORY;
        consumed_=true;if(fetched)*fetched=1;return count==1?S_OK:S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE Skip(ULONG count) override {if(!count)return S_OK;const bool available=!consumed_;consumed_=true;return available&&count==1?S_OK:S_FALSE;}
    HRESULT STDMETHODCALLTYPE Reset() override {consumed_=false;return S_OK;}
    HRESULT STDMETHODCALLTYPE Clone(IEnumIDList** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        try {auto clone=new(std::nothrow) Enumerator(entry_);if(!clone)return E_OUTOFMEMORY;
            clone->consumed_=consumed_;*result=clone;return S_OK;
        }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
    }
};
class Folder final : public IShellFolder2, public IPersistFolder2 {
    std::atomic_ulong refs_{1};
public:
    std::atomic_ulong enumerations{0};
    std::atomic<DWORD> enumThread{0};
    std::atomic<snapshot::Status> status{snapshot::Status::Ready};
    bool completeLoadingOnSecondEnumeration=false;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown||iid==IID_IShellFolder||iid==IID_IShellFolder2)*result=static_cast<IShellFolder2*>(this);
        else if(iid==IID_IPersist||iid==IID_IPersistFolder||iid==IID_IPersistFolder2)*result=static_cast<IPersistFolder2*>(this);
        else return E_NOINTERFACE;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {auto left=--refs_;if(!left)delete this;return left;}
    HRESULT STDMETHODCALLTYPE EnumObjects(HWND,SHCONTF,IEnumIDList** result) override {
        if(!result)return E_POINTER;*result=nullptr;const auto count=++enumerations;enumThread=GetCurrentThreadId();
        const auto displayed=completeLoadingOnSecondEnumeration&&count>1?snapshot::Status::Ready:status.load();
        try {snapshot::Entry entry{{1,0,0,{}},{displayed==snapshot::Status::Ready?2ul:0ul,0,0,{}},
            displayed==snapshot::Status::Ready?snapshot::Kind::Library:snapshot::Kind::StatusRow,
            displayed==snapshot::Status::Ready?L"V03-013 Ready":L"V03-013 Loading",displayed};
            *result=new(std::nothrow) Enumerator(std::move(entry));return *result?S_OK:E_OUTOFMEMORY;
        }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
    }
    HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND,IBindCtx*,LPWSTR,ULONG*,PIDLIST_RELATIVE* result,ULONG*) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE,IBindCtx*,REFIID,void** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE,IBindCtx*,REFIID,void** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM,PCUIDLIST_RELATIVE left,PCUIDLIST_RELATIVE right) override {
        snapshot::Entry a,b;if(!snapshot::ReadPidl(left,a)||!snapshot::ReadPidl(right,b))return E_INVALIDARG;
        return MAKE_HRESULT(SEVERITY_SUCCESS,0,static_cast<USHORT>(static_cast<SHORT>(a.name.compare(b.name))));
    }
    HRESULT STDMETHODCALLTYPE CreateViewObject(HWND,REFIID,void** result) override {if(result)*result=nullptr;return E_NOINTERFACE;}
    HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT,PCUITEMID_CHILD_ARRAY,SFGAOF* result) override {if(!result)return E_POINTER;*result&=SFGAO_READONLY;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND,UINT,PCUITEMID_CHILD_ARRAY,REFIID,UINT*,void** result) override {if(result)*result=nullptr;return E_NOINTERFACE;}
    HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD item,SHGDNF,STRRET* result) override {
        if(!result)return E_POINTER;snapshot::Entry entry;if(!snapshot::ReadPidl(item,entry))return E_INVALIDARG;
        result->uType=STRRET_WSTR;return SHStrDupW(entry.name.c_str(),&result->pOleStr);
    }
    HRESULT STDMETHODCALLTYPE SetNameOf(HWND,PCUITEMID_CHILD,LPCWSTR,SHGDNF,PITEMID_CHILD* result) override {if(result)*result=nullptr;return E_ACCESSDENIED;}
    HRESULT STDMETHODCALLTYPE GetDefaultSearchGUID(GUID*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE EnumSearches(IEnumExtraSearch** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD,ULONG* sort,ULONG* display) override {if(!sort||!display)return E_POINTER;*sort=*display=0;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetDefaultColumnState(UINT column,SHCOLSTATEF* result) override {if(!result)return E_POINTER;if(column)return E_INVALIDARG;*result=SHCOLSTATE_TYPE_STR|SHCOLSTATE_ONBYDEFAULT;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD,const SHCOLUMNID*,VARIANT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD item,UINT column,SHELLDETAILS* result) override {
        if(!result)return E_POINTER;if(column)return E_INVALIDARG;result->fmt=LVCFMT_LEFT;result->cxChar=24;
        if(item)return GetDisplayNameOf(item,SHGDN_NORMAL,&result->str);
        result->str.uType=STRRET_WSTR;return SHStrDupW(L"Name",&result->str.pOleStr);
    }
    HRESULT STDMETHODCALLTYPE MapColumnToSCID(UINT column,SHCOLUMNID* result) override {if(!result)return E_POINTER;if(column)return E_INVALIDARG;*result=PKEY_ItemNameDisplay;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetClassID(CLSID* result) override {if(!result)return E_POINTER;*result={0x26091301,0x1122,0x3344,{0x88,0,0,0,0,0,0,1}};return S_OK;}
    HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetCurFolder(PIDLIST_ABSOLUTE* result) override {
        if(!result)return E_POINTER;*result=static_cast<PIDLIST_ABSOLUTE>(CoTaskMemAlloc(sizeof(USHORT)));
        if(!*result)return E_OUTOFMEMORY;(*result)->mkid.cb=0;return S_OK;
    }
};
}
