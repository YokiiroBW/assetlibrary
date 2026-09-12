#include "SnapshotTestSupport.h"
#include "ProductIdentity.h"
#include "SettingsCommand.h"
#include <shellapi.h>
#include <sddl.h>

namespace {
using proof::Check;
void Identity(const wchar_t* path,const CLSID& accepted,const CLSID& rejected,bool production){
    proof::Library library(path,accepted);
    auto root=library.Root();proof::Com<IPersist> persist;
    Check(root.value->QueryInterface(IID_PPV_ARGS(&persist.value))==S_OK,"persist identity");
    CLSID actual{};Check(persist.value->GetClassID(&actual)==S_OK&&actual==accepted,"class identity");
    HMODULE module=GetModuleHandleW(path);Check(module!=nullptr,"loaded module");
    using GetFactory=HRESULT(STDAPICALLTYPE*)(REFCLSID,REFIID,void**);
    auto get=reinterpret_cast<GetFactory>(GetProcAddress(module,"DllGetClassObject"));
    proof::Com<IClassFactory> wrong;
    Check(get(rejected,IID_PPV_ARGS(&wrong.value))==CLASS_E_CLASSNOTAVAILABLE&&!wrong.value,"opposite identity rejected");
    snapshot::Entry entry{{101},{102},snapshot::Kind::Directory,L"folder"};
    proof::Item item(snapshot::MakePidl(entry));STRRET name{};
    Check(root.value->GetDisplayNameOf(item.value,SHGDN_FORPARSING,&name)==S_OK,"absolute parsing name");
    const std::wstring expected=production?product::ParsingRoot:L"::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}";
    Check(name.uType==STRRET_WSTR&&std::wstring(name.pOleStr).find(expected+L"\\snapshot-pidl-v1:")==0,"matching parsing root");CoTaskMemFree(name.pOleStr);
    proof::Com<IContextMenu> menu;
    const auto hr=root.value->CreateViewObject(nullptr,IID_PPV_ARGS(&menu.value));
    Check(production?hr==S_OK:hr==E_NOINTERFACE,"production-only settings menu");
    if(!production)return;
    name=STRRET{};
    if(root.value->GetDisplayNameOf(nullptr,SHGDN_NORMAL,&name)!=S_OK)throw std::runtime_error("product root title");
    Check(name.uType==STRRET_WSTR&&name.pOleStr&&wcscmp(name.pOleStr,L"资产库")==0,"product root title");CoTaskMemFree(name.pOleStr);
    HMENU popup=CreatePopupMenu();if(!popup)throw std::runtime_error("menu");
    Check(HRESULT_CODE(menu.value->QueryContextMenu(popup,0,19,19,CMF_DEFAULTONLY))==0&&GetMenuItemCount(popup)==0,"settings never default");
    Check(menu.value->QueryContextMenu(popup,0,20,19,CMF_NORMAL)==E_INVALIDARG,"menu command range");
    Check(HRESULT_CODE(menu.value->QueryContextMenu(popup,0,19,19,CMF_NORMAL))==1&&GetMenuItemCount(popup)==1,"one explicit settings action");
    wchar_t label[32]{};Check(GetMenuStringW(popup,19,label,32,MF_BYCOMMAND)>0&&wcscmp(label,L"连接设置(&S)")==0,"settings label");
    Check(GetMenuDefaultItem(popup,FALSE,0)==static_cast<UINT>(-1),"not default");DestroyMenu(popup);
    char verb[32]{};Check(menu.value->GetCommandString(0,GCS_VERBA,nullptr,verb,32)==S_OK&&strcmp(verb,"assetlibrary.settings")==0,"settings canonical verb");
    Check(FAILED(menu.value->GetCommandString(0,GCS_VERBA,nullptr,verb,2)),"bounded settings verb");
    CMINVOKECOMMANDINFO denied{};denied.cbSize=sizeof(denied);denied.lpVerb="open";
    Check(FAILED(menu.value->InvokeCommand(&denied)),"menu cannot open content");
    denied.lpVerb="assetlibrary.settings.extra";Check(FAILED(menu.value->InvokeCommand(&denied)),"unknown menu verb rejected");
    root.value->Release();root.value=nullptr;persist.value->Release();persist.value=nullptr;
    Check(library.canUnload()==S_FALSE,"settings menu keeps DLL owner alive");
    menu.value->Release();menu.value=nullptr;Check(library.canUnload()==S_OK,"settings owner released");
}
void PipeIdentity(){
    HANDLE token=nullptr;Check(OpenProcessToken(GetCurrentProcess(),TOKEN_QUERY,&token)!=FALSE,"TokenUser query");
    DWORD needed=0;GetTokenInformation(token,TokenUser,nullptr,0,&needed);
    std::vector<BYTE> bytes(needed);Check(GetTokenInformation(token,TokenUser,bytes.data(),needed,&needed)!=FALSE,"TokenUser");CloseHandle(token);
    LPWSTR sid=nullptr;Check(ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(bytes.data())->User.Sid,&sid)!=FALSE,"TokenUser SID");
    DWORD session=0;Check(ProcessIdToSessionId(GetCurrentProcessId(),&session)!=FALSE,"current session");
    std::wstring expected=L"\\\\.\\pipe\\AssetLibrary.Explorer.v1.";expected+=sid;LocalFree(sid);expected+=L"."+std::to_wstring(session);
    Check(snapshot::PipeName()==expected,"production pipe uses actual user/session");
}
void Commands(){
    settings::Command command;
    Check(settings::BuildCommand(L"C:\\Program Files\\资产库\\versions\\0.3.0-preview.1\\AssetLibrary.Explorer.dll",command)==S_OK,"settings path with spaces and Unicode");
    Check(command.executable==L"C:\\Program Files\\资产库\\versions\\0.3.0-preview.1\\AssetLibrary.Settings.exe","same installed version directory");
    Check(command.commandLine==L"\""+command.executable+L"\""&&command.directory==L"C:\\Program Files\\资产库\\versions\\0.3.0-preview.1\\","quoted executable and no arguments");
    for(const auto* path:{L"AssetLibrary.Explorer.dll",L"C:AssetLibrary.Explorer.dll",L"\\\\server\\share\\module.dll",L"C:\\folder\\",L"C:\\folder\\bad\".dll",L"C:/folder/module.dll",L"C:\\folder\\bad\n.dll"}){
        Check(settings::BuildCommand(path,command)==E_INVALIDARG&&command.executable.empty()&&command.commandLine.empty(),"unsafe path rejected without output");
    }
    std::wstring nul=L"C:\\folder\\module.dll";nul.push_back(L'\0');nul+=L".exe";
    Check(settings::BuildCommand(nul,command)==E_INVALIDARG,"embedded NUL rejected");
    Check(FAILED(settings::BuildCommand(L"C:\\"+std::wstring(32740,L'x')+L"\\module.dll",command)),"launch command length bounded");
    CMINVOKECOMMANDINFO info{};info.cbSize=sizeof(info);
    for(const auto* verb:{"assetlibrary.settings","ASSETLIBRARY.SETTINGS"}){info.lpVerb=verb;Check(settings::ValidateVerb(&info)==S_OK,"explicit ANSI settings verb");}
    for(const auto* verb:{"open","delete","https://example.invalid","cmd.exe","assetlibrary.settings-extra"}){info.lpVerb=verb;Check(FAILED(settings::ValidateVerb(&info)),"arbitrary verb rejected");}
    info.lpVerb=MAKEINTRESOURCEA(0);Check(settings::ValidateVerb(&info)==S_OK,"settings offset zero");
    info.lpVerb=MAKEINTRESOURCEA(1);Check(FAILED(settings::ValidateVerb(&info)),"other offsets rejected");
    CMINVOKECOMMANDINFOEX wide{};wide.cbSize=sizeof(wide);wide.fMask=CMIC_MASK_UNICODE;wide.lpVerbW=L"assetlibrary.settings";
    Check(settings::ValidateVerb(reinterpret_cast<CMINVOKECOMMANDINFO*>(&wide))==S_OK,"Unicode settings verb");
    wide.lpVerb="open";Check(FAILED(settings::ValidateVerb(reinterpret_cast<CMINVOKECOMMANDINFO*>(&wide))),"conflicting ANSI verb rejected");
    info.fMask=CMIC_MASK_UNICODE;Check(settings::ValidateVerb(&info)==E_INVALIDARG&&settings::ValidateVerb(nullptr)==E_INVALIDARG,"invalid invocation structures");
    Check(wcsstr(snapshot::StatusText(snapshot::Status::Unavailable),L"连接设置")!=nullptr,"unavailable directs settings");
    Check(wcsstr(snapshot::StatusText(snapshot::Status::Loading),L"重新打开资产库")!=nullptr,"Loading does not promise F5 rearm");
}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=3)return 2;if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    int result=0;
    try {
        Identity(argv[1],product::ClassId,proof::Library::ProofClassId,true);
        Identity(argv[2],proof::Library::ProofClassId,product::ClassId,false);
        PipeIdentity();Commands();
        puts("product_identity=passed; proof_identity=preserved; settings_command=passed; registry=0; GUI=0; pipe_IO=0; process_launch=0");
    }catch(const std::exception& error){fprintf(stderr,"ProductTests: %s\n",error.what());result=1;}
    CoUninitialize();return result;
}
