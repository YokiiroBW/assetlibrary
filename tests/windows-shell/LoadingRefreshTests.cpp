#include "LoadingRefresh.h"
#include "RefreshTestBrowser.h"
#include <thread>

namespace {
using proof::Check;
struct HiddenWindow {
    HWND value;
    explicit HiddenWindow(bool messageOnly=true):value(CreateWindowExW(0,L"STATIC",L"",messageOnly?0:WS_OVERLAPPEDWINDOW,
        0,0,640,480,messageOnly?HWND_MESSAGE:nullptr,nullptr,GetModuleHandleW(nullptr),nullptr)){
        if(!value)throw std::runtime_error("hidden test window");
    }
    ~HiddenWindow(){if(value)DestroyWindow(value);}
    void Close(){if(value){DestroyWindow(value);value=nullptr;}}
    HiddenWindow(const HiddenWindow&)=delete;HiddenWindow& operator=(const HiddenWindow&)=delete;
};
void Pump(DWORD milliseconds){
    const auto end=GetTickCount64()+milliseconds;
    do {
        MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}
        if(GetTickCount64()>=end)return;MsgWaitForMultipleObjectsEx(0,nullptr,5,QS_ALLINPUT,MWMO_INPUTAVAILABLE);
    }while(true);
}
class View final : public IShellView {
public:
    ULONG refs=1,refreshes=0;DWORD refreshThread=0;HWND window=nullptr;
    std::function<void()> onRefresh,onGetWindow;
    HRESULT refreshResult=S_OK;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid!=IID_IUnknown&&iid!=IID_IShellView&&iid!=IID_IOleWindow)return E_NOINTERFACE;
        *result=static_cast<IShellView*>(this);AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override {auto count=--refs;if(!count)delete this;return count;}
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* result) override {if(!result)return E_POINTER;if(onGetWindow)onGetWindow();*result=window;return window?S_OK:E_FAIL;}
    HRESULT STDMETHODCALLTYPE Refresh() override {++refreshes;refreshThread=GetCurrentThreadId();if(onRefresh)onRefresh();return refreshResult;}
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE TranslateAccelerator(MSG*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE EnableModeless(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE UIActivate(UINT) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE CreateViewWindow(IShellView*,LPCFOLDERSETTINGS,IShellBrowser*,RECT*,HWND*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE DestroyViewWindow() override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetCurrentInfo(LPFOLDERSETTINGS) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE AddPropertySheetPages(DWORD,LPFNSVADDPROPSHEETPAGE,LPARAM) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SaveViewState() override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SelectItem(PCUITEMID_CHILD,SVSIF) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetItemObject(UINT,REFIID,void** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
};
struct Fixture {
    HiddenWindow window;
    std::shared_ptr<loading::Signal> signal=std::make_shared<loading::Signal>();
    proof::Com<View> view;
    proof::Com<RefreshBrowser> browser;
    proof::Com<IShellFolderViewCB> callback;
    proof::Com<IObjectWithSite> site;
    Fixture(){
        view.value=new View();view.value->window=window.value;
        browser.value=new RefreshBrowser();browser.value->active=view.value;
        Check(SUCCEEDED(loading::CreateCallback(view.value,signal,&callback.value)),"callback create");
        Check(SUCCEEDED(callback.value->QueryInterface(IID_PPV_ARGS(&site.value))),"callback site");
        Check(SUCCEEDED(site.value->SetSite(static_cast<IShellBrowser*>(browser.value))),"attach site");
        Check(SUCCEEDED(callback.value->MessageSFVCB(SFVM_WINDOWCREATED,reinterpret_cast<WPARAM>(window.value),0)),"attach owner window");
    }
    ~Fixture(){if(site.value)site.value->SetSite(nullptr);window.Close();}
    Fixture(const Fixture&)=delete;Fixture& operator=(const Fixture&)=delete;
};
void Budgets(){
    loading::Budget budget;
    Check(budget.Observe(snapshot::Status::Loading,100),"first Loading starts episode");
    Check(!budget.Take(599)&&budget.Take(600)&&!budget.Take(600),"500ms pacing including queued duplicate timer");
    Check(budget.Observe(snapshot::Status::Loading,10099)&&budget.deadline==10100,"continuous Loading cannot extend deadline");
    Check(!budget.Take(10100)&&!budget.Observe(snapshot::Status::Loading,20000),"ten second exhaustion and no same-Loading restart");
    Check(!budget.Observe(snapshot::Status::Ready,20001)&&budget.Observe(snapshot::Status::Loading,20002),"new Loading after Ready can retry");
    unsigned taken=0;for(ULONGLONG at=20002;at<=40002;++at)if(budget.Take(at))++taken;
    Check(taken<=20&&!budget.active,"hard attempt/time ceiling");
    for(auto status:{snapshot::Status::Ready,snapshot::Status::Unavailable,snapshot::Status::AccessDenied,snapshot::Status::Expired,snapshot::Status::InvalidResponse,snapshot::Status::Busy}){
        loading::Budget one;one.Observe(snapshot::Status::Loading,0);Check(!one.Observe(status,10)&&!one.Take(500),"all terminal states stop");
    }
    puts("budget=passed; interval=500ms; duration=10s; maximum_attempts=20");
}
void Completion(){
    for(auto terminal:{snapshot::Status::Ready,snapshot::Status::AccessDenied}){
        Fixture fixture;
        fixture.view.value->onRefresh=[&]{fixture.signal->Publish(terminal);};
        std::thread producer([&]{fixture.signal->Publish(snapshot::Status::Loading);});producer.join();
        Pump(1150);
        Check(fixture.view.value->refreshes==1&&fixture.view.value->refreshThread==GetCurrentThreadId(),"Loading completes on owner UI thread then stops");
        PostMessageW(fixture.window.value,WM_TIMER,fixture.signal->cookie,0);Pump(50);
        Check(fixture.view.value->refreshes==1,"queued timer after kill is ignored");
    }
    Check(loading::ActiveViews()==0,"completion callback cleanup");
}
void Teardown(){
    {Fixture fixture;fixture.signal->Publish(snapshot::Status::Loading);Pump(20);
        fixture.site.value->SetSite(nullptr);Pump(550);Check(!fixture.view.value->refreshes&&!fixture.signal->window,"site clear stops timer");}
    {Fixture fixture;fixture.signal->Publish(snapshot::Status::Loading);Pump(20);
        PostMessageW(fixture.window.value,WM_TIMER,fixture.signal->cookie,0);fixture.window.Close();Pump(550);
        Check(!fixture.view.value->refreshes&&!fixture.signal->window,"destroy with queued timer stops callbacks");}
    {Fixture fixture;HRESULT siteHr=S_OK,windowHr=S_OK;
        std::thread other([&]{siteHr=fixture.site.value->SetSite(nullptr);windowHr=fixture.callback.value->MessageSFVCB(SFVM_WINDOWCREATED,reinterpret_cast<WPARAM>(fixture.window.value),0);});other.join();
        Check(siteHr==RPC_E_WRONG_THREAD&&windowHr==RPC_E_WRONG_THREAD,"no cross-thread site/window mutation");}
    Check(!loading::ActiveViews(),"teardown capacity cleanup");
}
void ReentryAndMismatch(){
    {Fixture fixture;fixture.view.value->onRefresh=[&]{
        fixture.window.Close();fixture.callback.value->Release();fixture.callback.value=nullptr;fixture.site.value->Release();fixture.site.value=nullptr;
        Check(loading::LiveCallbacks()==1&&!loading::ActiveViews(),"destroy returns slot while call-out retains callback");
    };
        fixture.signal->Publish(snapshot::Status::Loading);Pump(650);
        Check(fixture.view.value->refreshes==1&&!loading::ActiveViews()&&!loading::LiveCallbacks()&&fixture.browser.value->refs==1&&fixture.view.value->refs==1,"reentrant destruction releases self/site/view/owner after return");}
    {Fixture fixture;HiddenWindow different;fixture.view.value->window=different.value;
        fixture.signal->Publish(snapshot::Status::Loading);Pump(650);Check(!fixture.view.value->refreshes,"never refresh another active HWND");}
    {Fixture fixture;fixture.browser.value->serviceResult=E_ACCESSDENIED;
        fixture.signal->Publish(snapshot::Status::Loading);Pump(650);Check(!fixture.view.value->refreshes,"service failure stops without refresh");}
}
void IndependentAndCapacity(){
    {Fixture first,second;
        first.view.value->onRefresh=[&]{first.signal->Publish(snapshot::Status::Ready);};
        second.view.value->onRefresh=[&]{if(second.view.value->refreshes==2)second.signal->Publish(snapshot::Status::Ready);};
        first.signal->Publish(snapshot::Status::Loading);second.signal->Publish(snapshot::Status::Loading);Pump(1250);
        Check(first.view.value->refreshes==1&&second.view.value->refreshes==2,"each view has an independent Loading state");}
    {proof::Com<View> owner;owner.value=new View();std::vector<proof::Com<IShellFolderViewCB>> callbacks;
        for(unsigned i=0;i<4;++i){proof::Com<IShellFolderViewCB> callback;Check(SUCCEEDED(loading::CreateCallback(owner.value,std::make_shared<loading::Signal>(),&callback.value)),"capacity slot");callbacks.push_back(std::move(callback));}
        proof::Com<IShellFolderViewCB> fifth;Check(loading::CreateCallback(owner.value,std::make_shared<loading::Signal>(),&fifth.value)==HRESULT_FROM_WIN32(ERROR_BUSY)&&!fifth.value,"fifth automatic view refused");
    }
    Check(!loading::ActiveViews(),"capacity returns to zero");
    {Fixture first,second,third,fourth;Check(loading::ActiveViews()==4,"four active windows");
        first.window.Close();Check(loading::ActiveViews()==3&&loading::LiveCallbacks()==4,"closed externally retained callback releases its view slot");
        Fixture fifth;Check(loading::ActiveViews()==4&&loading::LiveCallbacks()==5,"fifth view refreshes while closed callback remains referenced");}
    Check(!loading::ActiveViews()&&!loading::LiveCallbacks(),"capacity and callback counts independently return to zero");
}
void Generations(){
    Fixture fixture;auto old=fixture.signal->Begin(),current=fixture.signal->Begin();snapshot::Status status;
    Check(!fixture.signal->Read(status),"in-flight enumeration has no completed current publication");
    fixture.signal->Publish(current,snapshot::Status::Ready);Pump(20);
    fixture.signal->Publish(old,snapshot::Status::Loading);Pump(550);
    Check(fixture.signal->Read(status)&&status==snapshot::Status::Ready&&!fixture.view.value->refreshes,"late Loading cannot replace Ready or arm retry");
    auto next=fixture.signal->Begin();fixture.signal->Publish(current,snapshot::Status::Loading);
    Check(!fixture.signal->Read(status),"old publish cannot complete a newer in-flight request");
    fixture.signal->Publish(next,snapshot::Status::AccessDenied);Pump(20);
    Check(fixture.signal->Read(status)&&status==snapshot::Status::AccessDenied,"newest terminal publication retained");
}
void CalloutChanges(){
    {Fixture fixture;proof::Com<RefreshBrowser> replacement;replacement.value=new RefreshBrowser();replacement.value->active=fixture.view.value;
        fixture.browser.value->duringRelease=[&]{fixture.site.value->SetSite(nullptr);};
        Check(SUCCEEDED(fixture.site.value->SetSite(static_cast<IShellBrowser*>(replacement.value))),"reentrant SetSite replacement");
        proof::Com<IUnknown> current;Check(FAILED(fixture.site.value->GetSite(IID_PPV_ARGS(&current.value)))&&!current.value&&!fixture.signal->window,"old site Release revocation cannot be overwritten");
        Check(replacement.value->refs==1,"revoked replacement reference released");}
    for(unsigned mode=0;mode<5;++mode){
        Fixture fixture;proof::Com<RefreshBrowser> replacement;replacement.value=new RefreshBrowser();replacement.value->active=fixture.view.value;
        bool called=false;
        auto change=[&]{if(called)return;called=true;
            if(mode==0){fixture.signal->Publish(snapshot::Status::Ready);Pump(0);}
            else if(mode==1){fixture.signal->Publish(snapshot::Status::AccessDenied);Pump(0);}
            else if(mode==2)fixture.site.value->SetSite(nullptr);
            else if(mode==3)fixture.site.value->SetSite(static_cast<IShellBrowser*>(replacement.value));
            else {fixture.signal->Publish(snapshot::Status::Ready);Pump(0);fixture.signal->Publish(snapshot::Status::Loading);Pump(0);}
        };
        if(mode==1)fixture.browser.value->duringActive=change;
        else if(mode==2)fixture.view.value->onGetWindow=change;
        else fixture.browser.value->duringQuery=change;
        fixture.signal->Publish(snapshot::Status::Loading);Pump(650);
        Check(called&&!fixture.view.value->refreshes,"call-out changed status/site/episode prevents captured refresh");
    }
}
void DefViewWiring(const wchar_t* path){
    proof::Library library(path);
    {auto root=library.Root();std::vector<proof::Com<IShellView>> views;std::vector<proof::Com<IUnknown>> folders;
        for(unsigned i=0;i<5;++i){
            proof::Com<IShellView> view;Check(SUCCEEDED(root.value->CreateViewObject(nullptr,IID_IShellView,reinterpret_cast<void**>(&view.value))),"DefView including capacity fallback");
            proof::Com<IFolderView> folderView;Check(SUCCEEDED(view.value->QueryInterface(IID_PPV_ARGS(&folderView.value))),"native folder view");
            proof::Com<IUnknown> folder;Check(SUCCEEDED(folderView.value->GetFolder(IID_PPV_ARGS(&folder.value))),"view-specific folder");
            proof::Com<IUnknown> rootIdentity;root.value->QueryInterface(IID_PPV_ARGS(&rootIdentity.value));Check(folder.value!=rootIdentity.value,"independent adapter per view");
            for(const auto& previous:folders)Check(previous.value!=folder.value,"distinct view states");
            folders.push_back(std::move(folder));views.push_back(std::move(view));
        }
    }
    Check(library.canUnload()==S_OK,"uncreated DefViews release callbacks without cycles");
}
void ControlledDllOwner(const wchar_t* path){
    proof::Library library(path);
    {auto root=library.Root();auto signal=std::make_shared<loading::Signal>();proof::Com<IShellFolderViewCB> callback;
        Check(SUCCEEDED(loading::CreateCallback(root.value,signal,&callback.value)),"controlled production DLL owner");
        proof::Com<IObjectWithSite> site;callback.value->QueryInterface(IID_PPV_ARGS(&site.value));
        HiddenWindow window;Check(SUCCEEDED(callback.value->MessageSFVCB(SFVM_WINDOWCREATED,reinterpret_cast<WPARAM>(window.value),0)),"controlled owner window");
        root.value->Release();root.value=nullptr;
        Check(library.canUnload()==S_FALSE,"callback keeps real DLL owner alive");
        window.Close();Check(!loading::ActiveViews()&&library.canUnload()==S_FALSE,"closed view returns slot without releasing live callback owner");
    }
    Check(library.canUnload()==S_OK&&!loading::LiveCallbacks(),"actual final callback release permits DLL unload");
}
void RealDefView(proof::Library& library,bool proofOwner){
    {HiddenWindow parent(false);
        auto root=[&]{
            if(proofOwner)return library.Root();
            proof::Com<IShellFolder> desktop;Check(SUCCEEDED(SHGetDesktopFolder(&desktop.value)),"system Desktop owner");
            proof::Com<IShellFolder2> folder;Check(SUCCEEDED(desktop.value->QueryInterface(IID_PPV_ARGS(&folder.value))),"system Desktop folder interface");return folder;
        }();
        auto signal=std::make_shared<loading::Signal>();
        proof::Com<IShellFolderViewCB> callback;Check(SUCCEEDED(loading::CreateCallback(root.value,signal,&callback.value)),"native callback create");
        proof::Com<IObjectWithSite> site;Check(SUCCEEDED(callback.value->QueryInterface(IID_PPV_ARGS(&site.value))),"native callback site interface");
        proof::Com<IShellView> view;SFV_CREATE create{sizeof(create),root.value,nullptr,callback.value};
        Check(SUCCEEDED(SHCreateShellFolderView(&create,&view.value)),"actual system DefView factory");
        proof::Com<RefreshBrowser> browser;browser.value=new RefreshBrowser();browser.value->window=parent.value;browser.value->active=view.value;
        FOLDERSETTINGS settings{FVM_DETAILS,FWF_NOCLIENTEDGE};RECT bounds{0,0,600,400};HWND child=nullptr;
        Check(SUCCEEDED(view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&child))&&child,"real hidden DefView creation");
        struct DestroyView {IShellView* view;bool needed=true;~DestroyView(){if(needed)view->DestroyViewWindow();}} destroy{view.value};
        Check(signal->window==child,"system SFVM_WINDOWCREATED supplies actual child HWND");
        {proof::Com<IUnknown> supplied;Check(SUCCEEDED(site.value->GetSite(IID_PPV_ARGS(&supplied.value)))&&supplied.value,"system automatically calls SetSite");}
        proof::Com<View> recorder;recorder.value=new View();recorder.value->window=child;
        recorder.value->onRefresh=[&]{signal->Publish(snapshot::Status::Ready);};browser.value->active=recorder.value;
        signal->Publish(snapshot::Status::Loading);Pump(1150);
        Check(recorder.value->refreshes==1&&recorder.value->refreshThread==GetCurrentThreadId(),"automatic system callback reaches active view recorder once then stops");
        browser.value->active=view.value;
        puts("system_SetSite=passed; system_WINDOWCREATED=passed; auto_recorded_refresh=1; proof_data_acceptance=false");
        const auto deactivated=view.value->UIActivate(SVUIA_DEACTIVATE);
        printf("system_UIActivate_DEACTIVATE=%08lx\n",static_cast<ULONG>(deactivated));
        Check(SUCCEEDED(deactivated),"deactivate native view before destruction");
        const auto destroyed=view.value->DestroyViewWindow();destroy.needed=false;
        printf("system_DestroyViewWindow=%08lx; window_alive=%d; signal_window=%d; callback_refs=%lu\n",static_cast<ULONG>(destroyed),IsWindow(child),signal->window.load()!=nullptr,callback.value->AddRef()-1);callback.value->Release();
        {proof::Com<IUnknown> remaining;auto remainingSite=site.value->GetSite(IID_PPV_ARGS(&remaining.value));
            printf("system_after_destroy_site=%08lx; view_refs=%lu; browser_refs=%lu\n",static_cast<ULONG>(remainingSite),view.value->AddRef()-1,browser.value->refs);view.value->Release();}
    }
    Pump(20);
    printf("system_callback_cleanup active=%lu; retained_callbacks=%lu; dll=%08lx\n",loading::ActiveViews(),loading::LiveCallbacks(),static_cast<ULONG>(library.canUnload()));
    Check(!loading::ActiveViews(),"actual window/site teardown returns automatic refresh capacity");
    if(proofOwner)Check(loading::LiveCallbacks()?library.canUnload()==S_FALSE:library.canUnload()==S_OK,"DLL lifetime follows real callback references, not window destruction");
    else Check(library.canUnload()==S_OK,"system-owner mechanism does not retain proof DLL objects");
}

}
int wmain(int argc,wchar_t** argv){
    const bool proofOwner=argc==3&&wcscmp(argv[2],L"--proof-owner-lifetime")==0;
    const bool wiring=proofOwner||(argc==3&&wcscmp(argv[2],L"--wiring-only")==0);
    if(argc!=2&&!wiring)return 2;if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    std::unique_ptr<proof::Library> moduleLifetime;
    int result=0;try{
        if(wiring){moduleLifetime=std::make_unique<proof::Library>(argv[1]);RealDefView(*moduleLifetime,proofOwner);}
        else{Budgets();Completion();Teardown();ReentryAndMismatch();IndependentAndCapacity();Generations();CalloutChanges();DefViewWiring(argv[1]);ControlledDllOwner(argv[1]);}
        puts(wiring?"loading_refresh_mechanism=passed; native_lifetime=pending; registry=0; explorer=0; pipe=0"
            :"loading_refresh_unit=passed; hidden_owned_windows_only; registry=0; explorer=0; pipe=0");
    }
    catch(const std::exception& error){fprintf(stderr,"LoadingRefreshTests: %s\n",error.what());result=1;}
    CoUninitialize();
    if(moduleLifetime){
        printf("after_CoUninitialize retained_callbacks=%lu; dll=%08lx\n",loading::LiveCallbacks(),static_cast<ULONG>(moduleLifetime->canUnload()));
        // Direct LoadLibrary is outside COM's DLL loader. Keep the test's one module
        // reference to process exit if Windows still owns a callback with a DLL owner.
        if(moduleLifetime->canUnload()!=S_OK){
            fprintf(stderr,"native_lifetime=not_passed; safe_module_pin_until_process_exit=true\n");result=1;moduleLifetime.release();
        }
    }
    return result;
}
