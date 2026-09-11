#include "LoadingRefresh.h"
#include <commctrl.h>
#include <shlwapi.h>
#include <shlguid.h>
#include <new>

namespace loading {
namespace {
std::atomic_ulong activeViews{0};
std::atomic_ulong liveCallbacks{0};
// Distinct timer/message generations also reject a queued message for a reused HWND.
std::atomic<UINT_PTR> nextCookie{0xA551000000000001ull};
template<class T> struct Reference {
    T* value;
    explicit Reference(T* object):value(object){if(value)value->AddRef();}
    ~Reference(){if(value)value->Release();}
    Reference(const Reference&)=delete;Reference& operator=(const Reference&)=delete;
};
class Callback final : public IShellFolderViewCB, public IObjectWithSite {
    std::atomic_ulong refs_{1};
    const DWORD thread_=GetCurrentThreadId();
    IUnknown* owner_;
    IUnknown* site_=nullptr;
    ULONGLONG siteRevision_=0;
    std::shared_ptr<Signal> signal_;
    HWND window_=nullptr;
    Budget budget_;
    bool timer_=false,refreshing_=false;
    std::atomic_bool slotHeld_{true};
    void ReleaseSlot() noexcept {if(slotHeld_.exchange(false))activeViews.fetch_sub(1);}
    bool ReserveSlot() noexcept {
        if(slotHeld_)return true;
        ULONG count=activeViews.load();do{if(count>=MaxViews)return false;}while(!activeViews.compare_exchange_weak(count,count+1));
        slotHeld_=true;return true;
    }
    void StopTimer() noexcept {
        if(timer_&&window_)KillTimer(window_,signal_->cookie);
        timer_=false;signal_->diagnostic.timerActive=0;
    }
    void Arm() noexcept {
        if(!timer_&&!refreshing_&&window_&&site_&&budget_.active){
            ++signal_->diagnostic.arms;signal_->diagnostic.armThread=GetCurrentThreadId();
            timer_=SetTimer(window_,signal_->cookie,static_cast<UINT>(IntervalMs),nullptr)!=0;
            signal_->diagnostic.armError=timer_?0:GetLastError();signal_->diagnostic.timerActive=timer_?1:0;
            if(!timer_)budget_.Stop();
        }
    }
    void StatusChanged() noexcept {
        signal_->queued=false;
        snapshot::Status status;if(!signal_->Read(status))return;
        if(!budget_.Observe(status,GetTickCount64()))StopTimer();else Arm();
    }
    void Detach() noexcept {
        ++signal_->diagnostic.detaches;
        StopTimer();budget_.Stop();signal_->window=nullptr;signal_->queued=false;
        if(window_){
            DWORD_PTR attached=0;
            if(RemoveWindowSubclass(window_,WindowProc,signal_->cookie)||!GetWindowSubclass(window_,WindowProc,signal_->cookie,&attached)){
                window_=nullptr;ReleaseSlot();Release();
            } // If unhooking fails, retain the callback/DLL until a later teardown.
        }else ReleaseSlot();
    }
    void ClearSite() noexcept {auto previous=site_;site_=nullptr;signal_->diagnostic.sitePresent=0;++siteRevision_;if(previous)previous->Release();}
    void Tick() noexcept {
        auto& diagnostic=signal_->diagnostic;++diagnostic.ticks;
        if(!timer_){diagnostic.skip=1;return;}if(refreshing_){diagnostic.skip=2;return;}
        StopTimer();
        const auto now=GetTickCount64();
        snapshot::Status status;ULONGLONG generation=0;
        if(!signal_->Read(status,&generation)){diagnostic.skip=3;if(now>=budget_.deadline)budget_.Stop();Arm();return;}
        if(!budget_.Observe(status,now)){diagnostic.skip=4;return;}
        if(!budget_.Take(now)){diagnostic.skip=5;Arm();return;}
        ++diagnostic.attempts;diagnostic.skip=0;diagnostic.activeWindow=0;diagnostic.windowMatch=0;
        diagnostic.serviceResult=E_PENDING;diagnostic.activeResult=E_PENDING;diagnostic.getWindowResult=E_PENDING;diagnostic.refreshResult=E_PENDING;
        refreshing_=true;
        const HWND expected=window_;
        const auto episode=budget_.episode,siteRevision=siteRevision_;
        Reference<IUnknown> site(site_);IShellBrowser* browser=nullptr;IShellView* view=nullptr;
        auto hr=site.value?IUnknown_QueryService(site.value,SID_STopLevelBrowser,IID_PPV_ARGS(&browser)):E_NOINTERFACE;
        diagnostic.serviceResult=hr;
        if(SUCCEEDED(hr)&&browser){hr=browser->QueryActiveShellView(&view);diagnostic.activeResult=hr;}
        else if(SUCCEEDED(hr))hr=E_NOINTERFACE;
        HWND actual=nullptr;
        if(SUCCEEDED(hr)&&view){hr=view->GetWindow(&actual);diagnostic.getWindowResult=hr;}else if(SUCCEEDED(hr))hr=E_NOINTERFACE;
        diagnostic.activeWindow=reinterpret_cast<UINT_PTR>(actual);diagnostic.windowMatch=expected&&actual==expected?1:0;
        if(SUCCEEDED(hr)){
            snapshot::Status current;ULONGLONG currentGeneration=0;
            const bool valid=expected&&actual==expected&&window_==expected&&site_==site.value
                &&siteRevision_==siteRevision&&budget_.active&&budget_.episode==episode
                &&GetTickCount64()<budget_.deadline&&signal_->Read(current,&currentGeneration)
                &&current==snapshot::Status::Loading&&currentGeneration==generation;
            if(valid){++diagnostic.refreshes;hr=view->Refresh();diagnostic.refreshResult=hr;}
            else{diagnostic.skip=6;hr=S_FALSE;}
        }
        if(view)view->Release();if(browser)browser->Release();
        refreshing_=false;
        if(FAILED(hr)){diagnostic.skip=7;budget_.Stop();StopTimer();return;}
        StatusChanged();
    }
    static LRESULT CALLBACK WindowProc(HWND window,UINT message,WPARAM wParam,LPARAM lParam,UINT_PTR,DWORD_PTR data) noexcept {
        auto self=reinterpret_cast<Callback*>(data);Reference<Callback> invocation(self);
        if(message==WM_NCDESTROY){
            self->Detach();const auto result=DefSubclassProc(window,message,wParam,lParam);self->ClearSite();return result;
        }
        if(message==self->signal_->notification&&wParam==self->signal_->cookie){self->StatusChanged();return 0;}
        if(message==WM_TIMER&&wParam==self->signal_->cookie){self->Tick();return 0;}
        return DefSubclassProc(window,message,wParam,lParam);
    }
public:
    Callback(IUnknown* owner,const std::shared_ptr<Signal>& signal):owner_(owner),signal_(signal){signal_->diagnostic.constructorThread=thread_;owner_->AddRef();liveCallbacks.fetch_add(1);}
    ~Callback(){ClearSite();ReleaseSlot();liveCallbacks.fetch_sub(1);owner_->Release();}
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(iid==IID_IUnknown||iid==IID_IShellFolderViewCB)*result=static_cast<IShellFolderViewCB*>(this);
        else if(iid==IID_IObjectWithSite)*result=static_cast<IObjectWithSite*>(this);else return E_NOINTERFACE;
        AddRef();return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override {return ++refs_;}
    ULONG STDMETHODCALLTYPE Release() override {auto refs=--refs_;if(!refs)delete this;return refs;}
    HRESULT STDMETHODCALLTYPE SetSite(IUnknown* site) override {
        auto& diagnostic=signal_->diagnostic;++diagnostic.siteCalls;diagnostic.siteThread=GetCurrentThreadId();
        if(GetCurrentThreadId()!=thread_){diagnostic.siteResult=RPC_E_WRONG_THREAD;return RPC_E_WRONG_THREAD;}
        Reference<Callback> invocation(this);if(site)site->AddRef();auto previous=site_;site_=site;++siteRevision_;
        diagnostic.sitePresent=site_?1:0;
        if(previous)previous->Release();
        if(!site_){Detach();diagnostic.siteResult=S_OK;return S_OK;}
        if(window_)StatusChanged();diagnostic.siteResult=S_OK;return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(GetCurrentThreadId()!=thread_)return RPC_E_WRONG_THREAD;
        Reference<Callback> invocation(this);Reference<IUnknown> site(site_);
        return site.value?site.value->QueryInterface(iid,result):E_FAIL;
    }
    HRESULT STDMETHODCALLTYPE MessageSFVCB(UINT message,WPARAM wParam,LPARAM) override {
        if(message!=SFVM_WINDOWCREATED)return E_NOTIMPL;
        auto& diagnostic=signal_->diagnostic;++diagnostic.windowCalls;diagnostic.windowThread=GetCurrentThreadId();
        diagnostic.reportedWindow=static_cast<UINT_PTR>(wParam);const DWORD windowThread=GetWindowThreadProcessId(reinterpret_cast<HWND>(wParam),nullptr);diagnostic.windowOwnerThread=windowThread;
        const auto done=[&](HRESULT result){diagnostic.windowResult=result;return result;};
        if(GetCurrentThreadId()!=thread_)return done(RPC_E_WRONG_THREAD);
        Reference<Callback> invocation(this);const auto window=reinterpret_cast<HWND>(wParam);
        if(!window||window_||windowThread!=thread_)return done(E_INVALIDARG);
        if(!ReserveSlot())return done(HRESULT_FROM_WIN32(ERROR_BUSY));
        if(!SetWindowSubclass(window,WindowProc,signal_->cookie,reinterpret_cast<DWORD_PTR>(this))){ReleaseSlot();return done(E_FAIL);}
        budget_=Budget{};
        AddRef();window_=window;signal_->window=window;StatusChanged();return done(S_OK);
    }
};
}
bool Budget::Observe(snapshot::Status status,ULONGLONG now) noexcept {
    if(status!=snapshot::Status::Loading){previous=status;active=false;return false;}
    if(previous!=status){deadline=now+DurationMs;next=now+IntervalMs;attempts=0;++episode;active=true;}
    previous=status;if(now>=deadline||attempts>=MaxAttempts)active=false;return active;
}
bool Budget::Take(ULONGLONG now) noexcept {
    if(!active||now>=deadline||attempts>=MaxAttempts){active=false;return false;}
    if(now<next)return false;++attempts;next=now+IntervalMs;return true;
}
Signal::Signal():notification(RegisterWindowMessageW(L"AssetLibrary.ExplorerProof.LoadingState.v1")),cookie(nextCookie.fetch_add(1)){}
void Signal::Publish(snapshot::Status value) noexcept {
    Publish(Begin(),value);
}
void Signal::Publish(ULONGLONG generation,snapshot::Status value) noexcept {
    if(generation!=started.load()||generation>(MAXULONGLONG>>3))return;
    auto previous=published.load();
    do{if((previous>>3)>generation)return;}while(!published.compare_exchange_weak(previous,(generation<<3)|static_cast<ULONGLONG>(value)));
    if(generation!=started.load())return;
    const HWND target=window.load();
    if(target&&notification&&!queued.exchange(true)){
        ++diagnostic.posts;const bool posted=PostMessageW(target,notification,cookie,0)!=FALSE;
        diagnostic.postError=posted?0:GetLastError();if(!posted)queued=false;
    }
}
bool Signal::Read(snapshot::Status& value,ULONGLONG* resultGeneration) const noexcept {
    const auto generation=started.load(),current=published.load();
    if((current>>3)!=generation||started.load()!=generation)return false;
    value=static_cast<snapshot::Status>(current&7);if(resultGeneration)*resultGeneration=generation;return true;
}
ULONG ActiveViews() noexcept {return activeViews.load();}
ULONG LiveCallbacks() noexcept {return liveCallbacks.load();}
HRESULT CreateCallback(IUnknown* owner,const std::shared_ptr<Signal>& signal,IShellFolderViewCB** result) noexcept {
    const auto done=[&](HRESULT hr){if(signal)signal->diagnostic.createResult=hr;return hr;};
    if(signal)signal->diagnostic.createSlots=activeViews.load();
    if(!result)return done(E_POINTER);*result=nullptr;if(!owner||!signal||!signal->notification)return done(E_INVALIDARG);
    ULONG count=activeViews.load();do{if(count>=MaxViews){signal->diagnostic.createSlots=count;return done(HRESULT_FROM_WIN32(ERROR_BUSY));}}while(!activeViews.compare_exchange_weak(count,count+1));
    signal->diagnostic.createSlots=count;
    auto callback=new(std::nothrow) Callback(owner,signal);
    if(!callback){activeViews.fetch_sub(1);return done(E_OUTOFMEMORY);}*result=callback;return done(S_OK);
}
HRESULT CreateView(IShellFolder* folder,const std::shared_ptr<Signal>& signal,IShellView** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;if(!folder)return E_INVALIDARG;
    IShellFolderViewCB* callback=nullptr;
    // Auto-refresh is a bounded enhancement; capacity failure preserves manual F5.
    CreateCallback(folder,signal,&callback);
    SFV_CREATE create{sizeof(create),folder,nullptr,callback};auto hr=SHCreateShellFolderView(&create,result);
    if(callback)callback->Release();return hr;
}
}
