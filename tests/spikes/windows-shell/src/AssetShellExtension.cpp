#include "AssetShellProtocol.h"

#include <winrt/base.h>
#include <windows.h>
#include <shlobj.h>
#include <shobjidl.h>

#include <algorithm>
#include <atomic>
#include <cwchar>
#include <cstring>
#include <memory>
#include <new>
#include <string>
#include <system_error>
#include <thread>

namespace {
using assetlibrary::m0002::FrameHeader;
using assetlibrary::m0002::kClientTimeoutMs;
using assetlibrary::m0002::kMagic;
using assetlibrary::m0002::kMaxPayloadBytes;
using assetlibrary::m0002::kMessageViewPing;
using assetlibrary::m0002::kMessageViewPong;
using assetlibrary::m0002::kPipeName;
using assetlibrary::m0002::kVersion;

// M0-002 owns this CLSID only under the current user's registry hive.
constexpr GUID kClsid = {0x9d52b2f8, 0x9ef4, 0x4f4c, {0x9c, 0x1a, 0x52, 0x9f, 0x66, 0x5f, 0x0a, 0x02}};
std::atomic_ulong g_object_count{0};
std::atomic_ulong g_server_lock_count{0};
HMODULE g_module = nullptr;
constexpr UINT WM_ASSET_HOST_RESULT = WM_APP + 42;

struct HostPingState {
  explicit HostPingState(HWND target) : window(target) {}
  HWND window;
};

ULONGLONG RemainingBudget(ULONGLONG deadline) {
  const ULONGLONG now = GetTickCount64();
  return now >= deadline ? 0 : deadline - now;
}

bool TransferBounded(HANDLE pipe, void* buffer, DWORD length, bool write, ULONGLONG deadline) {
  if (length == 0) return true;
  OVERLAPPED overlapped{};
  overlapped.hEvent = CreateEventW(nullptr, TRUE, FALSE, nullptr);
  if (!overlapped.hEvent) return false;
  DWORD completed = 0;
  const BOOL started = write
      ? WriteFile(pipe, buffer, length, &completed, &overlapped)
      : ReadFile(pipe, buffer, length, &completed, &overlapped);
  if (!started && GetLastError() != ERROR_IO_PENDING) {
    CloseHandle(overlapped.hEvent);
    return false;
  }
  if (!started) {
    const DWORD remaining = static_cast<DWORD>(std::min<ULONGLONG>(RemainingBudget(deadline), INFINITE - 1));
    if (WaitForSingleObject(overlapped.hEvent, remaining) != WAIT_OBJECT_0) {
      // Cancellation completion must be observed before the OVERLAPPED, event, or buffer go out of scope.
      CancelIoEx(pipe, &overlapped);
      WaitForSingleObject(overlapped.hEvent, INFINITE);
      DWORD cancelled_bytes = 0;
      GetOverlappedResult(pipe, &overlapped, &cancelled_bytes, FALSE);
      CloseHandle(overlapped.hEvent);
      return false;
    }
  }
  const BOOL finished = started || GetOverlappedResult(pipe, &overlapped, &completed, FALSE);
  CloseHandle(overlapped.hEvent);
  return finished && completed == length;
}

bool WriteExact(HANDLE pipe, void* buffer, DWORD length, ULONGLONG deadline) {
  return TransferBounded(pipe, buffer, length, true, deadline);
}

bool ReadExact(HANDLE pipe, void* buffer, DWORD length, ULONGLONG deadline) {
  return TransferBounded(pipe, buffer, length, false, deadline);
}

// explorer.exe must never inherit an unbounded wait from a missing or wedged host.
bool WriteFrame(HANDLE pipe, FrameHeader* header, const void* payload, ULONGLONG deadline) {
  if (!WriteExact(pipe, header, sizeof(*header), deadline)) return false;
  if (header->payload_length == 0) return true;
  return WriteExact(pipe, const_cast<void*>(payload), header->payload_length, deadline);
}

bool ReadFrame(HANDLE pipe, FrameHeader* header, ULONGLONG deadline) {
  if (!ReadExact(pipe, header, sizeof(*header), deadline)) return false;
  return header->payload_length <= kMaxPayloadBytes;
}

bool AskAssetHost() {
  const ULONGLONG deadline = GetTickCount64() + kClientTimeoutMs;
  const DWORD wait_ms = static_cast<DWORD>(std::min<ULONGLONG>(RemainingBudget(deadline), INFINITE - 1));
  if (WaitNamedPipeW(kPipeName, wait_ms) == FALSE) return false;
  HANDLE pipe = CreateFileW(kPipeName, GENERIC_READ | GENERIC_WRITE, 0, nullptr, OPEN_EXISTING,
                            FILE_FLAG_OVERLAPPED | SECURITY_SQOS_PRESENT | SECURITY_ANONYMOUS, nullptr);
  if (pipe == INVALID_HANDLE_VALUE) return false;
  constexpr char request_payload[] = "view-ping";
  FrameHeader request{kMagic, kVersion, kMessageViewPing,
                      static_cast<std::uint32_t>(sizeof(request_payload) - 1), 1};
  bool ok = WriteFrame(pipe, &request, request_payload, deadline);
  if (ok) {
    FrameHeader response{};
    ok = ReadFrame(pipe, &response, deadline);
    ok = ok && response.magic == kMagic && response.version == kVersion &&
         response.message_type == kMessageViewPong && response.request_id == request.request_id &&
         response.payload_length <= kMaxPayloadBytes;
    if (ok && response.payload_length != 0) {
      std::string payload(response.payload_length, '\0');
      ok = ReadExact(pipe, payload.data(), response.payload_length, deadline);
    }
  }
  CloseHandle(pipe);
  return ok;
}

void StartAssetHostPing(HWND window) {
  // The Shell UI thread only starts this worker and returns. The worker owns its
  // state until the logical transaction finishes and any cancelled I/O drains.
  auto state = std::make_shared<HostPingState>(window);
  ++g_object_count; // Keep the DLL loaded while detached worker code is executing.
  try {
    std::thread([state]() {
      const bool ready = AskAssetHost();
      PostMessageW(state->window, WM_ASSET_HOST_RESULT, ready ? 1 : 0, 0);
      --g_object_count;
    }).detach();
  } catch (const std::system_error&) {
    --g_object_count;
    PostMessageW(window, WM_ASSET_HOST_RESULT, 0, 0);
  }
}

class AssetShellView final : public IShellView {
 public:
  AssetShellView() : ref_count_(1), browser_(nullptr), window_(nullptr) { ++g_object_count; }
  ~AssetShellView() {
    if (browser_) browser_->Release();
    --g_object_count;
  }

  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
    if (!object) return E_POINTER;
    *object = nullptr;
    if (iid == IID_IUnknown || iid == IID_IShellView || iid == IID_IOleWindow) {
      *object = static_cast<IShellView*>(this);
      AddRef();
      return S_OK;
    }
    return E_NOINTERFACE;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++ref_count_; }
  ULONG STDMETHODCALLTYPE Release() override {
    const ULONG value = --ref_count_;
    if (value == 0) delete this;
    return value;
  }
  HRESULT STDMETHODCALLTYPE GetWindow(HWND* window) override {
    if (!window) return E_POINTER;
    *window = window_;
    return window_ ? S_OK : E_UNEXPECTED;
  }
  HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override { return E_NOTIMPL; }
  HRESULT STDMETHODCALLTYPE TranslateAccelerator(MSG*) override { return S_FALSE; }
  HRESULT STDMETHODCALLTYPE EnableModeless(BOOL) override { return S_OK; }
  HRESULT STDMETHODCALLTYPE UIActivate(UINT) override { return S_OK; }
  HRESULT STDMETHODCALLTYPE Refresh() override {
    if (window_) InvalidateRect(window_, nullptr, TRUE);
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE CreateViewWindow(IShellView*, LPCFOLDERSETTINGS settings,
                                              IShellBrowser* browser, RECT* rect, HWND* window) override {
    if (!browser || !rect || !window) return E_POINTER;
    *window = nullptr;
    if (browser_) browser_->Release();
    browser_ = browser;
    browser_->AddRef();
    browser_->GetWindow(&browser_window_);
    if (settings) settings_ = *settings;

    WNDCLASSW klass{};
    klass.lpfnWndProc = &AssetShellView::WindowProc;
    klass.hInstance = g_module;
    klass.lpszClassName = L"AssetLibraryM0002ShellView";
    RegisterClassW(&klass);
    window_ = CreateWindowExW(0, klass.lpszClassName, L"AssetLibrary", WS_CHILD | WS_VISIBLE,
                              rect->left, rect->top, rect->right - rect->left, rect->bottom - rect->top,
                              browser_window_, nullptr, klass.hInstance, this);
    if (!window_) return HRESULT_FROM_WIN32(GetLastError());
    *window = window_;
    SetStatusText(L"AssetHost connecting...");
    StartAssetHostPing(window_);
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE DestroyViewWindow() override {
    if (window_) {
      DestroyWindow(window_);
      window_ = nullptr;
    }
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE GetCurrentInfo(LPFOLDERSETTINGS settings) override {
    if (!settings) return E_POINTER;
    *settings = settings_;
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE AddPropertySheetPages(DWORD, LPFNSVADDPROPSHEETPAGE, LPARAM) override { return E_NOTIMPL; }
  HRESULT STDMETHODCALLTYPE SaveViewState() override { return S_OK; }
  HRESULT STDMETHODCALLTYPE SelectItem(PCUITEMID_CHILD, UINT) override { return S_OK; }
  HRESULT STDMETHODCALLTYPE GetItemObject(UINT, REFIID, void**) override { return E_NOINTERFACE; }

 private:
  static LRESULT CALLBACK WindowProc(HWND window, UINT message, WPARAM wparam, LPARAM lparam) {
    AssetShellView* view = reinterpret_cast<AssetShellView*>(GetWindowLongPtrW(window, GWLP_USERDATA));
    if (message == WM_NCCREATE) {
      auto* create = reinterpret_cast<CREATESTRUCTW*>(lparam);
      view = static_cast<AssetShellView*>(create->lpCreateParams);
      SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(view));
    }
    if (message == WM_ASSET_HOST_RESULT && view) {
      view->SetStatusText(wparam != 0
          ? L"AssetHost connected"
          : L"AssetHost unavailable; retry from the independent client");
      InvalidateRect(window, nullptr, TRUE);
      return 0;
    }
    if (message == WM_PAINT) {
      PAINTSTRUCT paint{};
      HDC dc = BeginPaint(window, &paint);
      const wchar_t* text = L"AssetLibrary M0-002\nShell bridge active; heavy view remains out of process.";
      RECT client{};
      GetClientRect(window, &client);
      DrawTextW(dc, text, -1, &client, DT_LEFT | DT_TOP | DT_NOPREFIX);
      EndPaint(window, &paint);
      return 0;
    }
    return DefWindowProcW(window, message, wparam, lparam);
  }

  void SetStatusText(const wchar_t* text) {
    if (browser_) browser_->SetStatusTextSB(text);
  }

  std::atomic_ulong ref_count_;
  IShellBrowser* browser_;
  HWND browser_window_ = nullptr;
  HWND window_;
  FOLDERSETTINGS settings_{};
};

class OneItemEnumerator final : public IEnumIDList {
 public:
  OneItemEnumerator() : ref_count_(1), returned_(false) { ++g_object_count; }
  ~OneItemEnumerator() { --g_object_count; }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
    if (!object) return E_POINTER;
    *object = nullptr;
    if (iid == IID_IUnknown || iid == IID_IEnumIDList) {
      *object = static_cast<IEnumIDList*>(this); AddRef(); return S_OK;
    }
    return E_NOINTERFACE;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++ref_count_; }
  ULONG STDMETHODCALLTYPE Release() override { ULONG value = --ref_count_; if (!value) delete this; return value; }
  HRESULT STDMETHODCALLTYPE Next(ULONG count, PITEMID_CHILD* items, ULONG* fetched) override {
    if (!items || (count != 1 && !fetched)) return E_POINTER;
    if (fetched) *fetched = 0;
    if (returned_ || count == 0) return S_FALSE;
    auto* item = static_cast<ITEMIDLIST*>(CoTaskMemAlloc(6));
    if (!item) return E_OUTOFMEMORY;
    item->mkid.cb = 4;
    item->mkid.abID[0] = 0;
    item->mkid.abID[1] = 0;
    *reinterpret_cast<USHORT*>(reinterpret_cast<BYTE*>(item) + 4) = 0;
    items[0] = item;
    returned_ = true;
    if (fetched) *fetched = 1;
    return count == 1 ? S_OK : S_FALSE;
  }
  HRESULT STDMETHODCALLTYPE Skip(ULONG count) override {
    if (count == 0) return S_OK;
    if (returned_) return S_FALSE;
    returned_ = true;
    return count == 1 ? S_OK : S_FALSE;
  }
  HRESULT STDMETHODCALLTYPE Reset() override { returned_ = false; return S_OK; }
  HRESULT STDMETHODCALLTYPE Clone(IEnumIDList** clone) override {
    if (!clone) return E_POINTER;
    auto* copy = new (std::nothrow) OneItemEnumerator();
    if (!copy) return E_OUTOFMEMORY;
    copy->returned_ = returned_; *clone = copy; return S_OK;
  }
 private:
  std::atomic_ulong ref_count_;
  bool returned_;
};

class AssetShellFolder final : public IShellFolder, public IPersistFolder {
 public:
  AssetShellFolder() : ref_count_(1) { ++g_object_count; }
  ~AssetShellFolder() {
    CoTaskMemFree(folder_pidl_);
    --g_object_count;
  }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
    if (!object) return E_POINTER;
    *object = nullptr;
    if (iid == IID_IUnknown || iid == IID_IShellFolder) {
      *object = static_cast<IShellFolder*>(this); AddRef(); return S_OK;
    }
    if (iid == IID_IPersist || iid == IID_IPersistFolder) {
      *object = static_cast<IPersistFolder*>(this); AddRef(); return S_OK;
    }
    return E_NOINTERFACE;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++ref_count_; }
  ULONG STDMETHODCALLTYPE Release() override { ULONG value = --ref_count_; if (!value) delete this; return value; }
  HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND, IBindCtx*, LPOLESTR name, ULONG* eaten,
                                             PIDLIST_RELATIVE* pidl, ULONG* attributes) override {
    if (!name || !pidl) return E_POINTER;
    *pidl = nullptr; if (eaten) *eaten = static_cast<ULONG>(wcslen(name)); if (attributes) *attributes = 0;
    auto* item = static_cast<ITEMIDLIST*>(CoTaskMemAlloc(6));
    if (!item) return E_OUTOFMEMORY;
    item->mkid.cb = 4; item->mkid.abID[0] = 0; item->mkid.abID[1] = 0;
    *reinterpret_cast<USHORT*>(reinterpret_cast<BYTE*>(item) + 4) = 0;
    *pidl = item; return S_OK;
  }
  HRESULT STDMETHODCALLTYPE EnumObjects(HWND, SHCONTF, IEnumIDList** enumerator) override {
    if (!enumerator) return E_POINTER; *enumerator = new (std::nothrow) OneItemEnumerator();
    return *enumerator ? S_OK : E_OUTOFMEMORY;
  }
  HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE, IBindCtx*, REFIID iid, void** object) override {
    if (!object) return E_POINTER;
    *object = nullptr; if (iid != IID_IShellFolder && iid != IID_IUnknown) return E_NOINTERFACE;
    auto* folder = new (std::nothrow) AssetShellFolder();
    if (!folder) return E_OUTOFMEMORY;
    HRESULT result = folder->QueryInterface(iid, object);
    folder->Release();
    return result;
  }
  HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE, IBindCtx*, REFIID, void**) override { return E_NOTIMPL; }
  HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM, PCUIDLIST_RELATIVE, PCUIDLIST_RELATIVE) override {
    return MAKE_HRESULT(SEVERITY_SUCCESS, 0, 0);
  }
  HRESULT STDMETHODCALLTYPE CreateViewObject(HWND, REFIID iid, void** object) override {
    if (!object) return E_POINTER;
    *object = nullptr; auto* view = new (std::nothrow) AssetShellView();
    if (!view) return E_OUTOFMEMORY; HRESULT result = view->QueryInterface(iid, object); view->Release(); return result;
  }
  HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT, PCUITEMID_CHILD_ARRAY, SFGAOF* attributes) override {
    if (!attributes) return E_POINTER;
    *attributes = SFGAO_FOLDER;
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE GetUIObjectOf(
      HWND, UINT, PCUITEMID_CHILD_ARRAY, REFIID, UINT*, void** object) override {
    if (!object) return E_POINTER;
    *object = nullptr;
    return E_NOINTERFACE;
  }
  HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD, SHGDNF flags, STRRET* name) override {
    if (!name) return E_POINTER; name->uType = STRRET_WSTR;
    const wchar_t* value = (flags & SHGDNF_FORPARSING) ? L"AssetHost" : L"AssetHost (M0-002)";
    const size_t bytes = (wcslen(value) + 1) * sizeof(wchar_t);
    name->pOleStr = static_cast<LPOLESTR>(CoTaskMemAlloc(bytes));
    if (!name->pOleStr) return E_OUTOFMEMORY; memcpy(name->pOleStr, value, bytes); return S_OK;
  }
  HRESULT STDMETHODCALLTYPE SetNameOf(
      HWND, PCUITEMID_CHILD, LPCWSTR, SHGDNF, PITEMID_CHILD*) override {
    return E_ACCESSDENIED;
  }

  HRESULT STDMETHODCALLTYPE GetClassID(CLSID* clsid) override {
    if (!clsid) return E_POINTER;
    *clsid = kClsid;
    return S_OK;
  }
  HRESULT STDMETHODCALLTYPE Initialize(PCIDLIST_ABSOLUTE folder) override {
    PIDLIST_ABSOLUTE copy = folder ? ILCloneFull(folder) : nullptr;
    if (folder && !copy) return E_OUTOFMEMORY;
    CoTaskMemFree(folder_pidl_);
    folder_pidl_ = copy;
    return S_OK;
  }
 private:
  std::atomic_ulong ref_count_;
  PIDLIST_ABSOLUTE folder_pidl_ = nullptr;
};

