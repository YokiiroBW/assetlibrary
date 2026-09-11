#include "ProbeDiagnostics.h"
#include "LoadingRefresh.h"
#include "SnapshotTestSupport.h"
#include <thread>
#include <limits>

namespace {
using proof::Check;
std::wstring Text(IShellFolder2* folder,const PROPERTYKEY& key=diagnostics::Key){
    const USHORT empty=0;VARIANT value{};
    Check(SUCCEEDED(folder->GetDetailsEx(reinterpret_cast<PCUITEMID_CHILD>(&empty),&key,&value)),"diagnostic property");
    Check(value.vt==VT_BSTR&&SysStringLen(value.bstrVal)<=diagnostics::MaxCharacters,"bounded BSTR");
    std::wstring text(value.bstrVal,SysStringLen(value.bstrVal));VariantClear(&value);return text;
}
ULONGLONG Number(const std::wstring& text,const wchar_t* key){
    auto prefix=std::wstring(L"\"")+key+L"\":";auto at=text.find(prefix);Check(at!=std::wstring::npos,"diagnostic field exists");
    auto begin=text.c_str()+at+prefix.size();wchar_t* end=nullptr;auto value=wcstoull(begin,&end,10);Check(end!=begin,"numeric diagnostic field");return value;
}
void FolderIdentity(proof::Library& library){
    auto root=library.Root();auto original=Text(root.value);
    Check(Number(original,L"v")==1&&Number(original,L"pid")==GetCurrentProcessId()&&Number(original,L"enum_n")==0,"execution process and no implicit enumeration");
    Check(original.find(L"\"view\":null")!=std::wstring::npos,"unbound Folder explicitly has no Signal");
    proof::Com<IShellView> view;Check(SUCCEEDED(root.value->CreateViewObject(nullptr,IID_IShellView,reinterpret_cast<void**>(&view.value))),"create view");
    proof::Com<IFolderView> folderView;view.value->QueryInterface(IID_PPV_ARGS(&folderView.value));
    proof::Com<IShellFolder2> clone;Check(SUCCEEDED(folderView.value->GetFolder(IID_PPV_ARGS(&clone.value))),"view Folder");
    auto after=Text(root.value),bound=Text(clone.value),again=Text(clone.value);
    Check(Number(bound,L"folder")!=Number(original,L"folder")&&Number(bound,L"source")==Number(original,L"folder")&&Number(after,L"last_clone")==Number(bound,L"folder"),"source to clone correspondence");
    Check(Number(bound,L"cb_hr")==0&&Number(bound,L"cb_tid")==GetCurrentThreadId()&&Number(bound,L"cookie")!=0,"callback construction diagnostic");
    Check(Number(bound,L"enum_n")==0&&Number(again,L"enum_n")==0&&Number(again,L"started")==0&&Number(again,L"tick_n")==0&&Number(again,L"refresh_n")==0,"reads do not enumerate, request or refresh");
    PROPERTYKEY unknown=diagnostics::Key;unknown.pid=2;VARIANT rejected{};const USHORT empty=0;
    Check(root.value->GetDetailsEx(reinterpret_cast<PCUITEMID_CHILD>(&empty),&unknown,&rejected)==E_INVALIDARG&&rejected.vt==VT_EMPTY,"unknown property rejected");
    wprintf(L"sample_unbound=%ls\nsample_bound=%ls\n",original.c_str(),bound.c_str());
}
void EarlyReturns(proof::Library& library){
    auto root=library.Root();auto signal=std::make_shared<loading::Signal>();proof::Com<IShellFolderViewCB> callback;
    Check(SUCCEEDED(loading::CreateCallback(root.value,signal,&callback.value)),"callback for diagnostic negative path");
    proof::Com<IObjectWithSite> site;callback.value->QueryInterface(IID_PPV_ARGS(&site.value));
    HWND window=CreateWindowExW(0,L"STATIC",L"",0,0,0,0,0,HWND_MESSAGE,nullptr,GetModuleHandleW(nullptr),nullptr);
    if(!window)throw std::runtime_error("owned message window");
    DWORD thread=0;HRESULT siteResult=S_OK,windowResult=S_OK;
    std::thread other([&]{thread=GetCurrentThreadId();siteResult=site.value->SetSite(nullptr);windowResult=callback.value->MessageSFVCB(SFVM_WINDOWCREATED,reinterpret_cast<WPARAM>(window),0);});other.join();
    DestroyWindow(window);
    Check(siteResult==RPC_E_WRONG_THREAD&&windowResult==RPC_E_WRONG_THREAD,"behavior still rejects wrong UI thread");
    Check(signal->diagnostic.siteThread==thread&&signal->diagnostic.windowThread==thread&&signal->diagnostic.windowOwnerThread==GetCurrentThreadId(),"both threads recorded before early return");
    Check(signal->diagnostic.siteResult==RPC_E_WRONG_THREAD&&signal->diagnostic.windowResult==RPC_E_WRONG_THREAD&&signal->window==nullptr&&signal->diagnostic.arms==0,"early results without attaching/timer mutation");
    diagnostics::FolderState state;VARIANT data{};Check(SUCCEEDED(diagnostics::Read(state,signal.get(),&data)),"serialize early return");
    wprintf(L"sample_thread_refusal=%ls\n",data.bstrVal);VariantClear(&data);
}
void Capacity(proof::Library& library){
    auto root=library.Root();std::vector<proof::Com<IShellFolderViewCB>> callbacks;
    for(unsigned i=0;i<4;++i){proof::Com<IShellFolderViewCB> callback;Check(SUCCEEDED(loading::CreateCallback(root.value,std::make_shared<loading::Signal>(),&callback.value)),"reserve existing capacity");callbacks.push_back(std::move(callback));}
    auto signal=std::make_shared<loading::Signal>();proof::Com<IShellFolderViewCB> fifth;
    Check(loading::CreateCallback(root.value,signal,&fifth.value)==HRESULT_FROM_WIN32(ERROR_BUSY),"capacity behavior unchanged");
    const HRESULT created=signal->diagnostic.createResult.load();const HRESULT busy=HRESULT_FROM_WIN32(ERROR_BUSY);
    Check(created==busy&&signal->diagnostic.createSlots==4&&signal->diagnostic.constructorThread==0,"capacity failure is observable without a callback");
}
void Bounds(){
    diagnostics::FolderState state;state.lastClone=std::numeric_limits<ULONGLONG>::max();state.enumerations=std::numeric_limits<ULONG>::max();state.enumThread=std::numeric_limits<ULONG>::max();
    auto signal=std::make_shared<loading::Signal>();signal->started=std::numeric_limits<ULONGLONG>::max();signal->published=std::numeric_limits<ULONGLONG>::max();
    auto& d=signal->diagnostic;d.reportedWindow=~UINT_PTR{};d.activeWindow=~UINT_PTR{};
    for(auto* field:{&d.createSlots,&d.constructorThread,&d.siteCalls,&d.siteThread,&d.sitePresent,&d.windowCalls,&d.windowThread,&d.windowOwnerThread,&d.posts,&d.postError,&d.arms,&d.armThread,&d.armError,&d.timerActive,&d.ticks,&d.attempts,&d.refreshes,&d.detaches,&d.windowMatch,&d.skip,&d.itemCount,&d.pidlValid,&d.renderedKind,&d.renderedStatus})*field=std::numeric_limits<ULONG>::max();
    for(auto* field:{&d.createResult,&d.siteResult,&d.windowResult,&d.serviceResult,&d.activeResult,&d.getWindowResult,&d.refreshResult,&d.folderViewResult,&d.countResult,&d.itemResult})*field=E_FAIL;
    VARIANT data{};Check(SUCCEEDED(diagnostics::Read(state,signal.get(),&data))&&SysStringLen(data.bstrVal)<=2048,"maximum-width counters fit fixed output cap");
    wprintf(L"sample_maximum=%ls\n",data.bstrVal);VariantClear(&data);
}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    int result=0;try{proof::Library library(argv[1]);FolderIdentity(library);EarlyReturns(library);Capacity(library);Bounds();Check(library.canUnload()==S_OK,"diagnostic does not retain DLL objects");puts("diagnostics=passed; no_refresh_or_registration_actions");}
    catch(const std::exception& error){fprintf(stderr,"ProbeDiagnosticsTests: %s\n",error.what());result=1;}
    CoUninitialize();return result;
}
