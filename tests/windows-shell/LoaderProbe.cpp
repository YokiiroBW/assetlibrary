#include <windows.h>
#include <shlobj.h>
#include <cstdio>
#include <cwchar>

namespace {
constexpr CLSID kProofClsid = {0x4ff8301d,0x2e73,0x4d49,{0x9f,0xe5,0x86,0x8d,0x5f,0x1e,0xa3,0x02}};
constexpr wchar_t kRuntimeNames[][20] = {
    L"MSVCP140.dll", L"VCRUNTIME140.dll", L"VCRUNTIME140_1.dll"
};

bool ReportRuntimeOrigins() {
  wchar_t systemDirectory[MAX_PATH]{};
  const UINT length = GetSystemDirectoryW(systemDirectory, MAX_PATH);
  if (length == 0 || length >= MAX_PATH) return false;
  bool valid = true;
  for (const auto name : kRuntimeNames) {
    const HMODULE runtime = GetModuleHandleW(name);
    wchar_t path[MAX_PATH]{};
    const DWORD pathLength = runtime ? GetModuleFileNameW(runtime, path, MAX_PATH) : 0;
    const bool system = pathLength > length && pathLength < MAX_PATH &&
        _wcsnicmp(path, systemDirectory, length) == 0 && path[length] == L'\\' &&
        _wcsicmp(path + length + 1, name) == 0;
    wprintf(L"Runtime.%ls=%ls\n", name, system ? L"System32" : runtime ? L"Other" : L"NotLoaded");
    valid = valid && system;
  }
  return valid;
}

bool TestFactory(HMODULE module) {
  using GetFactory = HRESULT (STDAPICALLTYPE*)(REFCLSID, REFIID, void**);
  const FARPROC address = GetProcAddress(module, "DllGetClassObject");
  GetFactory getFactory = nullptr;
  static_assert(sizeof(getFactory) == sizeof(address));
  // Both are Windows function pointers; copy avoids MSVC's unsafe-cast warning.
  memcpy(&getFactory, &address, sizeof(getFactory));
  if (!getFactory) { wprintf(L"DllGetClassObject=missing\n"); return false; }
  IClassFactory* factory = nullptr;
  HRESULT hr = getFactory(kProofClsid, IID_PPV_ARGS(&factory));
  wprintf(L"DllGetClassObject=%08lx\n", static_cast<unsigned long>(hr));
  if (FAILED(hr) || !factory) return false;
  IShellFolder2* folder = nullptr;
  hr = factory->CreateInstance(nullptr, IID_PPV_ARGS(&folder));
  wprintf(L"CreateInstance=%08lx\n", static_cast<unsigned long>(hr));
  const bool valid = SUCCEEDED(hr) && folder;
  if (folder) folder->Release();
  factory->Release();
  return valid;
}
}

int wmain(int argc, wchar_t** argv) {
  if (argc != 3 || (wcscmp(argv[2], L"default") != 0 && wcscmp(argv[2], L"restricted") != 0)) {
    fwprintf(stderr, L"Usage: ExplorerLoaderProbe <absolute-proof-dll> <default|restricted>\n");
    return 2;
  }
  // Missing dependencies must remain an observable error, never a modal dialog.
  SetErrorMode(SEM_FAILCRITICALERRORS | SEM_NOOPENFILEERRORBOX);
  const bool restricted = wcscmp(argv[2], L"restricted") == 0;
  if (restricted && !SetDefaultDllDirectories(LOAD_LIBRARY_SEARCH_SYSTEM32)) {
    wprintf(L"SetDefaultDllDirectories=%08lx\n", GetLastError()); return 4;
  }
  const HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
  if (FAILED(initialized)) { wprintf(L"CoInitializeEx=%08lx\n", static_cast<unsigned long>(initialized)); return 5; }
  for (const auto name : kRuntimeNames) {
    if (GetModuleHandleW(name)) { wprintf(L"RuntimePreloaded=true\n"); CoUninitialize(); return 3; }
  }
  wprintf(L"RuntimePreloaded=false\n");
  const DWORD flags = restricted ? LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32 : 0;
  const HMODULE module = LoadLibraryExW(argv[1], nullptr, flags);
  const DWORD error = module ? ERROR_SUCCESS : GetLastError();
  wprintf(L"LoadLibrary=%08lx\n", error);
  bool valid = false;
  if (module) {
    const bool origins = ReportRuntimeOrigins();
    valid = TestFactory(module) && origins;
    FreeLibrary(module);
  }
  CoUninitialize();
  return valid ? 0 : 1;
}
