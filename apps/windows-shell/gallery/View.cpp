#include "View.h"
#include "../LoadingRefresh.h"
#include "../SnapshotPidl.h"
#include "../SettingsCommand.h"
#include <shlwapi.h>
#include <shellapi.h>
#include <algorithm>
#include <array>
#include <atomic>
#include <cstring>
#include <new>
#include <utility>

namespace gallery {
namespace {
constexpr UINT CompletionMessage=WM_APP+0x321;
std::atomic<std::uint64_t> nextGeneration{1};
std::uint64_t Generation() noexcept {auto value=nextGeneration.fetch_add(1);return value?value:nextGeneration.fetch_add(1);}
template<class T> struct Reference {
    T* value;
    explicit Reference(T* object):value(object){if(value)value->AddRef();}
    ~Reference(){if(value)value->Release();}
};
template<class T> struct Out {
    T* value=nullptr;
    ~Out(){if(value)value->Release();}
};
struct Pidl {
    PIDLIST_RELATIVE value=nullptr;
    explicit Pidl(PIDLIST_RELATIVE item=nullptr):value(item){}
    ~Pidl(){CoTaskMemFree(value);}
    Pidl(Pidl&& other)noexcept:value(std::exchange(other.value,nullptr)){}
    Pidl(const Pidl&)=delete;Pidl& operator=(const Pidl&)=delete;
};
class Items final:public IEnumIDList {
    ULONG refs_=1,cursor_=0;
    IUnknown* owner_;
    std::vector<snapshot::Entry> entries_;
public:
    Items(IUnknown* owner,std::vector<snapshot::Entry> entries):owner_(owner),entries_(std::move(entries)){owner_->AddRef();}
    ~Items(){owner_->Release();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;if(iid!=IID_IUnknown&&iid!=IID_IEnumIDList)return E_NOINTERFACE;*result=static_cast<IEnumIDList*>(this);AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {const auto refs=--refs_;if(!refs)delete this;return refs;}
    HRESULT STDMETHODCALLTYPE Next(ULONG count,PITEMID_CHILD* items,ULONG* fetched) override {
        if(fetched)*fetched=0;if(!items||(!fetched&&count!=1))return E_POINTER;
        ULONG taken=0;while(taken<count&&cursor_<entries_.size()){
            auto item=snapshot::MakePidl(entries_[cursor_]);
            if(!item){while(taken){CoTaskMemFree(items[--taken]);items[taken]=nullptr;--cursor_;}return E_OUTOFMEMORY;}
            items[taken++]=item;++cursor_;
        }
        if(fetched)*fetched=taken;return taken==count?S_OK:S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE Skip(ULONG count) override {ULONG taken=0;while(taken<count&&cursor_<entries_.size()){++taken;++cursor_;}return taken==count?S_OK:S_FALSE;}
    HRESULT STDMETHODCALLTYPE Reset() override {cursor_=0;return S_OK;}
    HRESULT STDMETHODCALLTYPE Clone(IEnumIDList** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        try{auto item=new(std::nothrow) Items(owner_,entries_);if(!item)return E_OUTOFMEMORY;item->cursor_=cursor_;*result=item;return S_OK;}
        catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
    }
};
class View final:public IShellView,public IFolderView {
    ULONG refs_=1;
    const DWORD thread_=GetCurrentThreadId();
    IShellFolder2* folder_;
    IShellBrowser* browser_=nullptr;
    PIDLIST_ABSOLUTE absolute_=nullptr,root_=nullptr;
    snapshot::Location location_;
    static constexpr UINT MaxPageLocations=64;
    std::array<snapshot::Location,MaxPageLocations> pageLocations_{};
    snapshot::Location pageTarget_{};
    UINT pageCount_=0,pageIndex_=0,targetPage_=0;
    bool targetPending_=false,pageWithoutFiles_=false,crossPagePreview_=false;
    int pendingPreviewDirection_=0;
    Requests requests_;
    PreferenceStore preferences_;
    Surface* surface_=nullptr;
    snapshot::Page page_;
    std::uint64_t generation_=Generation();
    ULONG notification_=0;
    bool slot_=false,creating_=false,destroying_=false,visible_=false,refreshing_=false,retired_=false;
    bool statusUpdating_=false;
    bool preferenceSaveFailed_=false;
    int previewIndex_=-1;
    std::uint64_t previewSerial_=0;
    std::uint64_t statusRevision_=0;
    UINT active_=SVUIA_DEACTIVATE;
    bool OnThread()const noexcept {return GetCurrentThreadId()==thread_;}
    HWND Window()const noexcept {return surface_?surface_->Window():nullptr;}
    bool IsVisibleIntent()const noexcept {
        const auto window=Window();
        // WM_SHOWWINDOW is delivered before the HWND's own style changes.
        // Surface has already committed that intent; ancestors still use native visibility.
        return window&&surface_->Shown()&&IsWindowVisible(GetParent(window))!=FALSE;
    }
    bool Live(HWND window,std::uint64_t generation)const noexcept {return window&&Window()==window&&IsWindow(window)&&generation_==generation&&!destroying_;}
    void ReleaseSlot() noexcept {if(slot_){slot_=false;loading::ReleaseViewSlot();}}
    void ResetPages() noexcept {
        pageLocations_.fill({});pageTarget_={};pageCount_=pageIndex_=targetPage_=0;
        targetPending_=false;pageWithoutFiles_=false;crossPagePreview_=false;pendingPreviewDirection_=0;
    }
    enum class NextPageState {Absent,Available,Cycle,Capacity};
    NextPageState NextPage(snapshot::Location& target)const noexcept {
        target={};if(page_.status!=snapshot::Status::Ready||!pageCount_)return NextPageState::Absent;
        bool found=false;
        for(const auto& entry:page_.entries)if(entry.kind==snapshot::Kind::NextPage){
            if(found||snapshot::Zero(entry.node)||entry.epoch!=page_.epoch)return NextPageState::Cycle;
            found=true;target={entry.epoch,entry.node};
        }
        if(!found)return NextPageState::Absent;
        for(UINT at=0;at<=pageIndex_;++at)if(pageLocations_[at].epoch==target.epoch&&pageLocations_[at].node==target.node)return NextPageState::Cycle;
        return pageIndex_+1>=MaxPageLocations?NextPageState::Capacity:NextPageState::Available;
    }
    bool CanStepPage(int delta)const noexcept {
        if(retired_||targetPending_||page_.status!=snapshot::Status::Ready||!pageCount_)return false;
        if(delta==-1)return pageIndex_>0;
        snapshot::Location target;return delta==1&&NextPage(target)==NextPageState::Available;
    }
    void PageButtons() noexcept {
        if(surface_)surface_->SetPageNavigation(previewIndex_<0&&CanStepPage(-1),previewIndex_<0&&CanStepPage(1));
    }
    void StepPage(int delta,int previewDirection=0) noexcept {
        Reference<View> invocation(this);
        if(!surface_||!IsVisibleIntent()||(previewDirection==0&&previewIndex_>=0)||!CanStepPage(delta))return;
        snapshot::Location target;
        if(delta<0)target=pageLocations_[pageIndex_-1];else if(NextPage(target)!=NextPageState::Available)return;
        pageTarget_=target;targetPage_=delta<0?pageIndex_-1:pageIndex_+1;targetPending_=true;
        pendingPreviewDirection_=previewDirection;pageWithoutFiles_=false;
        Refresh();
    }
    void UpdateStatus(bool clear=false) noexcept {
        ++statusRevision_;if(statusUpdating_||!browser_)return;
        statusUpdating_=true;struct Updating {bool& value;~Updating(){value=false;}} updating{statusUpdating_};
        try{
            // A host call may synchronously change selection or refresh. Retry
            // that current state once; never recursively write stale status.
            for(unsigned attempt=0;attempt<2;++attempt){
                const auto window=Window();const auto generation=generation_,revision=statusRevision_;const auto target=browser_;
                if(!target||(!clear&&(!surface_||!window||destroying_)))return;
                std::wstring text;
                if(!clear){
                    if(page_.status!=snapshot::Status::Ready)text=snapshot::StatusText(page_.status);
                    else{
                        UINT count=0,selected=0;bool more=false;
                        for(const auto& entry:page_.entries){if(entry.kind==snapshot::Kind::NextPage)more=true;else if(entry.kind!=snapshot::Kind::StatusRow)++count;}
                        const auto selection=surface_->SelectedItems();for(UINT at=0;at<selection.count;++at){const auto index=selection.indices[at];
                            if(index<page_.entries.size()&&page_.entries[index].kind!=snapshot::Kind::NextPage&&page_.entries[index].kind!=snapshot::Kind::StatusRow)++selected;}
                        text=L"当前页 "+std::to_wstring(count)+L" 项，已选 "+std::to_wstring(selected)+L" 项";
                        if(more)text+=L"（还有下一页）";
                        if(pageIndex_)text+=L" · 第 "+std::to_wstring(pageIndex_+1)+L" 页";
                        snapshot::Location next;const auto navigation=NextPage(next);
                        if(navigation==NextPageState::Cycle)text+=L" · 分页位置重复，请重新打开目录";
                        if(navigation==NextPageState::Capacity)text+=L" · 已达64页浏览上限，请重新打开目录";
                        if(pageWithoutFiles_)text+=L" · 此页没有可预览的文件，请使用翻页按钮继续浏览";
                        if(preferenceSaveFailed_)text+=L" · 浏览设置未保存";
                    }
                }
                if(surface_&&(clear||Live(window,generation)))surface_->SetStatusText(text);
                if(browser_!=target||Window()!=window||generation_!=generation||statusRevision_!=revision)continue;
                Reference<IShellBrowser> browser(target);Out<IShellView> active;
                if(browser_!=target||Window()!=window||generation_!=generation)continue;
                const auto queried=browser.value->QueryActiveShellView(&active.value);
                if(browser_!=target||Window()!=window||generation_!=generation||statusRevision_!=revision)continue;
                if(FAILED(queried)||active.value!=static_cast<IShellView*>(this))return;
                // SetStatusTextSB is the public folder-view status contract. A
                // host without it may return E_NOTIMPL; do not edit its controls.
                browser.value->SetStatusTextSB(text.c_str());
                if(clear||statusRevision_==revision)return;
            }
        }catch(const std::bad_alloc&){return;}
    }
    int Index(PCUIDLIST_RELATIVE pidl)const {
        snapshot::Entry item;if(!snapshot::ReadPidl(pidl,item))return -1;
        for(size_t index=0;index<page_.entries.size();++index){const auto& entry=page_.entries[index];
            if(entry.epoch==item.epoch&&entry.node==item.node&&entry.kind==item.kind)return static_cast<int>(index);}
        return -1;
    }
    std::uint64_t Clear(snapshot::Status status) noexcept {
        previewIndex_=-1;previewSerial_=Generation();crossPagePreview_=false;
        const auto generation=Generation();generation_=generation;requests_.Cancel(generation);page_={};page_.status=status;
        if(surface_)surface_->Clear(status,generation);if(generation_==generation)UpdateStatus();return generation;
    }
    void Root() noexcept {
        Reference<View> invocation(this);const auto window=Window();
        retired_=true;ResetPages();const auto generation=Clear(snapshot::Status::Expired);
        Reference<IShellBrowser> browser(browser_);
        if(browser.value&&Live(window,generation))browser.value->BrowseObject(root_,SBSP_ABSOLUTE|SBSP_SAMEBROWSER);
    }
    void Apply(snapshot::Page page){
        const auto window=Window();const auto generation=generation_;
        if(!Live(window,generation)||retired_)return;
        Reference<IShellFolder2> folder(folder_);
        if(page.status==snapshot::Status::AccessDenied||page.status==snapshot::Status::Expired||
            (!snapshot::Zero(page.epoch)&&page.epoch!=pageTarget_.epoch)){Root();return;}
        if(page.status==snapshot::Status::Ready){
            if(page.epoch!=location_.epoch){Root();return;}
            std::vector<Pidl> ids;std::vector<size_t> order;ids.reserve(page.entries.size());order.reserve(page.entries.size());
            for(size_t index=0;index<page.entries.size();++index){ids.emplace_back(snapshot::MakePidl(page.entries[index]));if(!ids.back().value)throw std::bad_alloc();order.push_back(index);}
            // Folder's current-page display rules remain authoritative; no second sorter.
            std::stable_sort(order.begin(),order.end(),[&](size_t a,size_t b){
                if(!Live(window,generation)||retired_)throw HRESULT(E_ABORT);
                const auto result=folder.value->CompareIDs(0,ids[a].value,ids[b].value);
                if(!Live(window,generation)||retired_)throw HRESULT(E_ABORT);
                if(FAILED(result))throw result;return static_cast<SHORT>(HRESULT_CODE(result))<0;
            });
            std::vector<snapshot::Entry> sorted;sorted.reserve(order.size());for(auto index:order)sorted.push_back(std::move(page.entries[index]));page.entries=std::move(sorted);
        }
        if(!Live(window,generation)||retired_)return;
        int previewDirection=0;
        if(page.status==snapshot::Status::Ready){
            if(!targetPending_||targetPage_>=MaxPageLocations){Clear(snapshot::Status::InvalidResponse);return;}
            pageLocations_[targetPage_]=pageTarget_;pageIndex_=targetPage_;pageCount_=pageIndex_+1;
            // Requerying a page issues new opaque children: never reuse an old forward chain.
            for(UINT at=pageCount_;at<MaxPageLocations;++at)pageLocations_[at]={};
            targetPending_=false;previewDirection=std::exchange(pendingPreviewDirection_,0);pageWithoutFiles_=false;
        }
        const auto automaticPreview=previewSerial_;
        page_=std::move(page);
        if(surface_){const auto result=surface_->SetPage(page_,generation);
            if(FAILED(result)&&Live(window,generation)){Clear(snapshot::Status::Unavailable);return;}}
        if(!Live(window,generation)||retired_)return;
        if(previewDirection&&previewSerial_==automaticPreview){
            const auto index=AdjacentFile(previewDirection>0?-1:static_cast<int>(page_.entries.size()),previewDirection);
            if(index>=0){
                crossPagePreview_=true;
                surface_->SelectItem(static_cast<UINT>(index),SVSI_SELECT|SVSI_FOCUSED|SVSI_DESELECTOTHERS|SVSI_ENSUREVISIBLE);
                if(Live(window,generation)&&!retired_&&previewSerial_==automaticPreview)OpenPreview(static_cast<UINT>(index));
                return;
            }
            pageWithoutFiles_=true;
        }
        UpdateStatus();if(!Live(window,generation)||retired_)return;
        PageButtons();if(Live(window,generation)&&!retired_)Viewport();
    }
    LRESULT Completed(WPARAM value,LPARAM data) noexcept {
        Reference<View> invocation(this);
        if(data){
            PIDLIST_ABSOLUTE* items=nullptr;LONG event=0;
            const auto lock=SHChangeNotification_Lock(reinterpret_cast<HANDLE>(value),static_cast<DWORD>(data),&items,&event);
            if(!lock)return 0;
            UINT bytes=0,count=0,rootBytes=0,rootCount=0;
            const bool changed=event==SHCNE_UPDATEDIR&&items&&snapshot::BoundedList(items[0],bytes,count)
                &&snapshot::BoundedList(root_,rootBytes,rootCount)&&bytes==rootBytes&&std::memcmp(items[0],root_,bytes)==0;
            SHChangeNotification_Unlock(lock);if(changed)Root();return 0;
        }
        if(value!=generation_||!Window())return 0;
        const auto processingWindow=Window();const auto processingGeneration=generation_;
        try{
            auto completed=requests_.Take();
            for(auto& item:completed){
                if(item.generation!=generation_||!Window()||!requests_.Current(item))continue;
                if(item.page){Apply(std::move(item.snapshot));continue;}
                if(item.thumbnail.status==thumbnail::Status::AccessDenied||item.thumbnail.status==thumbnail::Status::Expired){Root();break;}
                if(page_.status!=snapshot::Status::Ready||item.index>=page_.entries.size())continue;
                const auto& entry=page_.entries[item.index];
                if(item.thumbnail.image&&(item.thumbnail.location.epoch!=entry.epoch||item.thumbnail.location.node!=entry.node))continue;
                if(item.preview){
                    if(previewIndex_!=static_cast<int>(item.index))continue;
                    const auto serial=previewSerial_;
                    const wchar_t* text=L"暂时无法显示图片，可返回后重试";
                    switch(item.thumbnail.status){
                    case thumbnail::Status::Ready:text=item.thumbnail.image?L"预览图已就绪":L"暂时无法显示图片";break;
                    case thumbnail::Status::Unsupported:text=L"此文件暂不支持图片预览";break;
                    case thumbnail::Status::Busy:text=L"图片服务繁忙，可返回后重试";break;
                    case thumbnail::Status::InvalidResponse:text=L"图片数据校验失败";break;
                    default:break;
                    }
                    UINT ordinal=0,total=0;
                    for(UINT index=0;index<page_.entries.size();++index)if(page_.entries[index].kind==snapshot::Kind::File){++total;if(index<=item.index)++ordinal;}
                    const auto status=L"本页文件 "+std::to_wstring(ordinal)+L"/"+std::to_wstring(total)+L" · "+text;
                    surface_->SetPreview(item.index,serial,std::move(item.thumbnail.image),status);
                }else if(previewIndex_<0)surface_->SetThumbnail(item.index,generation_,std::move(item.thumbnail.image));
            }
        }catch(const std::bad_alloc&){if(Live(processingWindow,processingGeneration))Clear(snapshot::Status::Unavailable);}
        catch(HRESULT error){if(error!=E_ABORT&&Live(processingWindow,processingGeneration))Clear(snapshot::Status::InvalidResponse);}
        if(Live(processingWindow,processingGeneration)&&!retired_)Viewport();
        return 0;
    }
    void Viewport() noexcept {
        if(creating_||destroying_||!surface_)return;
        const auto window=surface_->Window();if(!window){DestroyViewWindow();return;}
        const bool visible=IsVisibleIntent();
        if(!visible){
            if(visible_){visible_=false;ResetPages();Clear(snapshot::Status::Unavailable);}
            else requests_.Visible(page_,{},generation_);
            return;
        }
        if(!visible_){visible_=true;if(!refreshing_&&!retired_){Refresh();return;}}
        if(retired_||page_.status!=snapshot::Status::Ready)return;
        if(previewIndex_>=0)requests_.Preview(page_,static_cast<UINT>(previewIndex_),generation_);
        else requests_.Visible(page_,surface_->VisibleFileItems(),generation_);
    }
    void Focused() noexcept {
        if(active_==SVUIA_ACTIVATE_FOCUS)return;active_=SVUIA_ACTIVATE_FOCUS;
        Reference<IShellBrowser> browser(browser_);if(browser.value)browser.value->OnViewWindowActive(this);
        UpdateStatus();
    }
    void SelectionChanged() noexcept {
        const auto window=Window();const auto generation=generation_;Reference<IShellBrowser> browser(browser_);Out<ICommDlgBrowser> common;
        if(browser.value&&SUCCEEDED(browser.value->QueryInterface(IID_PPV_ARGS(&common.value)))&&Live(window,generation))common.value->OnStateChange(this,CDBOSC_SELCHANGE);
        if(Live(window,generation))UpdateStatus();
    }
    void Activate(UINT index) noexcept {
        if(page_.status==snapshot::Status::Ready&&index<page_.entries.size()&&page_.entries[index].kind==snapshot::Kind::File){OpenPreview(index);return;}
        if(index<page_.entries.size()&&page_.entries[index].kind==snapshot::Kind::NextPage){StepPage(1);return;}
        if(index>=page_.entries.size()||!snapshot::Navigable(page_.entries[index].kind))return;
        try{
            const auto window=Window();const auto generation=generation_;
            Pidl item(snapshot::MakePidl(page_.entries[index]));if(!item.value)return;PCUITEMID_CHILD raw=item.value;Out<IContextMenu> menu;
            if(FAILED(Menu(1,&raw,IID_PPV_ARGS(&menu.value)))||!Live(window,generation))return;
            CMINVOKECOMMANDINFO info{sizeof(info)};info.hwnd=window;info.lpVerb=MAKEINTRESOURCEA(0);info.nShow=SW_SHOWNORMAL;menu.value->InvokeCommand(&info);
        }catch(const std::bad_alloc&){return;}
    }
    int AdjacentFile(int index,int delta)const noexcept {
        if(delta!=-1&&delta!=1)return -1;
        for(int candidate=index+delta;candidate>=0&&candidate<static_cast<int>(page_.entries.size());candidate+=delta)
            if(page_.entries[static_cast<size_t>(candidate)].kind==snapshot::Kind::File)return candidate;
        return -1;
    }
    void OpenPreview(UINT index) noexcept {
        Reference<View> invocation(this);
        if(!surface_||!IsVisibleIntent()||retired_||page_.status!=snapshot::Status::Ready||index>=page_.entries.size()||page_.entries[index].kind!=snapshot::Kind::File)return;
        const auto window=Window();const auto generation=generation_,serial=Generation();
        previewIndex_=static_cast<int>(index);previewSerial_=serial;
        requests_.Visible(page_,{},generation);
        const auto hr=surface_->BeginPreview(index,serial,AdjacentFile(previewIndex_,-1)>=0||CanStepPage(-1),AdjacentFile(previewIndex_,1)>=0||CanStepPage(1));
        if(!Live(window,generation)||previewSerial_!=serial)return;
        if(FAILED(hr)){ClosePreview();return;}
        Viewport();
    }
    void ClosePreview() noexcept {
        Reference<View> invocation(this);
        pendingPreviewDirection_=0;
        if(!surface_||previewIndex_<0){previewSerial_=Generation();return;}
        const auto window=Window();const auto generation=generation_;const auto focused=previewIndex_;const auto cross=crossPagePreview_;
        previewIndex_=-1;previewSerial_=Generation();crossPagePreview_=false;requests_.Visible(page_,{},generation);
        surface_->EndPreview();if(!Live(window,generation)||retired_)return;
        if(cross)surface_->SelectItem(static_cast<UINT>(focused),SVSI_SELECT|SVSI_FOCUSED|SVSI_DESELECTOTHERS|SVSI_ENSUREVISIBLE);
        if(!Live(window,generation)||retired_)return;PageButtons();
        if(Live(window,generation)){Viewport();if(Live(window,generation))UpdateStatus();}
    }
    void StepPreview(int delta) noexcept {
        if(previewIndex_<0||(delta!=-1&&delta!=1))return;
        const auto next=AdjacentFile(previewIndex_,delta);
        if(next>=0)OpenPreview(static_cast<UINT>(next));else StepPage(delta,delta);
    }
    void PreferencesChanged() noexcept {
        if(creating_||destroying_||!surface_)return;
        const preferences::Values value{surface_->CurrentMode(),surface_->Density()};
        preferenceSaveFailed_=!preferences_.save||FAILED(preferences_.save(value));UpdateStatus();
    }
    HRESULT Menu(UINT count,PCUITEMID_CHILD_ARRAY items,REFIID iid,void** result){
        auto hr=count?folder_->GetUIObjectOf(Window(),count,items,iid,nullptr,result):folder_->CreateViewObject(Window(),iid,result);
        if(SUCCEEDED(hr)&&*result){Out<IObjectWithSite> site;auto object=static_cast<IUnknown*>(*result);
            if(SUCCEEDED(object->QueryInterface(IID_PPV_ARGS(&site.value))))site.value->SetSite(browser_);}
        return hr;
    }
    void Context(int index,POINT point) noexcept {
        if(!surface_)return;
        if(index>=0){const auto selected=surface_->SelectedItems();bool contained=false;for(UINT i=0;i<selected.count;++i)contained=contained||selected.indices[i]==static_cast<UINT>(index);
            if(!contained)surface_->SelectItem(static_cast<UINT>(index),SVSI_SELECT|SVSI_FOCUSED|SVSI_DESELECTOTHERS);}
        if(!surface_)return;
        Out<IContextMenu> menu;if(FAILED(GetItemObject(index<0?SVGIO_BACKGROUND:SVGIO_SELECTION,IID_PPV_ARGS(&menu.value))))return;
        const auto window=Window();const auto generation=generation_;HMENU popup=CreatePopupMenu();if(!popup)return;
        if(SUCCEEDED(menu.value->QueryContextMenu(popup,0,1,0x7fff,CMF_NORMAL))){
            // TPM_RETURNCMD changes the nominal BOOL return into a command identifier.
            const UINT command=static_cast<UINT>(TrackPopupMenuEx(popup,TPM_RETURNCMD|TPM_RIGHTBUTTON,point.x,point.y,Window(),nullptr));
            if(command&&Live(window,generation)){CMINVOKECOMMANDINFO info{sizeof(info)};info.hwnd=window;info.lpVerb=MAKEINTRESOURCEA(command-1);info.nShow=SW_SHOWNORMAL;menu.value->InvokeCommand(&info);}
        }
        DestroyMenu(popup);
    }
public:
    View(IShellFolder2* folder,PCIDLIST_ABSOLUTE absolute,snapshot::Location location,Sources sources,PCIDLIST_ABSOLUTE notificationRoot,PreferenceStore preferences)
        :folder_(folder),location_(location),requests_(sources),preferences_(preferences){
        absolute_=ILCloneFull(absolute);root_=notificationRoot?ILCloneFull(notificationRoot):ILCloneFirst(absolute);
        if(!absolute_||!root_){CoTaskMemFree(absolute_);CoTaskMemFree(root_);throw std::bad_alloc();}folder_->AddRef();
    }
    ~View(){requests_.Close();CoTaskMemFree(root_);CoTaskMemFree(absolute_);folder_->Release();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown||iid==IID_IShellView||iid==IID_IOleWindow)*result=static_cast<IShellView*>(this);
        else if(iid==IID_IFolderView)*result=static_cast<IFolderView*>(this);else return E_NOINTERFACE;AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {
        // Tear down the borrowed-owner HWND while the last real COM reference is
        // still alive, so Surface's reentrant callbacks can safely retain it.
        if(refs_==1&&surface_&&!destroying_)DestroyViewWindow();
        const auto refs=--refs_;if(!refs)delete this;return refs;
    }
    HRESULT STDMETHODCALLTYPE GetWindow(HWND* result) override {if(!result)return E_POINTER;*result=nullptr;if(!OnThread())return RPC_E_WRONG_THREAD;*result=Window();return *result?S_OK:E_FAIL;}
    HRESULT STDMETHODCALLTYPE ContextSensitiveHelp(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE TranslateAccelerator(MSG* message) override {
        if(!OnThread())return RPC_E_WRONG_THREAD;if(!message)return E_POINTER;Reference<View> invocation(this);
        if(message->message==WM_KEYDOWN&&message->wParam==VK_F5){Refresh();return S_OK;}
        if(message->message==WM_KEYDOWN&&message->wParam==VK_ESCAPE&&pendingPreviewDirection_&&
            !(GetKeyState(VK_CONTROL)&0x8000)&&!(GetKeyState(VK_MENU)&0x8000)&&
            (message->hwnd==Window()||IsChild(Window(),message->hwnd))){ClosePreview();return S_OK;}
        return surface_&&surface_->TranslateAccelerator(*message)?S_OK:S_FALSE;
    }
    HRESULT STDMETHODCALLTYPE EnableModeless(BOOL) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE UIActivate(UINT state) override {
        if(!OnThread())return RPC_E_WRONG_THREAD;Reference<View> invocation(this);active_=state;
        // Losing focus to the tree/address bar does not hide a visible view.
        if(state==SVUIA_ACTIVATE_FOCUS){Reference<IShellBrowser> browser(browser_);if(browser.value)browser.value->OnViewWindowActive(this);if(surface_)surface_->Focus();}
        UpdateStatus();
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Refresh() override {
        if(!OnThread())return RPC_E_WRONG_THREAD;if(!surface_)return E_UNEXPECTED;Reference<View> invocation(this);
        if(refreshing_)return S_FALSE;refreshing_=true;retired_=false;
        if(!targetPending_){pageTarget_=pageCount_?pageLocations_[pageIndex_]:location_;targetPage_=pageCount_?pageIndex_:0;targetPending_=true;pendingPreviewDirection_=0;}
        pageWithoutFiles_=false;
        const auto window=Window();const auto generation=Clear(snapshot::Status::Loading);
        if(!Live(window,generation)||retired_){refreshing_=false;return E_ABORT;}
        HRESULT hr=S_OK;
        if(IsVisibleIntent()){visible_=true;hr=requests_.Begin(pageTarget_,generation);}
        if(FAILED(hr)&&Live(window,generation))Clear(hr==HRESULT_FROM_WIN32(ERROR_BUSY)?snapshot::Status::Busy:snapshot::Status::Unavailable);
        refreshing_=false;return hr;
    }
    HRESULT STDMETHODCALLTYPE CreateViewWindow(IShellView*,LPCFOLDERSETTINGS,IShellBrowser* browser,RECT* bounds,HWND* result) override {
        if(!result)return E_POINTER;*result=nullptr;if(!OnThread())return RPC_E_WRONG_THREAD;if(!browser||!bounds)return E_POINTER;if(surface_||creating_||destroying_)return E_UNEXPECTED;
        Reference<View> invocation(this);creating_=true;
        struct Creating {bool& flag;~Creating(){flag=false;}} creating{creating_};
        const auto creation=generation_;Reference<IShellBrowser> incoming(browser);
        if(generation_!=creation||destroying_)return E_ABORT;
        ULONG previous=0;if(!loading::TryReserveViewSlot(previous))return HRESULT_FROM_WIN32(ERROR_BUSY);slot_=true;
        HWND parent=nullptr;auto hr=browser->GetWindow(&parent);if(FAILED(hr)||!parent){ReleaseSlot();return FAILED(hr)?hr:E_INVALIDARG;}
        DWORD process=0;if(!GetWindowThreadProcessId(parent,&process)||process!=GetCurrentProcessId()){ReleaseSlot();return E_ACCESSDENIED;}
        if(generation_!=creation||!slot_){ReleaseSlot();return E_ABORT;}
        browser_=browser;browser_->AddRef();
        Callbacks callbacks;callbacks.context=this;callbacks.lifetimeOwner=static_cast<IShellView*>(this);
        callbacks.providerLifetimeOwner=folder_;
        callbacks.activateItem=[](void* p,UINT index)noexcept{static_cast<View*>(p)->Activate(index);};
        callbacks.contextMenu=[](void* p,int index,POINT point)noexcept{static_cast<View*>(p)->Context(index,point);};
        callbacks.viewportChanged=[](void* p)noexcept{static_cast<View*>(p)->Viewport();};
        callbacks.selectionChanged=[](void* p)noexcept{static_cast<View*>(p)->SelectionChanged();};
        callbacks.refresh=[](void* p)noexcept{static_cast<View*>(p)->Refresh();};
        callbacks.settings=[](void*)noexcept{settings::Launch();};
        callbacks.focusActivated=[](void* p)noexcept{static_cast<View*>(p)->Focused();};
        callbacks.previewStep=[](void* p,int delta)noexcept{static_cast<View*>(p)->StepPreview(delta);};
        callbacks.previewClose=[](void* p)noexcept{static_cast<View*>(p)->ClosePreview();};
        callbacks.pageStep=[](void* p,int delta)noexcept{static_cast<View*>(p)->StepPage(delta);};
        callbacks.preferencesChanged=[](void* p)noexcept{static_cast<View*>(p)->PreferencesChanged();};
        callbacks.completionMessage=CompletionMessage;callbacks.completion=[](void* p,WPARAM w,LPARAM l)noexcept{return static_cast<View*>(p)->Completed(w,l);};
        Surface* created=nullptr;hr=Surface::Create(parent,*bounds,callbacks,&created);surface_=created;
        if(generation_!=creation)hr=E_ABORT;
        if(SUCCEEDED(hr)){
            const auto window=Window();const auto restored=preferences_.load?preferences_.load():preferences::Values{};
            // Preference sources and Surface setters are call-outs. Each may
            // synchronously retire this View; never continue through a stale HWND.
            if(Live(window,creation))surface_->SetMode(restored.mode);
            if(Live(window,creation))surface_->SetDensity(restored.densityDip);
            if(!Live(window,creation))hr=E_ABORT;
        }
        if(SUCCEEDED(hr)){
            requests_.Attach(Window(),CompletionMessage);SHChangeNotifyEntry entry{root_,TRUE};
            notification_=SHChangeNotifyRegister(Window(),SHCNRF_ShellLevel|SHCNRF_NewDelivery,SHCNE_UPDATEDIR,CompletionMessage,1,&entry);
            if(!notification_)hr=E_FAIL;
        }
        if(generation_!=creation)hr=E_ABORT;
        if(FAILED(hr)){DestroyViewWindow();return hr;}
        const auto window=Window();Refresh();
        if(!window||Window()!=window||retired_){DestroyViewWindow();return E_ABORT;}
        *result=window;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE DestroyViewWindow() override {
        if(!OnThread())return RPC_E_WRONG_THREAD;if(destroying_)return S_OK;Reference<View> invocation(this);destroying_=true;
        ResetPages();requests_.Close();generation_=Generation();previewIndex_=-1;previewSerial_=Generation();page_={};visible_=false;retired_=true;
        UpdateStatus(true);
        if(notification_){SHChangeNotifyDeregister(notification_);notification_=0;}
        auto surface=surface_;surface_=nullptr;if(surface){surface->Destroy();delete surface;}
        auto browser=browser_;browser_=nullptr;ReleaseSlot();if(browser)browser->Release();destroying_=false;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetCurrentInfo(LPFOLDERSETTINGS settings) override {if(!settings)return E_POINTER;if(!OnThread())return RPC_E_WRONG_THREAD;settings->ViewMode=surface_&&surface_->CurrentMode()==Mode::List?FVM_DETAILS:FVM_ICON;settings->fFlags=FWF_AUTOARRANGE;return S_OK;}
    HRESULT STDMETHODCALLTYPE AddPropertySheetPages(DWORD,LPFNSVADDPROPSHEETPAGE,LPARAM) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SaveViewState() override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE SelectItem(PCUITEMID_CHILD item,SVSIF flags) override {
        if(!OnThread())return RPC_E_WRONG_THREAD;if((flags&SVSI_EDIT)==SVSI_EDIT)return E_ACCESSDENIED;if(!surface_)return E_UNEXPECTED;Reference<View> invocation(this);
        try{const auto index=Index(item);return index<0?E_INVALIDARG:surface_->SelectItem(static_cast<UINT>(index),flags);}catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
    }
    HRESULT STDMETHODCALLTYPE GetItemObject(UINT flags,REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;if(!OnThread())return RPC_E_WRONG_THREAD;Reference<View> invocation(this);
        const auto type=flags&SVGIO_TYPE_MASK;
        if(type==SVGIO_BACKGROUND){if(iid==IID_IContextMenu)return Menu(0,nullptr,iid,result);return folder_->QueryInterface(iid,result);}
        if(type!=SVGIO_SELECTION&&type!=SVGIO_ALLVIEW)return E_NOTIMPL;
        try{
            std::vector<Pidl> ids;std::vector<PCUITEMID_CHILD> raw;
            const auto selected=surface_?surface_->SelectedItems():Selection{};
            if(type==SVGIO_SELECTION){for(UINT i=0;i<selected.count;++i){const auto index=selected.indices[i];if(index<page_.entries.size())ids.emplace_back(snapshot::MakePidl(page_.entries[index]));}}
            else for(const auto& entry:page_.entries)ids.emplace_back(snapshot::MakePidl(entry));
            for(const auto& id:ids){if(!id.value)return E_OUTOFMEMORY;raw.push_back(id.value);}
            if(raw.empty())return E_NOINTERFACE;
            return Menu(static_cast<UINT>(raw.size()),raw.data(),iid,result);
        }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
    }
    HRESULT STDMETHODCALLTYPE GetCurrentViewMode(UINT* mode) override {if(!mode)return E_POINTER;if(!OnThread())return RPC_E_WRONG_THREAD;*mode=surface_&&surface_->CurrentMode()==Mode::List?FVM_DETAILS:FVM_ICON;return S_OK;}
    HRESULT STDMETHODCALLTYPE SetCurrentViewMode(UINT mode) override {
        if(!OnThread())return RPC_E_WRONG_THREAD;if(!surface_)return E_UNEXPECTED;Reference<View> invocation(this);
        if(mode==FVM_DETAILS||mode==FVM_LIST)surface_->SetMode(Mode::List);
        else if(mode==FVM_ICON||mode==FVM_THUMBNAIL)surface_->SetMode(Mode::Gallery);else return E_NOTIMPL;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetFolder(REFIID iid,void** result) override {if(!OnThread())return RPC_E_WRONG_THREAD;return folder_->QueryInterface(iid,result);}
    HRESULT STDMETHODCALLTYPE Item(int index,PITEMID_CHILD* result) override {
        if(!result)return E_POINTER;*result=nullptr;if(!OnThread())return RPC_E_WRONG_THREAD;
        if(index<0||static_cast<size_t>(index)>=page_.entries.size())return E_INVALIDARG;
        *result=snapshot::MakePidl(page_.entries[static_cast<size_t>(index)]);return *result?S_OK:E_OUTOFMEMORY;
    }
    HRESULT STDMETHODCALLTYPE ItemCount(UINT flags,int* result) override {
        if(!result)return E_POINTER;*result=0;if(!OnThread())return RPC_E_WRONG_THREAD;
        if((flags&SVGIO_TYPE_MASK)==SVGIO_ALLVIEW)*result=static_cast<int>(page_.entries.size());
        else if((flags&SVGIO_TYPE_MASK)==SVGIO_SELECTION)*result=surface_?static_cast<int>(surface_->SelectedItems().count):0;else return E_NOTIMPL;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Items(UINT flags,REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;if(!OnThread())return RPC_E_WRONG_THREAD;if(iid!=IID_IEnumIDList)return E_NOINTERFACE;
        try{
            std::vector<snapshot::Entry> entries;
            if((flags&SVGIO_TYPE_MASK)==SVGIO_ALLVIEW)entries=page_.entries;
            else if((flags&SVGIO_TYPE_MASK)==SVGIO_SELECTION){const auto selected=surface_?surface_->SelectedItems():Selection{};for(UINT i=0;i<selected.count;++i)if(selected.indices[i]<page_.entries.size())entries.push_back(page_.entries[selected.indices[i]]);}
            else return E_NOTIMPL;
            auto items=new(std::nothrow) gallery::Items(folder_,std::move(entries));if(!items)return E_OUTOFMEMORY;*result=static_cast<IEnumIDList*>(items);return S_OK;
        }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
    }
    HRESULT STDMETHODCALLTYPE GetSelectionMarkedItem(int* index) override {if(!index)return E_POINTER;if(!OnThread())return RPC_E_WRONG_THREAD;const auto selected=surface_?surface_->SelectedItems():Selection{};*index=selected.count?static_cast<int>(selected.indices[0]):-1;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetFocusedItem(int* index) override {if(!index)return E_POINTER;if(!OnThread())return RPC_E_WRONG_THREAD;*index=surface_?surface_->FocusedItem():-1;return S_OK;}
    HRESULT STDMETHODCALLTYPE GetItemPosition(PCUITEMID_CHILD item,POINT* position) override {
        if(!position)return E_POINTER;*position={};if(!OnThread())return RPC_E_WRONG_THREAD;if(!surface_)return E_UNEXPECTED;
        try{const auto index=Index(item);if(index<0)return E_INVALIDARG;RECT bounds{};const auto hr=surface_->ItemRect(static_cast<UINT>(index),&bounds);if(SUCCEEDED(hr))*position={bounds.left,bounds.top};return hr;}catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
    }
    HRESULT STDMETHODCALLTYPE GetSpacing(POINT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetDefaultSpacing(POINT*) override {return E_NOTIMPL;}
    HRESULT STDMETHODCALLTYPE GetAutoArrange() override {return S_OK;}
    HRESULT STDMETHODCALLTYPE SelectItem(int index,DWORD flags) override {if(index<0)return E_INVALIDARG;if(!OnThread())return RPC_E_WRONG_THREAD;if((flags&SVSI_EDIT)==SVSI_EDIT)return E_ACCESSDENIED;Reference<View> invocation(this);return surface_?surface_->SelectItem(static_cast<UINT>(index),flags):E_UNEXPECTED;}
    HRESULT STDMETHODCALLTYPE SelectAndPositionItems(UINT,PCUITEMID_CHILD_ARRAY,POINT*,DWORD) override {return E_NOTIMPL;}
};
}
HRESULT CreateView(IShellFolder2* folder,PCIDLIST_ABSOLUTE absolute,snapshot::Location location,IShellView** result,Sources sources,PCIDLIST_ABSOLUTE notificationRoot,PreferenceStore preferences) noexcept {
    if(!result)return E_POINTER;*result=nullptr;UINT bytes=0,count=0;
    if(!folder||snapshot::Zero(location.epoch)||snapshot::Zero(location.node)||!snapshot::BoundedList(absolute,bytes,count)||!count)return E_INVALIDARG;
    if(notificationRoot&&(!snapshot::BoundedList(notificationRoot,bytes,count)||!count))return E_INVALIDARG;
    try{auto view=new(std::nothrow) View(folder,absolute,location,sources,notificationRoot,preferences);if(!view)return E_OUTOFMEMORY;*result=view;return S_OK;}catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
}
