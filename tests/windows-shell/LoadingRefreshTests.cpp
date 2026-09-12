#include "LoadingRefresh.h"
#include "SnapshotPidl.h"
#include "RefreshTestBrowser.h"
#include "EnumDoneTestFolder.h"
#include "G3Measurements.h"
#include <commctrl.h>
#include <array>
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
class View final : public IShellView, public IFolderView {
public:
    ULONG refs=1,refreshes=0;DWORD refreshThread=0;HWND window=nullptr;
    std::function<void()> onRefresh,onGetWindow,onFolderView,onCount,onItem,onRelease;
    HRESULT refreshResult=S_OK,folderViewResult=S_OK,countResult=S_OK,itemResult=S_OK;
    int count=1;ULONG countReads=0,itemReads=0;bool malformed=false,nullItem=false;
    snapshot::Entry rendered{{},{},snapshot::Kind::StatusRow,L"Loading",snapshot::Status::Loading};
    void Show(snapshot::Status status){
        rendered.status=status;rendered.kind=status==snapshot::Status::Ready?snapshot::Kind::Library:snapshot::Kind::StatusRow;
        rendered.epoch={1,0,0,{}};rendered.node={status==snapshot::Status::Ready?2ul:0ul,0,0,{}};
    }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IFolderView){if(onFolderView)onFolderView();if(folderViewResult!=S_OK)return folderViewResult;*result=static_cast<IFolderView*>(this);}
        else if(iid==IID_IUnknown||iid==IID_IShellView||iid==IID_IOleWindow)*result=static_cast<IShellView*>(this);
        else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs;}
    ULONG STDMETHODCALLTYPE Release() override {auto callback=std::move(onRelease);if(callback)callback();auto remaining=--refs;if(!remaining)delete this;return remaining;}
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
    HRESULT STDMETHODCALLTYPE GetCurrentViewMode(UINT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetCurrentViewMode(UINT) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetFolder(REFIID,void** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE Item(int index,PITEMID_CHILD* result) override {
        if(!result)return E_POINTER;*result=nullptr;++itemReads;Check(index==0,"only first rendered item read");if(onItem)onItem();
        if(itemResult!=S_OK||nullItem)return itemResult;
        *result=snapshot::MakePidl(rendered);if(*result&&malformed)(*result)->mkid.cb=0;
        return *result?S_OK:E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE ItemCount(UINT flags,int* result) override {
        if(!result)return E_POINTER;++countReads;Check(flags==SVGIO_ALLVIEW,"count all rendered items");if(onCount)onCount();*result=count;return countResult;
    }
    HRESULT STDMETHODCALLTYPE Items(UINT,REFIID,void** result) override {if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetSelectionMarkedItem(int*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetFocusedItem(int*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetItemPosition(PCUITEMID_CHILD,POINT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetSpacing(POINT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetDefaultSpacing(POINT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetAutoArrange() override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SelectItem(int,DWORD) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SelectAndPositionItems(UINT,PCUITEMID_CHILD_ARRAY,POINT*,DWORD) override {return E_NOTIMPL;}
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
        Check(SUCCEEDED(loading::CreateCallback(static_cast<IShellView*>(view.value),signal,&callback.value)),"callback create");
        Check(SUCCEEDED(callback.value->QueryInterface(IID_PPV_ARGS(&site.value))),"callback site");
        Check(SUCCEEDED(site.value->SetSite(static_cast<IShellBrowser*>(browser.value))),"attach site");
        Check(SUCCEEDED(callback.value->MessageSFVCB(SFVM_WINDOWCREATED,reinterpret_cast<WPARAM>(window.value),0)),"attach owner window");
    }
    ~Fixture(){if(site.value)site.value->SetSite(nullptr);window.Close();}
    Fixture(const Fixture&)=delete;Fixture& operator=(const Fixture&)=delete;
};
void Budgets(){
    loading::Budget budget;
    budget.Start(100);
    Check(!budget.Take(599)&&budget.Take(600)&&!budget.Take(600),"500ms pacing including queued duplicate timer");
    Check(budget.Take(10099)&&budget.deadline==10100,"observations cannot extend deadline");
    Check(!budget.Take(10100)&&!budget.Take(20000),"ten second exhaustion cannot restart");
    budget.Start(20002);Check(budget.episode==2,"new window starts a distinct episode");
    unsigned taken=0;for(ULONGLONG at=20002;at<=40002;++at)if(budget.Take(at))++taken;
    Check(taken<=20&&!budget.active,"hard attempt/time ceiling");
    budget.Start(0);budget.deadline=100000;for(unsigned i=1;i<=20;++i)Check(budget.Take(i*500),"twenty paced observations");
    Check(!budget.Take(10500)&&!budget.active,"attempt ceiling independent of duration");
    budget.Start(0);budget.Stop();Check(!budget.Take(500),"stopped episode stays stopped");
    puts("budget=passed; interval=500ms; duration=10s; maximum_attempts=20");
}
void Completion(){
    for(auto terminal:{snapshot::Status::Ready,snapshot::Status::AccessDenied}){
        Fixture fixture;
        fixture.view.value->onRefresh=[&]{fixture.view.value->Show(terminal);};
        std::thread producer([&]{fixture.signal->Publish(snapshot::Status::Ready);});producer.join();
        Pump(1150);
        Check(fixture.view.value->refreshes==1&&fixture.view.value->refreshThread==GetCurrentThreadId(),"Loading completes on owner UI thread then stops");
        const auto& d=fixture.signal->diagnostic;const auto& timing=g3::measurements.refresh;
        Check(d.observationStart>0&&timing.thread==GetCurrentThreadId()&&timing.end>=timing.start&&timing.maximum>=timing.last&&!timing.active,"Refresh measures real owner call and initial observation");
        Check(terminal==snapshot::Status::Ready?(d.renderedReady>d.observationStart&&d.readyThread==GetCurrentThreadId()):!d.renderedReady,"only actual valid rendered Ready completes observation");
        const auto firstReady=d.renderedReady.load();
        PostMessageW(fixture.window.value,WM_TIMER,fixture.signal->cookie,0);Pump(50);
        Check(fixture.view.value->refreshes==1,"queued timer after kill is ignored");
        fixture.view.value->Show(snapshot::Status::Loading);fixture.signal->Publish(snapshot::Status::Loading);Pump(550);
        Check(fixture.view.value->refreshes==1,"later Loading and F5 do not restart initial observation");
        Check(d.renderedReady==firstReady,"first rendered Ready timestamp stays fixed within view episode");
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
        first.view.value->onRefresh=[&]{first.view.value->Show(snapshot::Status::Ready);};
        second.view.value->onRefresh=[&]{if(second.view.value->refreshes==2)second.view.value->Show(snapshot::Status::Ready);};
        first.signal->Publish(snapshot::Status::Loading);second.signal->Publish(snapshot::Status::Loading);Pump(1250);
        Check(first.view.value->refreshes==1&&second.view.value->refreshes==2,"each view has an independent Loading state");}
    {proof::Com<View> owner;owner.value=new View();std::vector<proof::Com<IShellFolderViewCB>> callbacks;
        for(unsigned i=0;i<4;++i){proof::Com<IShellFolderViewCB> callback;Check(SUCCEEDED(loading::CreateCallback(static_cast<IShellView*>(owner.value),std::make_shared<loading::Signal>(),&callback.value)),"capacity slot");callbacks.push_back(std::move(callback));}
        proof::Com<IShellFolderViewCB> fifth;Check(loading::CreateCallback(static_cast<IShellView*>(owner.value),std::make_shared<loading::Signal>(),&fifth.value)==HRESULT_FROM_WIN32(ERROR_BUSY)&&!fifth.value,"fifth automatic view refused");
    }
    Check(!loading::ActiveViews(),"capacity returns to zero");
    {Fixture first,second,third,fourth;Check(loading::ActiveViews()==4,"four active windows");
        first.window.Close();Check(loading::ActiveViews()==3&&loading::LiveCallbacks()==4,"closed externally retained callback releases its view slot");
        Fixture fifth;Check(loading::ActiveViews()==4&&loading::LiveCallbacks()==5,"fifth view refreshes while closed callback remains referenced");}
    Check(!loading::ActiveViews()&&!loading::LiveCallbacks(),"capacity and callback counts independently return to zero");
}
void Generations(){
    Fixture fixture;fixture.view.value->Show(snapshot::Status::Ready);auto old=fixture.signal->Begin(),current=fixture.signal->Begin();snapshot::Status status;
    Check(!fixture.signal->Read(status),"in-flight enumeration has no completed current publication");
    fixture.signal->Publish(current,snapshot::Status::Ready);Pump(20);
    fixture.signal->Publish(old,snapshot::Status::Loading);Pump(550);
    Check(fixture.signal->Read(status)&&status==snapshot::Status::Ready&&!fixture.view.value->refreshes,"late Loading cannot replace Ready or arm retry");
    auto next=fixture.signal->Begin();fixture.signal->Publish(current,snapshot::Status::Loading);
    Check(!fixture.signal->Read(status),"old publish cannot complete a newer in-flight request");
    fixture.signal->Publish(next,snapshot::Status::AccessDenied);Pump(20);
    Check(fixture.signal->Read(status)&&status==snapshot::Status::AccessDenied,"newest terminal publication retained");
}
void RenderedBoundaries(){
    for(auto status:{snapshot::Status::Ready,snapshot::Status::Unavailable,snapshot::Status::AccessDenied,snapshot::Status::Expired,snapshot::Status::InvalidResponse,snapshot::Status::Busy}){
        Fixture fixture;fixture.view.value->Show(status);fixture.signal->Publish(snapshot::Status::Loading);Pump(550);
        Check(fixture.view.value->countReads==1&&fixture.view.value->itemReads==1&&!fixture.view.value->refreshes&&!fixture.signal->diagnostic.timerActive,"rendered ordinary/error item stops despite Loading source signal");
    }
    for(unsigned mode=0;mode<8;++mode){
        Fixture fixture;
        if(mode==0)fixture.view.value->folderViewResult=E_NOINTERFACE;
        else if(mode==1)fixture.view.value->countResult=E_FAIL;
        else if(mode==2)fixture.view.value->count=-1;
        else if(mode==3)fixture.view.value->count=102;
        else if(mode==4)fixture.view.value->itemResult=E_FAIL;
        else if(mode==5)fixture.view.value->nullItem=true;
        else if(mode==6)fixture.view.value->malformed=true;
        else fixture.view.value->countResult=S_FALSE;
        Pump(550);
        Check(!fixture.view.value->refreshes&&!fixture.signal->diagnostic.timerActive,"unknown/malformed rendered metadata fails closed");
        if(mode<4||mode==7)Check(!fixture.view.value->itemReads,"failed or out-of-bounds count never reads item");
    }
    {Fixture fixture;fixture.view.value->count=101;fixture.view.value->Show(snapshot::Status::Ready);Pump(550);
        Check(fixture.view.value->itemReads==1&&!fixture.view.value->refreshes,"bounded full page reads only first item");}
    {Fixture fixture;fixture.view.value->refreshResult=E_FAIL;Pump(550);
        Check(fixture.view.value->refreshes==1&&!fixture.signal->diagnostic.timerActive,"failed Refresh terminates observation");}
}
void EmptyAndDeadline(){
    {Fixture empty,delayed;
        empty.view.value->count=0;delayed.view.value->count=0;
        empty.signal->Publish(snapshot::Status::Loading);delayed.signal->Publish(snapshot::Status::Ready);
        Pump(600);Check(!empty.view.value->refreshes&&!delayed.view.value->refreshes&&!empty.view.value->itemReads,"unpainted first enumeration makes no refresh or item call");
        delayed.view.value->count=1;delayed.view.value->onRefresh=[&]{delayed.view.value->Show(snapshot::Status::Ready);};
        Pump(10200);
        Check(!empty.view.value->refreshes&&!empty.view.value->itemReads&&empty.view.value->countReads>1&&empty.view.value->countReads<=20&&!empty.signal->diagnostic.timerActive,"zero items observes until fixed deadline without inventing success");
        Check(delayed.view.value->refreshes==1&&!delayed.signal->diagnostic.timerActive,"painted Loading after empty starts refresh and Ready stops");
        PostMessageW(empty.window.value,WM_TIMER,empty.signal->cookie,0);Pump(0);
        Check(!empty.view.value->refreshes,"expired queued tick cannot query");
    }
    {Fixture fixture;fixture.view.value->onItem=[] {Sleep(static_cast<DWORD>(loading::DurationMs));};Pump(550);
        Check(fixture.view.value->itemReads==1&&!fixture.view.value->refreshes&&!fixture.signal->diagnostic.timerActive,"call-out crossing fixed deadline cannot Refresh or rearm");}
}
void CalloutChanges(){
    {Fixture fixture;proof::Com<RefreshBrowser> replacement;replacement.value=new RefreshBrowser();replacement.value->active=fixture.view.value;
        fixture.browser.value->duringRelease=[&]{fixture.site.value->SetSite(nullptr);};
        Check(SUCCEEDED(fixture.site.value->SetSite(static_cast<IShellBrowser*>(replacement.value))),"reentrant SetSite replacement");
        proof::Com<IUnknown> current;Check(FAILED(fixture.site.value->GetSite(IID_PPV_ARGS(&current.value)))&&!current.value&&!fixture.signal->window,"old site Release revocation cannot be overwritten");
        Check(replacement.value->refs==1,"revoked replacement reference released");}
    for(unsigned mode=0;mode<7;++mode){
        Fixture fixture;proof::Com<RefreshBrowser> replacement;replacement.value=new RefreshBrowser();replacement.value->active=fixture.view.value;
        bool called=false;
        auto change=[&]{if(called)return;called=true;
            if(mode%2==0)fixture.site.value->SetSite(static_cast<IShellBrowser*>(replacement.value));
            else fixture.window.Close();
        };
        if(mode==1)fixture.browser.value->duringActive=change;
        else if(mode==2)fixture.view.value->onGetWindow=change;
        else if(mode==3)fixture.view.value->onFolderView=change;
        else if(mode==4)fixture.view.value->onCount=change;
        else if(mode==5)fixture.view.value->onItem=change;
        else if(mode==6){fixture.view.value->count=0;fixture.view.value->onRelease=change;}
        else fixture.browser.value->duringQuery=change;
        fixture.signal->Publish(snapshot::Status::Loading);Pump(650);
        Check(called&&!fixture.view.value->refreshes&&!fixture.signal->diagnostic.timerActive,"call-out changed site/window prevents captured refresh and rearming");
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
        recorder.value->onRefresh=[&]{recorder.value->Show(snapshot::Status::Ready);};browser.value->active=recorder.value;
        signal->Publish(snapshot::Status::AccessDenied);Pump(1150);
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

struct EnumDoneTrace {
    static constexpr UINT ReadMessage=WM_APP+113;
    static constexpr UINT_PTR Timer=113;
    struct Event {std::atomic<ULONGLONG> tick{0};std::atomic<DWORD> thread{0};std::atomic_ulong phase{0};};
    std::array<Event,16> events;
    std::atomic_ulong done{0},windowCreated{0},phase{0},postFailures{0};
    std::atomic<HWND> target{nullptr};
    std::atomic_bool queued{false};
    const ULONGLONG started=GetTickCount64();
    IShellView* view=nullptr; // Owner-thread only, held by the fixture until detach.
    unsigned reads=0;
    bool timer=false,lastValid=false;
    snapshot::Status lastStatus=snapshot::Status::Unavailable;
    void Notify() noexcept {
        const auto index=done.fetch_add(1);
        if(index<events.size()){events[index].thread=GetCurrentThreadId();events[index].phase=phase.load();events[index].tick=GetTickCount64();}
        const auto window=target.load();
        if(window&&!queued.exchange(true)&&!PostMessageW(window,ReadMessage,0,0)){++postFailures;queued=false;}
    }
    void Read(bool notification=true) noexcept {
        if(notification)++reads;proof::Com<IFolderView> folder;int count=-1;snapshot::Entry entry;
        auto hr=view?view->QueryInterface(IID_PPV_ARGS(&folder.value)):E_ABORT;
        if(SUCCEEDED(hr))hr=folder.value->ItemCount(SVGIO_ALLVIEW,&count);
        bool valid=false;
        if(SUCCEEDED(hr)&&count==1){
            proof::Item item;hr=folder.value->Item(0,&item.value);
            if(SUCCEEDED(hr))try{valid=snapshot::ReadPidl(item.value,entry);}catch(const std::bad_alloc&){hr=E_OUTOFMEMORY;}
        }
        lastValid=valid;lastStatus=valid?entry.status:snapshot::Status::Unavailable;
        printf("enum_done_read triggered=%d; phase=%lu; elapsed_ms=%llu; thread=%lu; done=%lu; hr=%08lx; count=%d; valid=%d; status=%lu\n",
            notification,phase.load(),GetTickCount64()-started,GetCurrentThreadId(),done.load(),static_cast<ULONG>(hr),count,valid,valid?static_cast<ULONG>(entry.status):MAXDWORD);
    }
    static LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM wParam,LPARAM lParam,UINT_PTR,DWORD_PTR data) noexcept {
        auto self=reinterpret_cast<EnumDoneTrace*>(data);
        if(message==ReadMessage){
            // Coalesced, single delayed metadata read; notifications never reset this timer.
            if(!self->timer&&GetTickCount64()-self->started<5000){
                self->timer=SetTimer(window,Timer,500,nullptr)!=0;
                if(!self->timer){++self->postFailures;self->queued=false;}
            }
            return 0;
        }
        if(message==WM_TIMER&&wParam==Timer){
            KillTimer(window,Timer);self->timer=false;self->queued=false;
            if(GetTickCount64()-self->started<5000)self->Read();return 0;
        }
        return DefSubclassProc(window,message,wParam,lParam);
    }
};
// This wraps only the callback to record real system delivery. IShellView is untouched.
class EnumDoneCallback final : public IShellFolderViewCB,public IObjectWithSite {
    std::atomic_ulong refs_{1};
    proof::Com<IShellFolderViewCB> inner_;
    std::shared_ptr<EnumDoneTrace> trace_;
public:
    EnumDoneCallback(IShellFolderViewCB* inner,std::shared_ptr<EnumDoneTrace> trace):trace_(std::move(trace)){inner_.value=inner;if(inner)inner->AddRef();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown||iid==IID_IShellFolderViewCB)*result=static_cast<IShellFolderViewCB*>(this);
        else if(iid==IID_IObjectWithSite)*result=static_cast<IObjectWithSite*>(this);else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {auto left=--refs_;if(!left)delete this;return left;}
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override {
        proof::Com<IObjectWithSite> inner;if(!inner_.value)return E_NOTIMPL;
        auto hr=inner_.value->QueryInterface(IID_PPV_ARGS(&inner.value));return SUCCEEDED(hr)?inner.value->SetSite(site):hr;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;proof::Com<IObjectWithSite> inner;if(!inner_.value)return E_FAIL;
        auto hr=inner_.value->QueryInterface(IID_PPV_ARGS(&inner.value));return SUCCEEDED(hr)?inner.value->GetSite(iid,result):hr;
    }
    HRESULT STDMETHODCALLTYPE MessageSFVCB(UINT message,WPARAM wParam,LPARAM lParam) override {
        if(message==SFVM_BACKGROUNDENUMDONE)trace_->Notify();
        if(message==SFVM_WINDOWCREATED)++trace_->windowCreated;
        return inner_.value?inner_.value->MessageSFVCB(message,wParam,lParam):E_NOTIMPL;
    }
};
struct EnumDoneDeadline {
    HANDLE done=CreateEventW(nullptr,TRUE,FALSE,nullptr);
    std::thread watcher;
    EnumDoneDeadline(){
        Check(done!=nullptr,"diagnostic deadline event");
        try {watcher=std::thread([this]{if(WaitForSingleObject(done,15000)!=WAIT_OBJECT_0)TerminateProcess(GetCurrentProcess(),70);});}
        catch(...){CloseHandle(done);throw;}
    }
    ~EnumDoneDeadline(){SetEvent(done);watcher.join();CloseHandle(done);}
};
void EnumDoneMechanism(){
    EnumDoneDeadline deadline;
    puts("enum_done_research_only=true; empty_pidl_may_rebind_desktop=true; not_extension_acceptance=true");
    for(unsigned mode=0;mode<3;++mode){
        const char* label=mode==0?"initial":mode==1?"external":"automatic";
        auto trace=std::make_shared<EnumDoneTrace>(),negative=std::make_shared<EnumDoneTrace>();
        proof::Com<EnumDoneCallback> unattached;unattached.value=new EnumDoneCallback(nullptr,negative);
        HiddenWindow parent(false);
        proof::Com<enum_done_test::Folder> folder;folder.value=new enum_done_test::Folder();
        if(mode==2){folder.value->status=snapshot::Status::Loading;folder.value->completeLoadingOnSecondEnumeration=true;}
        auto signal=std::make_shared<loading::Signal>();proof::Com<IShellFolderViewCB> inner;
        Check(SUCCEEDED(loading::CreateCallback(static_cast<IShellFolder2*>(folder.value),signal,&inner.value)),"mechanism existing callback");
        proof::Com<EnumDoneCallback> callback;callback.value=new EnumDoneCallback(inner.value,trace);
        proof::Com<IShellView> view;SFV_CREATE create{sizeof(create),folder.value,nullptr,callback.value};
        Check(SUCCEEDED(SHCreateShellFolderView(&create,&view.value)),"synthetic system DefView");
        proof::Com<RefreshBrowser> browser;browser.value=new RefreshBrowser();browser.value->window=parent.value;browser.value->active=view.value;
        trace->view=view.value;trace->target=parent.value;
        Check(SetWindowSubclass(parent.value,EnumDoneTrace::WindowProc,113,reinterpret_cast<DWORD_PTR>(trace.get()))!=FALSE,"mechanism read dispatcher");
        struct DetachRead {HWND window;EnumDoneTrace* trace;~DetachRead(){trace->target=nullptr;trace->view=nullptr;KillTimer(window,EnumDoneTrace::Timer);RemoveWindowSubclass(window,EnumDoneTrace::WindowProc,113);}} detach{parent.value,trace.get()};
        FOLDERSETTINGS settings{FVM_DETAILS,FWF_NOCLIENTEDGE};RECT bounds{0,0,600,400};HWND child=nullptr;
        Check(SUCCEEDED(view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&child))&&child,"synthetic hidden view creation");
        struct DestroyView {IShellView* view;~DestroyView(){view->DestroyViewWindow();}} destroy{view.value};
        Pump(1500);
        if(mode==1){
            trace->phase=1;folder.value->status=snapshot::Status::Loading;
            Check(SUCCEEDED(view.value->Refresh()),"external system Refresh");Pump(1500);
        }
        trace->Read(false);
        printf("enum_done_fixture mode=%s; window_callbacks=%lu; source_enumerations=%lu; negative_done=%lu; automatic_refreshes=%lu\n",
            label,trace->windowCreated.load(),folder.value->enumerations.load(),negative->done.load(),signal->diagnostic.refreshes.load());
        Check(trace->lastValid&&trace->lastStatus==(mode==1?snapshot::Status::Loading:snapshot::Status::Ready),"system view actually renders synthetic page");
        Check(trace->windowCreated==1&&folder.value->enumerations>0,"system callback and synthetic enumeration positive controls");
        Check(!negative->done&&!negative->windowCreated&&!negative->reads,"unattached recorder cannot manufacture system events");
        Check(trace->done<=trace->events.size()&&!trace->postFailures,"bounded notification recording and dispatch");
        Check(mode==2?signal->diagnostic.refreshes>0:signal->diagnostic.refreshes==0,"internal Refresh uses unchanged Loading callback only");
        printf("enum_done_mode=%s; owner_thread=%lu; enumerations=%lu; enum_thread=%lu; automatic_refreshes=%lu; done=%lu; deferred_reads=%u; negative_done=%lu\n",
            label,GetCurrentThreadId(),folder.value->enumerations.load(),folder.value->enumThread.load(),signal->diagnostic.refreshes.load(),trace->done.load(),trace->reads,negative->done.load());
        for(ULONG index=0;index<trace->done;++index){
            const auto& event=trace->events[index];Check(event.tick!=0,"completed notification record");
            printf("enum_done_event mode=%s; ordinal=%lu; phase=%lu; elapsed_ms=%llu; thread=%lu\n",label,index+1,event.phase.load(),event.tick.load()-trace->started,event.thread.load());
        }
        Check(GetTickCount64()-trace->started<5000,"fixed mechanism phase deadline");
    }
    Check(!loading::ActiveViews(),"mechanism windows return automatic observation quota");
    puts("enum_done_mechanism=recorded; explorer=0; registry=0; pipe=0; manual_done_calls=0; behavior_changed=false");
}

}
int wmain(int argc,wchar_t** argv){
    const bool enumDone=argc==2&&wcscmp(argv[1],L"--enum-done-mechanism")==0;
    const bool proofOwner=argc==3&&wcscmp(argv[2],L"--proof-owner-lifetime")==0;
    const bool wiring=proofOwner||(argc==3&&wcscmp(argv[2],L"--wiring-only")==0);
    if(argc!=2&&!wiring)return 2;if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    std::unique_ptr<proof::Library> moduleLifetime;
    int result=0;try{
        if(enumDone)EnumDoneMechanism();
        else if(wiring){moduleLifetime=std::make_unique<proof::Library>(argv[1]);RealDefView(*moduleLifetime,proofOwner);}
        else{Budgets();Completion();Teardown();ReentryAndMismatch();IndependentAndCapacity();Generations();RenderedBoundaries();EmptyAndDeadline();CalloutChanges();DefViewWiring(argv[1]);ControlledDllOwner(argv[1]);}
        puts(enumDone?"enum_done_controls=passed; notification_delivery=see_recorded_result; native_explorer_acceptance=false":wiring?"loading_refresh_mechanism=passed; native_lifetime=pending; registry=0; explorer=0; pipe=0"
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
