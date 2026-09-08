#include <windows.h>
#include <shlobj.h>
#include <shellapi.h>
#include <exdisp.h>
#include <stdio.h>
#include <vector>
#include <algorithm>

std::vector<SHANDLE_PTR> WindowIds(IShellWindows* windows) {
  std::vector<SHANDLE_PTR> ids; long count = 0;
  if (FAILED(windows->get_Count(&count))) return ids;
  for (long index = 0; index < count; ++index) {
    VARIANT position{}; position.vt = VT_I4; position.lVal = index;
    IDispatch* dispatch = nullptr;
    if (FAILED(windows->Item(position, &dispatch)) || !dispatch) continue;
    IWebBrowser2* browser = nullptr;
    if (SUCCEEDED(dispatch->QueryInterface(IID_PPV_ARGS(&browser)))) {
      SHANDLE_PTR id = 0; if (SUCCEEDED(browser->get_HWND(&id))) ids.push_back(id);
      browser->Release();
    }
    dispatch->Release();
  }
  return ids;
}

HRESULT NavigateNewProofWindow(PCIDLIST_ABSOLUTE root) {
  IShellWindows* windows = nullptr;
  HRESULT hr = CoCreateInstance(CLSID_ShellWindows, nullptr, CLSCTX_LOCAL_SERVER, IID_PPV_ARGS(&windows));
  if (FAILED(hr)) return hr;
  const auto before = WindowIds(windows);
  SHELLEXECUTEINFOW launch{sizeof(launch)};
  launch.fMask = SEE_MASK_NOCLOSEPROCESS; launch.lpFile = L"explorer.exe";
  launch.lpParameters = L"/n,shell:desktop"; launch.nShow = SW_SHOWNORMAL;
  if (!ShellExecuteExW(&launch)) { windows->Release(); return HRESULT_FROM_WIN32(GetLastError()); }
  if (launch.hProcess) CloseHandle(launch.hProcess);
  IWebBrowser2* target = nullptr;
  const ULONGLONG deadline = GetTickCount64() + 5000;
  while (!target && GetTickCount64() < deadline) {
    Sleep(100); long count = 0;
    if (FAILED(windows->get_Count(&count))) continue;
    for (long index = 0; index < count; ++index) {
      VARIANT position{}; position.vt = VT_I4; position.lVal = index;
      IDispatch* dispatch = nullptr;
      if (FAILED(windows->Item(position, &dispatch)) || !dispatch) continue;
      IWebBrowser2* candidate = nullptr;
      if (SUCCEEDED(dispatch->QueryInterface(IID_PPV_ARGS(&candidate)))) {
        SHANDLE_PTR id = 0; BSTR location = nullptr;
        const bool identified = SUCCEEDED(candidate->get_HWND(&id)) && SUCCEEDED(candidate->get_LocationName(&location));
        const bool isDesktop = location && (wcscmp(location, L"桌面") == 0 || wcscmp(location, L"Desktop") == 0);
        if (identified && isDesktop && std::find(before.begin(), before.end(), id) == before.end()) {
          target = candidate; wprintf(L"NewProofWindow=%llu\n", static_cast<unsigned long long>(id));
        } else candidate->Release();
        if (location) SysFreeString(location);
      }
      dispatch->Release(); if (target) break;
    }
  }
  windows->Release();
  if (!target) return HRESULT_FROM_WIN32(ERROR_TIMEOUT);
  SAFEARRAY* bytes = SafeArrayCreateVector(VT_UI1, 0, ILGetSize(root));
  if (!bytes) { target->Release(); return E_OUTOFMEMORY; }
  void* data = nullptr; hr = SafeArrayAccessData(bytes, &data);
  if (SUCCEEDED(hr)) { memcpy(data, root, ILGetSize(root)); hr = SafeArrayUnaccessData(bytes); }
  if (SUCCEEDED(hr)) {
    VARIANT location{}; location.vt = VT_ARRAY | VT_UI1; location.parray = bytes;
    VARIANT optional{};
    hr = target->Navigate2(&location, &optional, &optional, &optional, &optional);
    wprintf(L"Navigate2Pidl=%08lx\n", static_cast<unsigned long>(hr));
  }
  const HRESULT cleared = SafeArrayDestroy(bytes); (void)cleared;
  target->Release(); return hr;
}
