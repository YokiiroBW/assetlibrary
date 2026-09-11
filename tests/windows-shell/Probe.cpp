#include <windows.h>
#include <shlobj.h>
#include <stdio.h>
#include <shlwapi.h>
HRESULT NavigateNewProofWindow(PCIDLIST_ABSOLUTE root);

static int RootBindProof() {
  // Preserve the last completed stage if the outer ten-second runner must terminate COM.
  setvbuf(stdout, nullptr, _IONBF, 0);
  PIDLIST_ABSOLUTE root = nullptr;
  HRESULT hr = SHParseDisplayName(L"::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}", nullptr, &root, 0, nullptr);
  wprintf(L"RootParse=%08lx; Pidl=%ls\n", static_cast<unsigned long>(hr), root ? L"present" : L"absent");
  if (FAILED(hr) || !root) { CoTaskMemFree(root); return 1; }
  IShellFolder* desktop = nullptr;
  hr = SHGetDesktopFolder(&desktop);
  wprintf(L"GetDesktopFolder=%08lx\n", static_cast<unsigned long>(hr));
  IShellFolder2* folder = nullptr;
  if (SUCCEEDED(hr) && desktop) {
    hr = desktop->BindToObject(root, nullptr, IID_PPV_ARGS(&folder));
    wprintf(L"DesktopBindRoot=%08lx\n", static_cast<unsigned long>(hr));
  }
  if (SUCCEEDED(hr) && folder) {
    IShellView* view = nullptr;
    hr = folder->CreateViewObject(nullptr, IID_PPV_ARGS(&view));
    if (SUCCEEDED(hr) && !view) hr = E_UNEXPECTED;
    wprintf(L"RootBoundCreateView=%08lx\n", static_cast<unsigned long>(hr));
    if (view) view->Release();
  } else if (SUCCEEDED(hr)) hr = E_UNEXPECTED;
  if (folder) folder->Release();
  if (desktop) desktop->Release();
  CoTaskMemFree(root);
  return FAILED(hr) ? 1 : 0;
}

