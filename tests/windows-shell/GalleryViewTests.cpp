#include "gallery/View.h"
#include "LoadingRefresh.h"
#include "EnumDoneTestFolder.h"
#include "RefreshTestBrowser.h"
#include "ProductIdentity.h"
#include "gallery/Uia.h"
#include <shlguid.h>
#include <atomic>
#include <cstdio>
#include <thread>

namespace {
using proof::Check;
std::atomic_ulong queries{0};
bool cleanupFailed=false;
snapshot::Page Page(const snapshot::Location&,HANDLE) noexcept {++queries;return {};}
thumbnail::Result Image(const snapshot::Location&,HANDLE) noexcept {++queries;return {};}
struct HiddenWindow {
    HWND value;
    explicit HiddenWindow(bool offscreen=false,bool topLevel=false):value(CreateWindowExW(offscreen?WS_EX_NOACTIVATE|WS_EX_TOOLWINDOW:0,L"STATIC",L"",
        offscreen?WS_POPUP|WS_VISIBLE:0,offscreen?-32000:0,offscreen?-32000:0,640,480,(offscreen||topLevel)?nullptr:HWND_MESSAGE,nullptr,GetModuleHandleW(nullptr),nullptr)){
        if(!value)throw std::runtime_error("owned view test parent");}
    ~HiddenWindow(){if(value)DestroyWindow(value);}
};
void Pump(DWORD duration){
    const auto until=GetTickCount64()+duration;
    do{MSG message{};while(PeekMessageW(&message,nullptr,0,0,PM_REMOVE)){TranslateMessage(&message);DispatchMessageW(&message);}if(GetTickCount64()>=until)return;MsgWaitForMultipleObjectsEx(0,nullptr,5,QS_ALLINPUT,MWMO_INPUTAVAILABLE);}while(true);
}
bool UiaIdle(){const auto state=gallery::InspectUia();return !state.providers&&!state.pendingRetirements&&!state.dispatcherWindows;}
template<class F>void Eventually(F ready,const char* message,DWORD timeout=3000){
    const auto until=GetTickCount64()+timeout;while(!ready()&&GetTickCount64()<until)Pump(5);Check(ready(),message);
}
ULONG References(IUnknown* object){object->AddRef();return object->Release();}
class CompareFolder final:public IShellFolder2 {
    ULONG refs_=1;
    enum_done_test::Folder base_;
public:
    std::function<void()> comparing;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result)override{if(!result)return E_POINTER;*result=nullptr;if(iid!=IID_IUnknown&&iid!=IID_IShellFolder&&iid!=IID_IShellFolder2)return E_NOINTERFACE;*result=static_cast<IShellFolder2*>(this);AddRef();return S_OK;}
    ULONG STDMETHODCALLTYPE AddRef()override{return ++refs_;}
    ULONG STDMETHODCALLTYPE Release()override{const auto refs=--refs_;if(!refs)delete this;return refs;}
    HRESULT STDMETHODCALLTYPE CompareIDs(LPARAM flags,PCUIDLIST_RELATIVE a,PCUIDLIST_RELATIVE b)override{auto action=std::move(comparing);if(action)action();return base_.CompareIDs(flags,a,b);}
    HRESULT STDMETHODCALLTYPE ParseDisplayName(HWND a,IBindCtx* b,LPWSTR c,ULONG* d,PIDLIST_RELATIVE* e,ULONG* f)override{return base_.ParseDisplayName(a,b,c,d,e,f);}
    HRESULT STDMETHODCALLTYPE EnumObjects(HWND a,SHCONTF b,IEnumIDList** c)override{return base_.EnumObjects(a,b,c);}
    HRESULT STDMETHODCALLTYPE BindToObject(PCUIDLIST_RELATIVE a,IBindCtx* b,REFIID c,void** d)override{return base_.BindToObject(a,b,c,d);}
    HRESULT STDMETHODCALLTYPE BindToStorage(PCUIDLIST_RELATIVE a,IBindCtx* b,REFIID c,void** d)override{return base_.BindToStorage(a,b,c,d);}
    HRESULT STDMETHODCALLTYPE CreateViewObject(HWND a,REFIID b,void** c)override{return base_.CreateViewObject(a,b,c);}
    HRESULT STDMETHODCALLTYPE GetAttributesOf(UINT a,PCUITEMID_CHILD_ARRAY b,SFGAOF* c)override{return base_.GetAttributesOf(a,b,c);}
    HRESULT STDMETHODCALLTYPE GetUIObjectOf(HWND a,UINT b,PCUITEMID_CHILD_ARRAY c,REFIID d,UINT* e,void** f)override{return base_.GetUIObjectOf(a,b,c,d,e,f);}
    HRESULT STDMETHODCALLTYPE GetDisplayNameOf(PCUITEMID_CHILD a,SHGDNF b,STRRET* c)override{return base_.GetDisplayNameOf(a,b,c);}
    HRESULT STDMETHODCALLTYPE SetNameOf(HWND a,PCUITEMID_CHILD b,LPCWSTR c,SHGDNF d,PITEMID_CHILD* e)override{return base_.SetNameOf(a,b,c,d,e);}
    HRESULT STDMETHODCALLTYPE GetDefaultSearchGUID(GUID* a)override{return base_.GetDefaultSearchGUID(a);}
    HRESULT STDMETHODCALLTYPE EnumSearches(IEnumExtraSearch** a)override{return base_.EnumSearches(a);}
    HRESULT STDMETHODCALLTYPE GetDefaultColumn(DWORD a,ULONG* b,ULONG* c)override{return base_.GetDefaultColumn(a,b,c);}
    HRESULT STDMETHODCALLTYPE GetDefaultColumnState(UINT a,SHCOLSTATEF* b)override{return base_.GetDefaultColumnState(a,b);}
    HRESULT STDMETHODCALLTYPE GetDetailsEx(PCUITEMID_CHILD a,const SHCOLUMNID* b,VARIANT* c)override{return base_.GetDetailsEx(a,b,c);}
    HRESULT STDMETHODCALLTYPE GetDetailsOf(PCUITEMID_CHILD a,UINT b,SHELLDETAILS* c)override{return base_.GetDetailsOf(a,b,c);}
    HRESULT STDMETHODCALLTYPE MapColumnToSCID(UINT a,SHCOLUMNID* b)override{return base_.MapColumnToSCID(a,b);}
};
class CreationBrowser final:public IShellBrowser {
    ULONG refs_=1;
public:
    HWND parent=nullptr;
    IShellView* active=nullptr;
    std::wstring status;
    HRESULT statusResult=S_OK;
    std::vector<std::wstring> statuses;
    std::function<void()> adding;
    std::function<void()> getWindow;
    std::function<void()> querying;
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result)override{if(!result)return E_POINTER;*result=nullptr;if(iid!=IID_IUnknown&&iid!=IID_IOleWindow&&iid!=IID_IShellBrowser)return E_NOINTERFACE;*result=static_cast<IShellBrowser*>(this);AddRef();return S_OK;}
    ULONG STDMETHODCALLTYPE AddRef()override{++refs_;auto action=std::move(adding);if(action)action();return refs_;}
    ULONG STDMETHODCALLTYPE Release()override{const auto refs=--refs_;if(!refs)delete this;return refs;}
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* result)override{if(!result)return E_POINTER;auto action=std::move(getWindow);if(action)action();*result=parent;return S_OK;}
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE InsertMenusSB(HMENU,LPOLEMENUGROUPWIDTHS)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetMenuSB(HMENU,HOLEMENU,HWND)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE RemoveMenusSB(HMENU)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SetStatusTextSB(LPCWSTR text)override{try{status=text;statuses.push_back(status);}catch(const std::bad_alloc&){return E_OUTOFMEMORY;}return statusResult;}
    HRESULT STDMETHODCALLTYPE EnableModelessSB(BOOL)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE TranslateAcceleratorSB(MSG*,WORD)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE BrowseObject(PCUIDLIST_RELATIVE,UINT)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetViewStateStream(DWORD,IStream** result)override{if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetControlWindow(UINT,HWND* result)override{if(result)*result=nullptr;return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SendControlMsg(UINT,UINT,WPARAM,LPARAM,LRESULT*)override{return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE QueryActiveShellView(IShellView** result)override{if(!result)return E_POINTER;*result=nullptr;auto action=std::move(querying);if(action)action();*result=active;if(active)active->AddRef();return active?S_OK:E_FAIL;}
    HRESULT STDMETHODCALLTYPE OnViewWindowActive(IShellView*)override{return S_OK;}
    HRESULT STDMETHODCALLTYPE SetToolbarItems(LPTBBUTTONSB,UINT,UINT)override{return E_NOTIMPL;}
};
struct PageControl {
    DWORD ui=GetCurrentThreadId();
    std::atomic_ulong calls{0},canceled{0},uiCalls{0};
    std::atomic_ulong images{0};
    std::atomic_bool blockAll{false},blockAfterFirst{false};
    std::atomic<snapshot::Status> status{snapshot::Status::Ready};
    UINT items=2;
    bool more=false;
    ~PageControl(){const auto until=GetTickCount64()+2000;while(gallery::PendingWork()&&GetTickCount64()<until)Sleep(1);if(gallery::PendingWork())TerminateProcess(GetCurrentProcess(),70);}
};
PageControl* pageControl=nullptr;
snapshot::Page VisiblePage(const snapshot::Location& location,HANDLE cancel)noexcept{
    const auto ordinal=++pageControl->calls;if(GetCurrentThreadId()==pageControl->ui)++pageControl->uiCalls;
    if(pageControl->blockAll||(pageControl->blockAfterFirst&&ordinal>1))if(WaitForSingleObject(cancel,5000)==WAIT_OBJECT_0)++pageControl->canceled;
    snapshot::Page page;page.status=pageControl->status;page.epoch=location.epoch;if(page.status!=snapshot::Status::Ready)return page;
    try{page.entries={ {location.epoch,{401},snapshot::Kind::File,L"fixture-b"},{location.epoch,{402},snapshot::Kind::File,L"fixture-a"} };
        for(UINT i=2;i<pageControl->items;++i)page.entries.push_back({location.epoch,{401+i},snapshot::Kind::File,L"fixture"});
        if(pageControl->more)page.entries.push_back({location.epoch,{999},snapshot::Kind::NextPage,L"下一页（导航）"});}
    catch(const std::bad_alloc&){page={};}return page;
}
thumbnail::Result VisibleImage(const snapshot::Location& location,HANDLE)noexcept{
    ++pageControl->images;if(GetCurrentThreadId()==pageControl->ui)++pageControl->uiCalls;
    thumbnail::Result result;result.status=thumbnail::Status::Ready;result.location=location;
    try{auto image=std::make_shared<gallery::Pbgra>();image->width=4;image->height=3;image->stride=16;image->pixels.resize(48);result.image=std::move(image);}
    catch(const std::bad_alloc&){result={};}return result;
}
template<class F>void Await(F ready,const char* phase="quiesce"){
    const auto until=GetTickCount64()+2000;while(!ready()&&GetTickCount64()<until)Pump(5);
    if(!ready()){fprintf(stderr,"phase=%s pages=%lu canceled=%lu jobs=%lu\n",phase,pageControl->calls.load(),pageControl->canceled.load(),gallery::PendingWork());throw std::runtime_error("bounded visible-view condition");}
}
struct VisibleFixture {
    HiddenWindow parent{true};
    proof::Com<CompareFolder> folder;
    proof::Com<CreationBrowser> browser;
    proof::Item root{snapshot::MakePidl({{501},{502},snapshot::Kind::Library,L"offscreen synthetic namespace"})};
    proof::Com<IShellView> view;
    proof::Com<IFolderView> items;
    VisibleFixture(){folder.value=new CompareFolder();browser.value=new CreationBrowser();browser.value->parent=parent.value;
        Check(gallery::CreateView(folder.value,root.value,{{501},{502}},&view.value,{VisiblePage,VisibleImage})==S_OK,"offscreen gallery create");
        Check(view.value->QueryInterface(IID_PPV_ARGS(&items.value))==S_OK,"offscreen IFolderView");browser.value->active=view.value;}
    ~VisibleFixture(){if(view.value)view.value->DestroyViewWindow();browser.value->active=nullptr;}
    HWND Open(int width=600,int height=400){RECT bounds{0,0,width,height};FOLDERSETTINGS settings{FVM_ICON,0};HWND window=nullptr;
        SetWindowPos(parent.value,nullptr,0,0,width,height,SWP_NOMOVE|SWP_NOZORDER|SWP_NOACTIVATE);
        Check(view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&window)==S_OK,"offscreen child created");
        ShowWindow(parent.value,SW_SHOWNOACTIVATE);ShowWindow(window,SW_SHOWNOACTIVATE);
        Check(IsWindowVisible(window),"only owned offscreen content is logically visible");return window;}
    int Count(){int count=-1;Check(items.value->ItemCount(SVGIO_ALLVIEW,&count)==S_OK,"current memory count");return count;}
};
struct NotificationFolder {
    std::wstring path;
    proof::Item pidl;
    NotificationFolder(){
        wchar_t temporary[MAX_PATH]{},id[40]{};const auto length=GetTempPathW(MAX_PATH,temporary);GUID guid{};
        Check(length>0&&length<MAX_PATH&&SUCCEEDED(CoCreateGuid(&guid))&&StringFromGUID2(guid,id,40)>0,"private temporary notification directory identity");
        path=std::wstring(temporary)+L"AssetLibrary.Gallery.Notify."+id;
        Check(CreateDirectoryW(path.c_str(),nullptr)!=FALSE,"create only owned empty temporary directory");
        const auto hr=SHParseDisplayName(path.c_str(),nullptr,&pidl.value,0,nullptr);
        if(FAILED(hr)){if(!RemoveDirectoryW(path.c_str()))cleanupFailed=true;throw std::runtime_error("parse temporary directory Shell PIDL");}
    }
    ~NotificationFolder(){if(!RemoveDirectoryW(path.c_str()))cleanupFailed=true;}
};
struct Fixture {
    HiddenWindow parent;
    proof::Com<enum_done_test::Folder> folder;
    proof::Com<RefreshBrowser> browser;
    proof::Item root{snapshot::MakePidl({{101},{201},snapshot::Kind::Library,L"private notification fixture"})};
    proof::Com<IShellView> view;
    explicit Fixture(PCIDLIST_ABSOLUTE notificationRoot=nullptr){folder.value=new enum_done_test::Folder();browser.value=new RefreshBrowser();browser.value->window=parent.value;
        Check(gallery::CreateView(folder.value,root.value,{{101},{201}},&view.value,{Page,Image},notificationRoot)==S_OK,"gallery view create");browser.value->active=view.value;}
    ~Fixture(){if(view.value)view.value->DestroyViewWindow();browser.value->active=nullptr;}
    HWND Open(){RECT bounds{0,0,600,400};FOLDERSETTINGS settings{FVM_ICON,FWF_AUTOARRANGE};HWND window=nullptr;
        Check(view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&window)==S_OK&&window,"gallery child HWND created");return window;}
};
void InterfacesAndFinalRelease(){
    Fixture fixture;const auto before=loading::ActiveViews();const auto window=fixture.Open();
    Check(GetParent(window)==fixture.parent.value&&loading::ActiveViews()==before+1,"same-process child and shared quota");
    proof::Com<IFolderView> items;Check(fixture.view.value->QueryInterface(IID_PPV_ARGS(&items.value))==S_OK,"IFolderView");
    proof::Com<IUnknown> defView;Check(fixture.view.value->QueryInterface(IID_CDefView,reinterpret_cast<void**>(&defView.value))==E_NOINTERFACE,"custom view does not impersonate DefView");
    const auto style=GetWindowLongPtrW(window,GWL_STYLE);
    Check(fixture.view.value->UIActivate(SVUIA_DEACTIVATE)==S_OK&&GetWindowLongPtrW(window,GWL_STYLE)==style,"focus loss does not hide content HWND");
    UINT mode=0;Check(items.value->GetCurrentViewMode(&mode)==S_OK&&mode==FVM_ICON,"default gallery mode");
    Check(items.value->SetCurrentViewMode(FVM_DETAILS)==S_OK&&items.value->GetCurrentViewMode(&mode)==S_OK&&mode==FVM_DETAILS,"real list mode");
    Check(items.value->SetCurrentViewMode(FVM_ICON)==S_OK&&items.value->SetCurrentViewMode(FVM_CONTENT)==E_NOTIMPL,"gallery mode and unsupported mode honesty");
    int count=-1;Check(items.value->ItemCount(SVGIO_ALLVIEW,&count)==S_OK&&count==0,"hidden view has no fabricated page");
    PITEMID_CHILD absent=nullptr;Check(items.value->Item(0,&absent)==E_INVALIDARG&&!absent,"out-of-page item rejected");
    Check(items.value->SelectItem(0,SVSI_EDIT)==E_ACCESSDENIED,"rename never enabled");
    Check(items.value->SelectItem(0,SVSI_SELECT)==E_INVALIDARG,"ordinary select reaches bounds validation rather than being mistaken for edit");
    proof::Com<IEnumIDList> enumeration;Check(items.value->Items(SVGIO_ALLVIEW,IID_PPV_ARGS(&enumeration.value))==S_OK,"memory-only enumeration interface");
    ULONG fetched=9;Check(enumeration.value->Next(1,&absent,&fetched)==S_FALSE&&fetched==0&&!absent,"empty memory enumeration");
    proof::Com<IShellFolder2> folder;Check(fixture.view.value->GetItemObject(SVGIO_BACKGROUND,IID_PPV_ARGS(&folder.value))==S_OK,"background folder identity");
    Check(folder.value==static_cast<IShellFolder2*>(fixture.folder.value),"original Folder is reused");folder.value->Release();folder.value=nullptr;
    MSG refresh{};refresh.hwnd=window;refresh.message=WM_KEYDOWN;refresh.wParam=VK_F5;
    Check(fixture.view.value->TranslateAccelerator(&refresh)==S_OK&&queries==0,"hidden F5 never waits for or starts IPC");
    Check(fixture.view.value->SaveViewState()==E_NOTIMPL,"unsupported persistence reported");
    enumeration.value->Release();enumeration.value=nullptr;items.value->Release();items.value=nullptr;
    fixture.browser.value->active=nullptr;
    // Exercise the real View's last reference with a live Surface. No explicit Destroy first.
    const auto remaining=fixture.view.value->Release();fixture.view.value=nullptr;
    Check(remaining==0&&!IsWindow(window)&&loading::ActiveViews()==before,"last Release retires Surface before refcount zero");
    Eventually([&]{return References(static_cast<IShellFolder2*>(fixture.folder.value))==1&&References(static_cast<IShellBrowser*>(fixture.browser.value))==1&&UiaIdle();},"STA owner/browser and asynchronous provider retirement reclaimed exactly once");
}
void QuotaAndExternalDestroy(){
    const auto before=loading::ActiveViews();Check(before==0,"test starts with no active views");
    std::array<std::unique_ptr<Fixture>,5> fixtures;
    for(UINT i=0;i<5;++i)fixtures[i]=std::make_unique<Fixture>();
    std::array<HWND,4> windows{};for(UINT i=0;i<4;++i)windows[i]=fixtures[i]->Open();
    RECT bounds{0,0,600,400};FOLDERSETTINGS settings{FVM_ICON,0};HWND denied=nullptr;
    Check(fixtures[4]->view.value->CreateViewWindow(nullptr,&settings,fixtures[4]->browser.value,&bounds,&denied)==HRESULT_FROM_WIN32(ERROR_BUSY)&&!denied,"fifth view fails without passive fallback");
    DestroyWindow(windows[0]);Check(loading::ActiveViews()==3,"external HWND destruction immediately releases shared slot");
    Check(fixtures[0]->view.value->DestroyViewWindow()==S_OK&&loading::ActiveViews()==3,"Destroy is idempotent after external retirement");
    const auto reused=fixtures[4]->Open();Check(reused&&loading::ActiveViews()==4,"released slot is reusable");
    for(auto& fixture:fixtures)fixture.reset();Eventually([]{return loading::ActiveViews()==0&&!gallery::PendingWork()&&UiaIdle();},"all views, providers and workers retired");
}
void PublicNotification(){
    NotificationFolder directory;Fixture fixture(directory.pidl.value);fixture.Open();
    fixture.browser.value->duringBrowse=[&]{proof::Com<IFolderView> items;Check(fixture.view.value->QueryInterface(IID_PPV_ARGS(&items.value))==S_OK,"notify view query");int count=-1;Check(items.value->ItemCount(SVGIO_ALLVIEW,&count)==S_OK&&count==0,"notify clears memory before browser call");};
    SHChangeNotify(SHCNE_UPDATEDIR,SHCNF_IDLIST|SHCNF_FLUSHNOWAIT,directory.pidl.value,nullptr);
    const auto until=GetTickCount64()+2000;while(!fixture.browser.value->calls&&GetTickCount64()<until)Pump(10);
    Check(fixture.browser.value->calls>0&&fixture.browser.value->flags==(SBSP_ABSOLUTE|SBSP_SAMEBROWSER)
        &&ILIsEqual(fixture.browser.value->last,directory.pidl.value),"real NewDelivery notification routes to exact owned temporary root");
    Check(!queries,"notification on hidden view does not query storage");
}
void DestroyReentry(){
    Fixture fixture;const auto window=fixture.Open();HRESULT recreate=S_OK;
    fixture.browser.value->duringRelease=[&]{RECT bounds{0,0,100,100};FOLDERSETTINGS settings{FVM_ICON,0};HWND rejected=nullptr;
        recreate=fixture.view.value->CreateViewWindow(nullptr,&settings,fixture.browser.value,&bounds,&rejected);Check(!rejected,"teardown cannot create another child HWND");};
    Check(fixture.view.value->DestroyViewWindow()==S_OK&&recreate==E_UNEXPECTED&&!IsWindow(window)&&loading::ActiveViews()==0,"browser release reentry cannot resurrect a retiring surface");
}
void CreationReentry(){
    Fixture fixture;proof::Com<CreationBrowser> browser;browser.value=new CreationBrowser();browser.value->parent=fixture.parent.value;
    HRESULT nested=S_OK;RECT bounds{0,0,600,400};FOLDERSETTINGS settings{FVM_ICON,0};
    browser.value->getWindow=[&]{HWND rejected=nullptr;nested=fixture.view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&rejected);Check(!rejected,"nested creation returns no HWND");};
    HWND window=nullptr;const auto before=loading::ActiveViews();
    Check(fixture.view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&window)==S_OK&&nested==E_UNEXPECTED,"GetWindow reentry cannot enter creation twice");
    Check(loading::ActiveViews()==before+1,"outer creation owns exactly one quota slot");fixture.view.value->DestroyViewWindow();
    Check(loading::ActiveViews()==before&&!IsWindow(window),"creation reentry has no quota leak");
    browser.value->adding=[&]{fixture.view.value->DestroyViewWindow();};window=nullptr;
    Check(fixture.view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&window)==E_ABORT&&!window&&loading::ActiveViews()==before,"even incoming AddRef reentry cannot reopen a canceled creation");
}
void CompareReentry(){
    {
        PageControl control;pageControl=&control;VisibleFixture fixture;bool compared=false;
        fixture.folder.value->comparing=[&]{compared=true;fixture.view.value->DestroyViewWindow();};const auto window=fixture.Open();
        Await([&]{return compared;},"compare-destroy");Check(!IsWindow(window)&&fixture.Count()==0,"CompareIDs destruction cannot republish the old page");
        Check(loading::ActiveViews()==0,"CompareIDs destruction releases quota");
    }
    {
        PageControl control;pageControl=&control;control.blockAfterFirst=true;VisibleFixture fixture;bool compared=false;
        fixture.folder.value->comparing=[&]{compared=true;Check(SUCCEEDED(fixture.view.value->Refresh()),"reentrant Refresh starts a new generation");};fixture.Open();
        Await([&]{return compared&&control.calls>=2;},"compare-refresh");Pump(20);
        Check(fixture.Count()==0&&control.canceled==0&&gallery::PendingWork()>0,"old sort cannot overwrite or cancel reentrant Loading generation");
        fixture.view.value->DestroyViewWindow();Await([&]{return gallery::PendingWork()==0;});Check(control.canceled==1,"only explicit teardown cancels the new page");
    }
}
void LoadingVisibility(){
    PageControl control;pageControl=&control;control.blockAll=true;VisibleFixture fixture;const auto window=fixture.Open();
    Await([&]{return control.calls==1;},"loading-start");Check(fixture.Count()==0,"Loading has zero image candidates");
    fixture.view.value->UIActivate(SVUIA_DEACTIVATE);Pump(10);Check(IsWindowVisible(window)&&control.canceled==0,"focus loss preserves visible Loading work");
    ShowWindow(window,SW_HIDE);Await([&]{return control.canceled==1&&gallery::PendingWork()==0;},"loading-hide");Check(fixture.Count()==0,"hide cancels Loading even with unchanged empty viewport");
    control.blockAll=false;ShowWindow(window,SW_SHOWNA);Await([&]{return fixture.Count()==2;},"loading-show");Await([]{return gallery::PendingWork()==0;});
    Check(control.calls>=2&&!control.uiCalls,"show revalidates through background source");
    Check(fixture.items.value->SelectItem(0,SVSI_SELECT|SVSI_FOCUSED)==S_OK,"ordinary selection succeeds on a real populated View");
    int selected=0;Check(fixture.items.value->ItemCount(SVGIO_SELECTION,&selected)==S_OK&&selected==1,"selection is visible through IFolderView");
    proof::Item item;Check(fixture.items.value->Item(1,&item.value)==S_OK&&fixture.view.value->SelectItem(item.value,SVSI_SELECT|SVSI_DESELECTOTHERS)==S_OK,"PIDL selection reuses current memory identity");
    fixture.view.value->DestroyViewWindow();Await([]{return gallery::PendingWork()==0;});
}
void WideVisiblePage(){
    PageControl control;pageControl=&control;control.items=29;VisibleFixture fixture;fixture.Open(2400,1800);
    Await([&]{return fixture.Count()==29&&control.images==29&&gallery::PendingWork()==0;},"same-screen-completion-refill");
    // All images keep the placeholder's 4:3 aspect, so SetThumbnail cannot rely
    // on a changed visible set to enqueue the second batch.
    Pump(20);Check(control.images==29&&!control.uiCalls,"unchanged visible set completes every image without polling or UI IPC");
    fixture.view.value->DestroyViewWindow();Await([]{return gallery::PendingWork()==0;});
}
void BrowserStatus(){
    PageControl control;pageControl=&control;control.items=100;control.more=true;control.blockAll=true;VisibleFixture fixture;fixture.Open();
    auto& browser=*fixture.browser.value;
    Check(browser.status==snapshot::StatusText(snapshot::Status::Loading),"new active view replaces inherited status while loading");
    control.blockAll=false;Check(SUCCEEDED(fixture.view.value->Refresh()),"status ready refresh");
    Await([&]{return fixture.Count()==101;},"status-ready");
    Check(browser.status==L"当前页 100 项，已选 0 项（还有下一页）","status excludes pagination from current-page item count and inherited selection");
    Check(fixture.items.value->SelectItem(0,SVSI_SELECT)==S_OK&&fixture.items.value->SelectItem(1,SVSI_SELECT)==S_OK,"status two selections");
    Check(browser.status==L"当前页 100 项，已选 2 项（还有下一页）","status reflects current multi-selection");
    Check(fixture.items.value->SelectItem(100,SVSI_SELECT|SVSI_DESELECTOTHERS)==S_OK,"select page navigation");
    Check(browser.status==L"当前页 100 项，已选 0 项（还有下一页）","navigation selection is not an asset selection");
    browser.querying=[&]{fixture.items.value->SelectItem(0,SVSI_SELECT|SVSI_DESELECTOTHERS);};
    fixture.view.value->UIActivate(SVUIA_ACTIVATE_NOFOCUS);
    Check(browser.status==L"当前页 100 项，已选 1 项（还有下一页）","status query selection reentry publishes latest state once");
    browser.querying=[&]{browser.active=nullptr;browser.status=L"replacement view status";};
    fixture.view.value->UIActivate(SVUIA_ACTIVATE_NOFOCUS);
    Check(browser.status==L"replacement view status","old view cannot overwrite new active view after COM call-out");
    browser.active=fixture.view.value;control.blockAll=true;
    browser.querying=[&]{fixture.view.value->Refresh();};fixture.view.value->UIActivate(SVUIA_ACTIVATE_NOFOCUS);
    Check(browser.status==snapshot::StatusText(snapshot::Status::Loading),"status query Refresh reentry cannot restore old counts");
    browser.statusResult=E_NOTIMPL;Check(fixture.view.value->UIActivate(SVUIA_DEACTIVATE)==S_OK,"host status support is optional and never breaks browsing");browser.statusResult=S_OK;
    control.status=snapshot::Status::Expired;control.blockAll=false;fixture.view.value->Refresh();
    Await([&]{return fixture.Count()==0&&browser.status==snapshot::StatusText(snapshot::Status::Expired);},"status-expired");
    fixture.view.value->DestroyViewWindow();Check(browser.status.empty(),"active retiring view clears its status");Await([]{return gallery::PendingWork()==0;});
    VisibleFixture inactive;inactive.Open();auto& inactiveBrowser=*inactive.browser.value;inactiveBrowser.active=nullptr;inactiveBrowser.status=L"new active status";
    inactive.view.value->DestroyViewWindow();Check(inactiveBrowser.status==L"new active status","inactive retirement does not clear replacement status");
}
class UiaClient final {
    std::thread worker_;
public:
    std::atomic_int stage{0};
    std::atomic_bool release{false};
    std::string error;
    bool rootRejected=false,childRejected=false,selectionRejected=false;
    explicit UiaClient(HWND canvas,bool hasChild=true){
        worker_=std::thread([this,canvas,hasChild]{
            const auto initialized=CoInitializeEx(nullptr,COINIT_MULTITHREADED);
            if(FAILED(initialized)){error="MTA initialization";stage=3;return;}
            try{
                proof::Com<IUIAutomation> automation;proof::Com<IUIAutomationElement> root,child;
                proof::Com<IUIAutomationCondition> condition;proof::Com<IUIAutomationElementArray> children;
                proof::Com<IUIAutomationSelectionPattern> selection;
                Check(SUCCEEDED(CoCreateInstance(CLSID_CUIAutomation,nullptr,CLSCTX_INPROC_SERVER,IID_PPV_ARGS(&automation.value))),"MTA UIA client");
                Check(SUCCEEDED(automation.value->ElementFromHandle(canvas,&root.value)),"actual View UIA root");
                CONTROLTYPEID type=0;Check(SUCCEEDED(root.value->get_CurrentControlType(&type))&&type==UIA_ListControlTypeId,"actual View native UIA List");
                Check(SUCCEEDED(root.value->GetCurrentPatternAs(UIA_SelectionPatternId,IID_PPV_ARGS(&selection.value))),"actual View Selection pattern");
                if(hasChild){
                    VARIANT item{};item.vt=VT_I4;item.lVal=UIA_ListItemControlTypeId;
                    Check(SUCCEEDED(automation.value->CreatePropertyCondition(UIA_ControlTypePropertyId,item,&condition.value)),"UIA current-page condition");
                    Check(SUCCEEDED(root.value->FindAll(TreeScope_Children,condition.value,&children.value)),"actual View UIA children");
                    int count=0;Check(SUCCEEDED(children.value->get_Length(&count))&&count==2,"actual View exposes two synthetic items");
                    Check(SUCCEEDED(children.value->GetElement(0,&child.value)),"retain actual View UIA child");
                }
                stage=1;const auto until=GetTickCount64()+5000;while(!release&&GetTickCount64()<until)Sleep(1);Check(release,"UIA fixture retirement signal");
                BSTR name=nullptr;const auto rootResult=root.value->get_CurrentName(&name);rootRejected=FAILED(rootResult)&&(!name||SysStringLen(name)==0);SysFreeString(name);
                childRejected=!hasChild;
                if(child.value){name=nullptr;const auto childResult=child.value->get_CurrentName(&name);childRejected=FAILED(childResult)&&(!name||SysStringLen(name)==0);SysFreeString(name);}
                proof::Com<IUIAutomationElementArray> selected;const auto selectedResult=selection.value->GetCurrentSelection(&selected.value);
                int selectedCount=-1;if(selected.value)selected.value->get_Length(&selectedCount);
                selectionRejected=!selected.value||selectedCount==0;
                if(!selectionRejected)fprintf(stderr,"retired UIA selection hr=0x%08lx count=%d\n",static_cast<ULONG>(selectedResult),selectedCount);
            }catch(const std::exception& failure){error=failure.what();}
            CoUninitialize();stage=3;
        });
    }
    ~UiaClient(){release=true;const auto until=GetTickCount64()+6000;while(stage!=3&&GetTickCount64()<until)Pump(5);if(stage!=3)TerminateProcess(GetCurrentProcess(),71);worker_.join();}
    void Held(){Eventually([&]{return stage!=0;},"MTA UIA acquisition completes");Check(stage==1&&error.empty(),error.empty()?"UIA client holds root and child":error.c_str());}
    void Retired(){release=true;Eventually([&]{return stage==3;},"MTA UIA retired queries complete");Check(error.empty(),error.c_str());Check(rootRejected&&childRejected&&selectionRejected,"retired UIA root/child CurrentName and CurrentSelection expose no old data");}
};
void ViewUiaRetirement(){
    for(const bool explicitDestroy:{false,true}){
        PageControl control;pageControl=&control;VisibleFixture fixture;const auto window=fixture.Open();Await([&]{return fixture.Count()==2&&gallery::PendingWork()==0;});
        const auto canvas=FindWindowExW(window,nullptr,L"STATIC",nullptr);Check(canvas!=nullptr,"actual View canvas");
        UiaClient client(canvas);client.Held();
        Check(References(fixture.view.value)==2&&References(static_cast<IShellFolder2*>(fixture.folder.value))>2,"external UIA holds only independent Folder, never View");
        fixture.items.value->Release();fixture.items.value=nullptr;
        if(explicitDestroy)Check(fixture.view.value->DestroyViewWindow()==S_OK&&References(fixture.view.value)==1,"explicit Destroy keeps only caller View reference before UIA client releases");
        const auto remaining=fixture.view.value->Release();fixture.view.value=nullptr;fixture.browser.value->active=nullptr;
        Check(remaining==0&&!IsWindow(window)&&loading::ActiveViews()==0,"View last Release and slot retire while external root and child are still held");
        client.Retired();
        Eventually([&]{return UiaIdle()&&References(static_cast<IShellFolder2*>(fixture.folder.value))==1&&References(static_cast<IShellBrowser*>(fixture.browser.value))==1&&gallery::PendingWork()==0;},"all Folder/browser leases, providers, retirements and dispatcher HWNDs return to baseline");
    }
}
void ProductionRoute(const wchar_t* path){
    proof::Library library(path,product::ClassId);auto root=library.Root();
    proof::Item entry(snapshot::MakePidl({{301},{302},snapshot::Kind::Library,L"library route"}));auto child=proof::Bind(root.value,entry);
    proof::Com<IShellView> view;Check(child.value->CreateViewObject(nullptr,IID_PPV_ARGS(&view.value))==S_OK,"production non-root view");
    proof::Com<IUnknown> defView;Check(view.value->QueryInterface(IID_CDefView,reinterpret_cast<void**>(&defView.value))==E_NOINTERFACE,"production child selects custom view");
    proof::Com<IFolderView> folderView;Check(view.value->QueryInterface(IID_PPV_ARGS(&folderView.value))==S_OK,"production custom IFolderView");
    folderView.value->Release();folderView.value=nullptr;view.value->Release();view.value=nullptr;child.value->Release();child.value=nullptr;root.value->Release();root.value=nullptr;
    Check(library.canUnload()==S_OK,"unopened product gallery releases Folder ownership");
}
void ProductionUiaDllLifetime(const wchar_t* path){
    proof::Library library(path,product::ClassId);auto root=library.Root();
    proof::Item entry(snapshot::MakePidl({{301},{302},snapshot::Kind::Library,L"hidden DLL lifecycle fixture"}));auto child=proof::Bind(root.value,entry);
    HiddenWindow parent(false,true);proof::Com<CreationBrowser> browser;browser.value=new CreationBrowser();browser.value->parent=parent.value;
    proof::Com<IShellView> view;Check(child.value->CreateViewObject(nullptr,IID_PPV_ARGS(&view.value))==S_OK,"actual DLL gallery view");browser.value->active=view.value;
    RECT bounds{0,0,600,400};FOLDERSETTINGS settings{FVM_ICON,0};HWND window=nullptr;
    Check(view.value->CreateViewWindow(nullptr,&settings,browser.value,&bounds,&window)==S_OK&&!IsWindowVisible(window),"hidden product view cannot start IPC");
    UiaClient client(FindWindowExW(window,nullptr,L"STATIC",nullptr),false);client.Held();
    child.value->Release();child.value=nullptr;root.value->Release();root.value=nullptr;
    Check(References(view.value)==1,"actual DLL provider never retains View");
    const auto remaining=view.value->Release();view.value=nullptr;browser.value->active=nullptr;
    Check(remaining==0&&!IsWindow(window)&&library.canUnload()==S_FALSE,"actual product View retires before client release while provider still pins DLL");
    client.Retired();Eventually([&]{return library.canUnload()==S_OK&&References(static_cast<IShellBrowser*>(browser.value))==1;},"actual product DLL becomes unloadable after UIA retirement and all client releases");
}
}
int wmain(int argc,wchar_t** argv){
    if(argc!=2)return 2;if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    int result=0;try{InterfacesAndFinalRelease();QuotaAndExternalDestroy();PublicNotification();DestroyReentry();CreationReentry();CompareReentry();LoadingVisibility();WideVisiblePage();BrowserStatus();ViewUiaRetirement();ProductionRoute(argv[1]);ProductionUiaDllLifetime(argv[1]);Check(!cleanupFailed,"temporary notification directory removed");
        puts("gallery_view=passed; last_Release=retired_before_zero; external_destroy=idempotent; shared_quota=4; public_notification=NewDelivery; root_navigation=same_browser; hidden_IPC=0; registry_writes=0; real_Explorer=0");
    }catch(const std::exception& error){fprintf(stderr,"GalleryViewTests: %s\n",error.what());result=1;}
    CoUninitialize();return result;
}
