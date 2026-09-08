#include <windows.h>
#include <shlobj.h>
#include <shlwapi.h>
#include <propkey.h>
#include <propvarutil.h>
#include <atomic>
#include <new>
#include <string>
#include <vector>
#include <cstdio>

namespace {
constexpr CLSID kClsid = {0x4ff8301d,0x2e73,0x4d49,{0x9f,0xe5,0x86,0x8d,0x5f,0x1e,0xa3,0x02}};
std::atomic_long objects{0};
std::atomic_long locks{0};
HMODULE proofModule = nullptr;
void Trace(const char* operation, REFIID iid = IID_NULL, HRESULT result = S_OK) noexcept {
  wchar_t path[MAX_PATH]{};
  if (!GetModuleFileNameW(proofModule, path, MAX_PATH)) return;
  wchar_t* slash = wcsrchr(path, L'\\'); if (!slash) return;
  if (wcscpy_s(slash + 1, MAX_PATH - static_cast<size_t>(slash + 1 - path), L"proof-calls.log") != 0) return;
  HANDLE file = CreateFileW(path, FILE_APPEND_DATA | FILE_READ_ATTRIBUTES, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
      nullptr, OPEN_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
  if (file == INVALID_HANDLE_VALUE) return;
  LARGE_INTEGER length{};
  if (GetFileSizeEx(file, &length) && length.QuadPart < 1048576) {
    wchar_t guid[40]{};
    if (StringFromGUID2(iid, guid, 40) <= 0) { CloseHandle(file); return; }
    char line[200]{};
    const int count = sprintf_s(line, "%lu %llu %s %ls %08lx\n", GetCurrentProcessId(), GetTickCount64(), operation, guid, static_cast<unsigned long>(result));
    DWORD written = 0;
    if (count > 0) { const BOOL ok = WriteFile(file, line, static_cast<DWORD>(count), &written, nullptr); (void)ok; }
  }
  CloseHandle(file);
}
#pragma pack(push, 1)
struct Item { USHORT size; USHORT id; };
#pragma pack(pop)

PIDLIST_RELATIVE MakeId(USHORT id) {
  auto p = static_cast<Item*>(CoTaskMemAlloc(sizeof(Item) + sizeof(USHORT)));
  if (!p) return nullptr;
  p->size = sizeof(Item); p->id = id;
  *reinterpret_cast<USHORT*>(reinterpret_cast<BYTE*>(p) + sizeof(Item)) = 0;
  return reinterpret_cast<PIDLIST_RELATIVE>(p);
}
USHORT Id(PCUIDLIST_RELATIVE p) {
  return p && p->mkid.cb == sizeof(Item) ? reinterpret_cast<const Item*>(p)->id : 0;
}
bool FolderId(USHORT id) { return id == 1 || id == 3; }
const wchar_t* Name(USHORT id) {
  switch(id) { case 1: return L"示例资源库"; case 2: return L"只读示例.txt"; case 3: return L"真实目录样例"; default: return L"资产库验证"; }
}

class Enumerator final : public IEnumIDList {
  ULONG refs_ = 1; ULONG cursor_ = 0; std::vector<USHORT> ids_;
 public:
  explicit Enumerator(std::vector<USHORT> ids) : ids_(std::move(ids)) { ++objects; }
  ~Enumerator() { --objects; }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr;
    Trace("Enumerator.QueryInterface", iid);
    if (iid != IID_IUnknown && iid != IID_IEnumIDList) return E_NOINTERFACE;
    *value = static_cast<IEnumIDList*>(this); AddRef(); return S_OK;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
  ULONG STDMETHODCALLTYPE Release() override { auto count = --refs_; if (!count) delete this; return count; }
  HRESULT STDMETHODCALLTYPE Next(ULONG count, PITEMID_CHILD* items, ULONG* fetched) override {
    if (!items || (!fetched && count != 1)) return E_POINTER;
    ULONG actual = 0;
    while (actual < count && cursor_ < ids_.size()) {
      auto item = MakeId(ids_[cursor_]); if (!item) return E_OUTOFMEMORY;
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
    if (!result) return E_POINTER; auto clone = new(std::nothrow) Enumerator(ids_);
    if (!clone) return E_OUTOFMEMORY; clone->cursor_ = cursor_; *result = clone; return S_OK;
  }
};

class Folder final : public IShellFolder2, public IPersistFolder2 {
  ULONG refs_ = 1; PIDLIST_ABSOLUTE absolute_ = nullptr; USHORT node_ = 0;
 public:
  Folder() { ++objects; }
  ~Folder() { CoTaskMemFree(absolute_); --objects; }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr;
    Trace("Folder.QueryInterface", iid);
    if (iid == IID_IUnknown || iid == IID_IShellFolder || iid == IID_IShellFolder2) *value = static_cast<IShellFolder2*>(this);
    else if (iid == IID_IPersist || iid == IID_IPersistFolder || iid == IID_IPersistFolder2) *value = static_cast<IPersistFolder2*>(this);
    else return E_NOINTERFACE;
    AddRef(); return S_OK;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++refs_; }
  ULONG STDMETHODCALLTYPE Release() override { auto count = --refs_; if (!count) delete this; return count; }
  HRESULT STDMETHODCALLTYPE GetClassID(CLSID* value) override { if (!value) return E_POINTER; *value = kClsid; return S_OK; }
  HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE value) override {
    Trace("Folder.Initialize");
    if (!value) return E_INVALIDARG;
    auto next = ILCloneFull(value); if (!next) return E_OUTOFMEMORY;
    CoTaskMemFree(absolute_); absolute_ = next;
    node_ = Id(ILFindLastID(value)); return S_OK;
  }
  HRESULT STDMETHODCALLTYPE GetCurFolder(PIDLIST_ABSOLUTE* value) override {
    if (!value) return E_POINTER; *value = ILCloneFull(absolute_); return *value ? S_OK : E_OUTOFMEMORY;
  }
  HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND, IBindCtx*, LPWSTR name, ULONG* eaten, PIDLIST_RELATIVE* value, ULONG* attributes) override {
    if (!name || !value) return E_POINTER; *value = nullptr;
    for (USHORT id = 1; id <= 3; ++id) {
      if (wcscmp(name, Name(id)) == 0) {
        *value = MakeId(id); if (!*value) return E_OUTOFMEMORY;
        if (eaten) *eaten = static_cast<ULONG>(wcslen(name));
        if (attributes) { PCUITEMID_CHILD child = *value; GetAttributesOf(1, &child, attributes); }
        return S_OK;
      }
    }
    return HRESULT_FROM_WIN32(ERROR_FILE_NOT_FOUND);
  }
  HRESULT STDMETHODCALLTYPE EnumObjects(HWND, SHCONTF flags, IEnumIDList** value) override {
    if (!value) return E_POINTER; std::vector<USHORT> ids;
    if (node_ == 0 && (flags & SHCONTF_FOLDERS)) ids.push_back(1);
    if (node_ == 1) {
      if (flags & SHCONTF_NONFOLDERS) ids.push_back(2);
      if (flags & SHCONTF_FOLDERS) ids.push_back(3);
    }
    *value = new(std::nothrow) Enumerator(std::move(ids)); return *value ? S_OK : E_OUTOFMEMORY;
  }
  HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE pidl, IBindCtx*, REFIID iid, void** value) override {
    if (!value) return E_POINTER; *value = nullptr; if (!FolderId(Id(pidl))) return E_NOINTERFACE;
    auto child = new(std::nothrow) Folder(); if (!child) return E_OUTOFMEMORY;
    auto combined = ILCombine(absolute_, pidl); if (!combined) { child->Release(); return E_OUTOFMEMORY; }
    auto hr = child->Initialize(combined); CoTaskMemFree(combined);
    if (SUCCEEDED(hr)) hr = child->QueryInterface(iid, value); child->Release(); return hr;
  }
  HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE, IBindCtx*, REFIID, void** value) override { if (value) *value = nullptr; return E_NOINTERFACE; }
  HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM, PCUIDLIST_RELATIVE a, PCUIDLIST_RELATIVE b) override {
    int result = wcscmp(Name(Id(a)), Name(Id(b))); return MAKE_HRESULT(SEVERITY_SUCCESS, 0, static_cast<USHORT>(static_cast<SHORT>(result < 0 ? -1 : result > 0 ? 1 : 0)));
  }
  HRESULT STDMETHODCALLTYPE CreateViewObject(HWND, REFIID iid, void** value) override {
    Trace("Folder.CreateViewObject", iid);
    if (!value) return E_POINTER; *value = nullptr; if (iid != IID_IShellView) return E_NOINTERFACE;
    SFV_CREATE create{sizeof(create), static_cast<IShellFolder2*>(this), nullptr, nullptr};
    const HRESULT hr = SHCreateShellFolderView(&create, reinterpret_cast<IShellView**>(value));
    Trace("Folder.CreateDefView.result", iid, hr); return hr;
  }
  HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT count, PCUITEMID_CHILD_ARRAY items, SFGAOF* attributes) override {
    if (!attributes) return E_POINTER;
    SFGAOF result = SFGAO_FOLDER | SFGAO_HASSUBFOLDER | SFGAO_BROWSABLE | SFGAO_READONLY;
    for (UINT index = 0; index < count; ++index) {
      result &= FolderId(Id(items[index])) ? SFGAO_FOLDER | SFGAO_HASSUBFOLDER | SFGAO_BROWSABLE | SFGAO_READONLY : SFGAO_READONLY;
    }
    *attributes &= result; return S_OK;
  }
  HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND, UINT, PCUITEMID_CHILD_ARRAY, REFIID iid, UINT*, void** value) override { Trace("Folder.GetUIObjectOf", iid, E_NOINTERFACE); if (value) *value = nullptr; return E_NOINTERFACE; }
  HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD pidl, SHGDNF, STRRET* value) override {
    if (!value) return E_POINTER; value->uType = STRRET_WSTR; return SHStrDupW(Name(Id(pidl)), &value->pOleStr);
  }
  HRESULT STDMETHODCALLTYPE SetNameOf(HWND, PCUITEMID_CHILD, LPCWSTR, SHGDNF, PITEMID_CHILD* value) override { if (value) *value = nullptr; return E_ACCESSDENIED; }
  HRESULT STDMETHODCALLTYPE GetDefaultSearchGUID(GUID*) override { return E_NOTIMPL; }
  HRESULT STDMETHODCALLTYPE EnumSearches(IEnumExtraSearch**) override { return E_NOTIMPL; }
  HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD, ULONG* sort, ULONG* display) override { if (!sort || !display) return E_POINTER; *sort = 0; *display = 0; return S_OK; }
  HRESULT STDMETHODCALLTYPE GetDefaultColumnState(UINT column, SHCOLSTATEF* state) override { if (!state) return E_POINTER; if (column > 1) return E_INVALIDARG; *state = SHCOLSTATE_TYPE_STR | SHCOLSTATE_ONBYDEFAULT; return S_OK; }
  HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD item, const SHCOLUMNID* key, VARIANT* value) override {
    if (!key || !value) return E_POINTER; VariantInit(value);
    const wchar_t* text = IsEqualPropertyKey(*key, PKEY_ItemNameDisplay) ? Name(Id(item)) : FolderId(Id(item)) ? L"真实文件夹（验证样例）" : L"只读文件（验证样例）";
    value->vt = VT_BSTR; value->bstrVal = SysAllocString(text); return value->bstrVal ? S_OK : E_OUTOFMEMORY;
  }
  HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD item, UINT column, SHELLDETAILS* details) override {
    if (!details) return E_POINTER; if (column > 1) return E_INVALIDARG;
    details->fmt = LVCFMT_LEFT; details->cxChar = column == 0 ? 28 : 24; details->str.uType = STRRET_WSTR;
    const wchar_t* text = item ? (column == 0 ? Name(Id(item)) : FolderId(Id(item)) ? L"文件夹" : L"文件") : (column == 0 ? L"名称" : L"类型");
    return SHStrDupW(text, &details->str.pOleStr);
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
    Trace("Factory.CreateInstance", iid);
    if (!value) return E_POINTER; *value = nullptr; if (outer) return CLASS_E_NOAGGREGATION;
    auto folder = new(std::nothrow) Folder(); if (!folder) return E_OUTOFMEMORY;
    auto hr = folder->QueryInterface(iid, value); folder->Release(); return hr;
  }
  HRESULT STDMETHODCALLTYPE LockServer(BOOL value) override { if(value) ++locks; else --locks; return S_OK; }
};
}
__control_entrypoint(DllExport) STDAPI DllCanUnloadNow() { return objects == 0 && locks == 0 ? S_OK : S_FALSE; }
_Check_return_ STDAPI DllGetClassObject(_In_ REFCLSID clsid, _In_ REFIID iid, _Outptr_ void** value) {
  Trace("DllGetClassObject", iid);
  if (!value) return E_POINTER; *value = nullptr; if (clsid != kClsid) return CLASS_E_CLASSNOTAVAILABLE;
  auto factory = new(std::nothrow) Factory(); if (!factory) return E_OUTOFMEMORY;
  auto hr = factory->QueryInterface(iid, value); factory->Release(); return hr;
}
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
  if (reason == DLL_PROCESS_ATTACH) proofModule = module;
  return TRUE;
}