class ClassFactory final : public IClassFactory {
 public:
  ClassFactory() : ref_count_(1) { ++g_object_count; }
  ~ClassFactory() { --g_object_count; }
  HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** object) override {
    if (!object) return E_POINTER; *object = nullptr;
    if (iid == IID_IUnknown || iid == IID_IClassFactory) {
      *object = static_cast<IClassFactory*>(this);
      AddRef();
      return S_OK;
    }
    return E_NOINTERFACE;
  }
  ULONG STDMETHODCALLTYPE AddRef() override { return ++ref_count_; }
  ULONG STDMETHODCALLTYPE Release() override { ULONG value = --ref_count_; if (!value) delete this; return value; }
  HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** object) override {
    if (outer) return CLASS_E_NOAGGREGATION; auto* folder = new (std::nothrow) AssetShellFolder();
    if (!folder) return E_OUTOFMEMORY;
    HRESULT result = folder->QueryInterface(iid, object);
    folder->Release();
    return result;
  }
  HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override {
    if (lock) {
      ++g_server_lock_count;
    } else if (g_server_lock_count != 0) {
      --g_server_lock_count;
    }
    return S_OK;
  }
 private:
  std::atomic_ulong ref_count_;
};
} // namespace

extern "C" HRESULT __declspec(dllexport) DllGetClassObject(REFCLSID clsid, REFIID iid, void** object) {
  if (!object) return E_POINTER; *object = nullptr;
  if (clsid != kClsid) return CLASS_E_CLASSNOTAVAILABLE;
  auto* factory = new (std::nothrow) ClassFactory();
  if (!factory) return E_OUTOFMEMORY;
  HRESULT result = factory->QueryInterface(iid, object);
  factory->Release();
  return result;
}

extern "C" HRESULT __declspec(dllexport) DllCanUnloadNow() {
  return g_object_count == 0 && g_server_lock_count == 0 ? S_OK : S_FALSE;
}

BOOL APIENTRY DllMain(HMODULE module, DWORD reason, LPVOID) {
  if (reason == DLL_PROCESS_ATTACH) {
    g_module = module;
    DisableThreadLibraryCalls(g_module);
  }
  return TRUE;
}