int wmain(int argc, wchar_t** argv) {
  HRESULT hr = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
  if (FAILED(hr)) return 1;
  if (argc == 2 && wcscmp(argv[1], L"--root-bind") == 0) {
    const int result = RootBindProof(); CoUninitialize(); return result;
  }
  CLSID clsid{}; hr = CLSIDFromString(L"{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}", &clsid);
  if (FAILED(hr)) { CoUninitialize(); return 1; }
  IShellFolder2* folder = nullptr;
  hr = CoCreateInstance(clsid, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&folder));
  wprintf(L"CoCreateInstance=%08lx\n", static_cast<unsigned long>(hr));
  PIDLIST_ABSOLUTE root = nullptr;
  HRESULT parsed = SHParseDisplayName(L"::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}", nullptr, &root, 0, nullptr);
  wprintf(L"SHParseDisplayName=%08lx\n", static_cast<unsigned long>(parsed));
  if (FAILED(parsed) || !root) {
    CoTaskMemFree(root); if (folder) folder->Release(); CoUninitialize(); return 1;
  }
  if (root) {
    IShellItem* rootItem = nullptr;
    HRESULT itemHr = SHCreateItemFromIDList(root, IID_PPV_ARGS(&rootItem));
    if (SUCCEEDED(itemHr)) {
      IQueryAssociations* associations = nullptr;
      itemHr = rootItem->BindToHandler(nullptr, BHID_AssociationArray, IID_PPV_ARGS(&associations));
      wprintf(L"RootAssociationArray=%08lx\n", static_cast<unsigned long>(itemHr));
      if (associations) {
        wchar_t command[1024]{}; DWORD characters = 1024;
        HRESULT commandHr = associations->GetString(ASSOCF_NONE, ASSOCSTR_COMMAND, L"open", command, &characters);
        wprintf(L"RootOpenAssociation=%08lx; Length=%lu\n", static_cast<unsigned long>(commandHr), characters);
        associations->Release();
      }
      rootItem->Release();
    }
  }
  IShellFolder* desktop = nullptr;
  HRESULT desktopHr = SHGetDesktopFolder(&desktop);
  bool found = false;
  if (SUCCEEDED(desktopHr)) {
    SFGAOF attributes = SFGAO_FOLDER | SFGAO_BROWSABLE | SFGAO_HASSUBFOLDER;
    PCUITEMID_CHILD rootChild = root;
    HRESULT attributeHr = desktop->GetAttributesOf(1, &rootChild, &attributes);
    wprintf(L"RootAttributes=%08lx; Result=%08lx\n", attributes, static_cast<unsigned long>(attributeHr));
    IEnumIDList* desktopItems = nullptr;
    desktopHr = desktop->EnumObjects(nullptr, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS | SHCONTF_INCLUDEHIDDEN, &desktopItems);
    if (SUCCEEDED(desktopHr) && desktopItems) {
      PITEMID_CHILD entry = nullptr;
      for (unsigned int index = 0; index < 1000 && desktopItems->Next(1, &entry, nullptr) == S_OK; ++index) {
        if (root && ILIsEqual(entry, root)) found = true;
        CoTaskMemFree(entry); entry = nullptr;
      }
      desktopItems->Release();
    }
    desktop->Release();
  }
  wprintf(L"DesktopEnumeratesRoot=%ls; Result=%08lx\n", found ? L"true" : L"false", static_cast<unsigned long>(desktopHr));
  if (folder && root) {
    IPersistFolder2* persist = nullptr;
    hr = folder->QueryInterface(IID_PPV_ARGS(&persist));
    if (SUCCEEDED(hr)) { hr = persist->Initialize(root); persist->Release(); }
    wprintf(L"Initialize=%08lx\n", static_cast<unsigned long>(hr));
    IShellView* view = nullptr;
    hr = folder->CreateViewObject(nullptr, IID_PPV_ARGS(&view));
    wprintf(L"CreateDefView=%08lx\n", static_cast<unsigned long>(hr));
    if (view) view->Release();
    IEnumIDList* entries = nullptr;
    hr = folder->EnumObjects(nullptr, SHCONTF_FOLDERS | SHCONTF_NONFOLDERS, &entries);
    PITEMID_CHILD item = nullptr; ULONG fetched = 0;
    if (SUCCEEDED(hr)) { hr = entries->Next(1, &item, &fetched); entries->Release(); }
    wprintf(L"FirstChild=%08lx; Count=%lu\n", static_cast<unsigned long>(hr), fetched);
    if (item) {
      IShellFolder2* child = nullptr;
      PCUITEMID_CHILD raw=item;SFGAOF attributes=SFGAO_FOLDER|SFGAO_READONLY;
      hr=folder->GetAttributesOf(1,&raw,&attributes);
      if(SUCCEEDED(hr)&&!(attributes&SFGAO_READONLY))hr=E_UNEXPECTED;
      if(SUCCEEDED(hr)&&(attributes&SFGAO_FOLDER))hr = folder->BindToObject(item, nullptr, IID_PPV_ARGS(&child));
      wprintf(L"BindChild=%08lx\n", static_cast<unsigned long>(hr));
      if (child) child->Release();
    }
    wprintf(L"ReadOnlyPageValidated=%ls\n", SUCCEEDED(hr) ? L"true" : L"false");
    if (argc == 2 && wcscmp(argv[1], L"--open") == 0) {
      SHChangeNotify(SHCNE_MKDIR, SHCNF_IDLIST | SHCNF_FLUSHNOWAIT, root, nullptr);
      PCUITEMID_CHILD selected = item;
      hr = SHOpenFolderAndSelectItems(root, item ? 1 : 0, item ? &selected : nullptr, 0);
      wprintf(L"OpenFolderByPidl=%08lx\n", static_cast<unsigned long>(hr));
    }
    if (argc == 2 && wcscmp(argv[1], L"--navigate") == 0) {
      hr = NavigateNewProofWindow(root);
      wprintf(L"DirectNavigation=%08lx\n", static_cast<unsigned long>(hr));
    }
    if (item) CoTaskMemFree(item);
  }
  if (root) CoTaskMemFree(root);
  if (folder) folder->Release();
  CoUninitialize(); return FAILED(hr) || FAILED(parsed) ? 1 : 0;
}
