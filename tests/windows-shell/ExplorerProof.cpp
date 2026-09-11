#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <propkey.h>
#include <propvarutil.h>
#include <atomic>
#include <new>
#include <string>
#include <vector>
#include "SnapshotPidl.h"
#include "NavigationMenu.h"
#include "SnapshotIcon.h"
#include "LoadingRefresh.h"
#include <cstring>

namespace {
constexpr CLSID kClsid = {0x4ff8301d,0x2e73,0x4d49,{0x9f,0xe5,0x86,0x8d,0x5f,0x1e,0xa3,0x02}};
std::atomic_long objects{0};
std::atomic_long locks{0};
SFGAOF Attributes(snapshot::Kind kind) {
  return SFGAO_READONLY | (snapshot::Navigable(kind) ? SFGAO_FOLDER | SFGAO_HASSUBFOLDER | SFGAO_BROWSABLE : 0);
}

class Enumerator final : public IEnumIDList {
  ULONG refs_ = 1; ULONG cursor_ = 0; std::vector<snapshot::Entry> ids_;
 public:
  explicit Enumerator(std::vector<snapshot::Entry> ids) : ids_(std::move(ids)) { ++objects; }
  ~Enumerator() { --objects; }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr;
    if (iid != IID_IUnknown && iid != IID_IEnumIDList) return E_NOINTERFACE;
    *value = static_cast<IEnumIDList*>(this); AddRef(); return S_OK;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
  ULONG STDMETHODCALLTYPE Release() override { auto count = --refs_; if (!count) delete this; return count; }
  HRESULT STDMETHODCALLTYPE Next(ULONG count, PITEMID_CHILD* items, ULONG* fetched) override {
    if (!items || (!fetched && count != 1)) return E_POINTER;
    ULONG actual = 0;
    while (actual < count && cursor_ < ids_.size()) {
      auto item = snapshot::MakePidl(ids_[cursor_]);
      if (!item) { while(actual) { CoTaskMemFree(items[--actual]); items[actual]=nullptr; --cursor_; } if(fetched)*fetched=0; return E_OUTOFMEMORY; }
      items[actual++] = item; ++cursor_;
    }
    if (fetched) *fetched = actual; return actual == count ? S_OK : S_FALSE;
  }
  HRESULT STDMETHODCALLTYPE Skip(ULONG count) override {
    ULONG actual = 0; while (actual < count && cursor_ < ids_.size()) { ++actual; ++cursor_; }
    return actual == count ? S_OK : S_FALSE;
  }
  HRESULT STDMETHODCALLTYPE Reset() override { cursor_ = 0; return S_OK; }
  HRESULT STDMETHODCALLTYPE Clone(IEnumIDList** result) override {
    if (!result) return E_POINTER; *result=nullptr;
    try { auto clone = new(std::nothrow) Enumerator(ids_);
      if (!clone) return E_OUTOFMEMORY; clone->cursor_ = cursor_; *result = clone; return S_OK;
    } catch(const std::bad_alloc&) { return E_OUTOFMEMORY; }
  }
};

class Folder final : public IShellFolder2, public IPersistFolder2 {
  ULONG refs_ = 1; PIDLIST_ABSOLUTE absolute_ = nullptr; snapshot::Location location_;
  std::shared_ptr<loading::Signal> viewState_;
 public:
  Folder() { ++objects; }
  explicit Folder(std::shared_ptr<loading::Signal> state):viewState_(std::move(state)){++objects;}
  ~Folder() { CoTaskMemFree(absolute_); --objects; }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr;
    if (iid == IID_IUnknown || iid == IID_IShellFolder || iid == IID_IShellFolder2) *value = static_cast<IShellFolder2*>(this);
    else if (iid == IID_IPersist || iid == IID_IPersistFolder || iid == IID_IPersistFolder2) *value = static_cast<IPersistFolder2*>(this);
    else return E_NOINTERFACE;
    AddRef(); return S_OK;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
  ULONG STDMETHODCALLTYPE Release() override { auto count = --refs_; if (!count) delete this; return count; }
  HRESULT STDMETHODCALLTYPE GetClassID(CLSID* value) override { if (!value) return E_POINTER; *value = kClsid; return S_OK; }
  HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE value) override {
    UINT bytes=0,count=0;if(!snapshot::BoundedList(value,bytes,count))return E_INVALIDARG;
    try {
      snapshot::Location location;auto current=reinterpret_cast<const BYTE*>(value);
      for(UINT i=0;i<count;++i){
        auto item=reinterpret_cast<PCUIDLIST_RELATIVE>(current);snapshot::Entry entry;
        if(snapshot::IsOurPidl(item)) { if(!snapshot::ReadPidl(item,entry)||!snapshot::Navigable(entry.kind))return E_INVALIDARG;location={entry.epoch,entry.node}; }
        else if(i!=0)return E_INVALIDARG; // Windows supplies an opaque namespace root prefix.
        current+=item->mkid.cb;
      }
      auto next=static_cast<PIDLIST_ABSOLUTE>(CoTaskMemAlloc(bytes));if(!next)return E_OUTOFMEMORY;
      std::memcpy(next,value,bytes);CoTaskMemFree(absolute_);absolute_=next;location_=location;return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE GetCurFolder(PIDLIST_ABSOLUTE* value) override {
    if (!value) return E_POINTER; *value = ILCloneFull(absolute_); return *value ? S_OK : E_OUTOFMEMORY;
  }
  HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND, IBindCtx*, LPWSTR name, ULONG* eaten, PIDLIST_RELATIVE* value, ULONG* attributes) override {
    if(!name||!value)return E_POINTER;*value=nullptr;if(eaten)*eaten=0;
    try {
      snapshot::Kind kind;
      if(!snapshot::ParseName(name,value,eaten,kind))return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
      if(attributes)*attributes&=Attributes(kind);return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE EnumObjects(HWND, SHCONTF flags, IEnumIDList** value) override {
    if(!value)return E_POINTER;*value=nullptr;
    const auto generation=viewState_?viewState_->Begin():0;
    try {
      auto page=snapshot::Query(location_);auto entries=std::move(page.entries);
      if(page.status!=snapshot::Status::Ready)entries.push_back({page.epoch,{},snapshot::Kind::StatusRow,snapshot::StatusText(page.status),page.status});
      std::vector<snapshot::Entry> visible;
      for(const auto& entry:entries)if(flags&(snapshot::Navigable(entry.kind)?SHCONTF_FOLDERS:SHCONTF_NONFOLDERS))visible.push_back(entry);
      *value=new(std::nothrow) Enumerator(std::move(visible));
      if(viewState_)viewState_->Publish(generation,*value?page.status:snapshot::Status::Unavailable);
      return *value?S_OK:E_OUTOFMEMORY;
    }catch(const std::bad_alloc&){if(viewState_)viewState_->Publish(generation,snapshot::Status::Unavailable);return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE pidl, IBindCtx*, REFIID iid, void** value) override {
    if(!value)return E_POINTER;*value=nullptr;UINT bytes=0,count=0;
    if(!absolute_||!snapshot::BoundedList(pidl,bytes,count)||!count)return E_INVALIDARG;
    try {
      auto current=reinterpret_cast<const BYTE*>(pidl);
      for(UINT i=0;i<count;++i){snapshot::Entry entry;auto item=reinterpret_cast<PCUIDLIST_RELATIVE>(current);
        if(!snapshot::ReadPidl(item,entry))return E_INVALIDARG;if(!snapshot::Navigable(entry.kind))return E_ACCESSDENIED;current+=item->mkid.cb;
      }
      auto combined=ILCombine(absolute_,pidl);if(!combined)return E_OUTOFMEMORY;
      auto child=new(std::nothrow) Folder();if(!child){CoTaskMemFree(combined);return E_OUTOFMEMORY;}
      auto hr=child->Initialize(combined);CoTaskMemFree(combined);if(SUCCEEDED(hr))hr=child->QueryInterface(iid,value);child->Release();return hr;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE, IBindCtx*, REFIID, void** value) override { if (value) *value = nullptr; return E_NOINTERFACE; }
  HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM flags, PCUIDLIST_RELATIVE a, PCUIDLIST_RELATIVE b) override {
    const auto column=static_cast<UINT>(flags&SHCIDS_COLUMNMASK);
    if(column>1)return E_INVALIDARG;
    UINT bytes=0,count=0;if(!snapshot::BoundedList(a,bytes,count)||!snapshot::BoundedList(b,bytes,count))return E_INVALIDARG;
    try {
      int result=0;
      while(a->mkid.cb&&b->mkid.cb){snapshot::Entry left,right;
        if(!snapshot::ReadPidl(a,left)||!snapshot::ReadPidl(b,right))return E_INVALIDARG;
        if(!(flags&SHCIDS_CANONICALONLY)){
          if(column==1)result=wcscmp(snapshot::TypeText(left.kind),snapshot::TypeText(right.kind));
          if(!result)result=left.name.compare(right.name);
        }
        if(!result)result=std::memcmp(&left.epoch,&right.epoch,16);
        if(!result)result=std::memcmp(&left.node,&right.node,16);
        if(!result)result=static_cast<int>(left.kind)-static_cast<int>(right.kind);
        if(result)break;
        a=reinterpret_cast<PCUIDLIST_RELATIVE>(reinterpret_cast<const BYTE*>(a)+a->mkid.cb);
        b=reinterpret_cast<PCUIDLIST_RELATIVE>(reinterpret_cast<const BYTE*>(b)+b->mkid.cb);
      }
      if(!result)result=(a->mkid.cb!=0)-(b->mkid.cb!=0);
      return MAKE_HRESULT(SEVERITY_SUCCESS,0,static_cast<USHORT>(static_cast<SHORT>(result<0?-1:result>0?1:0)));
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE CreateViewObject(HWND, REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr; if (iid != IID_IShellView) return E_NOINTERFACE;
    if(!absolute_)return E_UNEXPECTED;
    try {
      auto state=std::make_shared<loading::Signal>();auto folder=new(std::nothrow) Folder(state);if(!folder)return E_OUTOFMEMORY;
      auto hr=folder->Initialize(absolute_);
      if(SUCCEEDED(hr))hr=loading::CreateView(static_cast<IShellFolder2*>(folder),state,reinterpret_cast<IShellView**>(value));
      folder->Release();return hr;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT count, PCUITEMID_CHILD_ARRAY items, SFGAOF* attributes) override {
    if(!attributes||(count&&!items))return E_POINTER;if(count>snapshot::MaxItems)return E_INVALIDARG;
    try {SFGAOF result=Attributes(snapshot::Kind::Library);
      for(UINT i=0;i<count;++i){snapshot::Entry entry;if(!snapshot::ReadPidl(items[i],entry))return E_INVALIDARG;result&=Attributes(entry.kind);}
      *attributes&=result;return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND, UINT count, PCUITEMID_CHILD_ARRAY items, REFIID iid, UINT*, void** value) override {
    if(iid==IID_IExtractIconW||iid==IID_IExtractIconA)return CreateSnapshotIcon(count,items,iid,value);
    return CreateNavigationMenu(static_cast<IShellFolder2*>(this),absolute_,count,items,iid,value);
  }
  HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD pidl, SHGDNF flags, STRRET* value) override {
    if(!value)return E_POINTER;
    try {snapshot::Entry entry;if(!snapshot::ReadPidl(pidl,entry))return E_INVALIDARG;
      std::wstring name=entry.name;
      if(flags&SHGDN_FORPARSING){name=snapshot::ParsingName(entry);if(name.empty())return E_ACCESSDENIED;
        if(!(flags&SHGDN_INFOLDER)){
          std::wstring prefix=L"::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}";
          if(absolute_){auto at=reinterpret_cast<PCUIDLIST_RELATIVE>(absolute_);
            while(at->mkid.cb){snapshot::Entry parent;if(snapshot::ReadPidl(at,parent))prefix+=L"\\"+snapshot::ParsingName(parent);
              at=reinterpret_cast<PCUIDLIST_RELATIVE>(reinterpret_cast<const BYTE*>(at)+at->mkid.cb);}}
          name=prefix+L"\\"+name;
        }
      }
      value->uType=STRRET_WSTR;return SHStrDupW(name.c_str(),&value->pOleStr);
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE SetNameOf(HWND, PCUITEMID_CHILD, LPCWSTR, SHGDNF, PITEMID_CHILD* value) override { if (value) *value = nullptr; return E_ACCESSDENIED; }
  HRESULT STDMETHODCALLTYPE GetDefaultSearchGUID(GUID*) override { return E_NOTIMPL; }
  HRESULT STDMETHODCALLTYPE EnumSearches(IEnumExtraSearch**) override { return E_NOTIMPL; }
  HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD, ULONG* sort, ULONG* display) override { if (!sort || !display) return E_POINTER; *sort = 0; *display = 0; return S_OK; }
  HRESULT STDMETHODCALLTYPE GetDefaultColumnState(UINT column, SHCOLSTATEF* state) override { if (!state) return E_POINTER; if (column > 1) return E_INVALIDARG; *state = SHCOLSTATE_TYPE_STR | SHCOLSTATE_ONBYDEFAULT; return S_OK; }
  HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD item, const SHCOLUMNID* key, VARIANT* value) override {
    if(!key||!value)return E_POINTER;VariantInit(value);
    if(!IsEqualPropertyKey(*key,PKEY_ItemNameDisplay)&&!IsEqualPropertyKey(*key,PKEY_ItemTypeText))return E_INVALIDARG;
    try {snapshot::Entry entry;if(!snapshot::ReadPidl(item,entry))return E_INVALIDARG;
      value->bstrVal=SysAllocString(IsEqualPropertyKey(*key,PKEY_ItemNameDisplay)?entry.name.c_str():snapshot::TypeText(entry.kind));
      if(!value->bstrVal)return E_OUTOFMEMORY;value->vt=VT_BSTR;return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD item, UINT column, SHELLDETAILS* details) override {
    if(!details)return E_POINTER;if(column>1)return E_INVALIDARG;
    details->fmt=LVCFMT_LEFT;details->cxChar=column==0?28:24;details->str.uType=STRRET_WSTR;
    try {snapshot::Entry entry;if(item&&!snapshot::ReadPidl(item,entry))return E_INVALIDARG;
      const wchar_t* text=item?(column==0?entry.name.c_str():snapshot::TypeText(entry.kind)):(column==0?L"名称":L"类型");
      return SHStrDupW(text,&details->str.pOleStr);
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
  }
  HRESULT STDMETHODCALLTYPE MapColumnToSCID(UINT column, SHCOLUMNID* key) override {
    if (!key) return E_POINTER; if (column > 1) return E_INVALIDARG; *key = column == 0 ? PKEY_ItemNameDisplay : PKEY_ItemTypeText; return S_OK;
  }
};

class Factory final : public IClassFactory {
  ULONG refs_ = 1;
 public:
  Factory() { ++objects; } ~Factory() { --objects; }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr; if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
    *value = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
  ULONG STDMETHODCALLTYPE Release() override { auto count = --refs_; if (!count) delete this; return count; }
  HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
    auto folder = new(std::nothrow) Folder(); if (!folder) return E_OUTOFMEMORY;
    auto hr = folder->QueryInterface(iid, value); folder->Release(); return hr;
  }
  HRESULT STDMETHODCALLTYPE LockServer(BOOL value) override { if(value) ++locks; else --locks; return S_OK; }
};
}
__control_entrypoint(DllExport) STDAPI DllCanUnloadNow() { return objects == 0 && locks == 0 && snapshot::PendingOperations() == 0 ? S_OK : S_FALSE; }
_Check_return_ STDAPI DllGetClassObject(_In_ REFCLSID clsid, _In_ REFIID iid, _Outptr_ void** value) {
  if (!value) return E_POINTER; *value = nullptr; if (clsid != kClsid) return CLASS_E_CLASSNOTAVAILABLE;
  auto factory = new(std::nothrow) Factory(); if (!factory) return E_OUTOFMEMORY;
  auto hr = factory->QueryInterface(iid, value); factory->Release(); return hr;
}
