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
        timer_=false;
    }
    void Arm() noexcept {
        if(!timer_&&!refreshing_&&window_&&site_&&budget_.active){
            timer_=SetTimer(window_,signal_->cookie,static_cast<UINT>(IntervalMs),nullptr)!=0;
            if(!timer_)budget_.Stop();
        }
    }
    void StatusChanged() noexcept {
        signal_->queued=false;
        snapshot::Status status;if(!signal_->Read(status))return;
        if(!budget_.Observe(status,GetTickCount64()))StopTimer();else Arm();
    }
    void Detach() noexcept {
        StopTimer();budget_.Stop();signal_->window=nullptr;signal_->queued=false;
        if(window_){
            DWORD_PTR attached=0;
            if(RemoveWindowSubclass(window_,WindowProc,signal_->cookie)||!GetWindowSubclass(window_,WindowProc,signal_->cookie,&attached)){
                window_=nullptr;ReleaseSlot();Release();
            } // If unhooking fails, retain the callback/DLL until a later teardown.
        }else ReleaseSlot();
    }
    void ClearSite() noexcept {auto previous=site_;site_=nullptr;++siteRevision_;if(previous)previous->Release();}
    void Tick() noexcept {
        if(!timer_||refreshing_)return;
        StopTimer();
        const auto now=GetTickCount64();
        snapshot::Status status;ULONGLONG generation=0;
        if(!signal_->Read(status,&generation)){if(now>=budget_.deadline)budget_.Stop();Arm();return;}
        if(!budget_.Observe(status,now))return;
        if(!budget_.Take(now)){Arm();return;}
        refreshing_=true;
        const HWND expected=window_;
        const auto episode=budget_.episode,siteRevision=siteRevision_;
        Reference<IUnknown> site(site_);IShellBrowser* browser=nullptr;IShellView* view=nullptr;
        auto hr=site.value?IUnknown_QueryService(site.value,SID_STopLevelBrowser,IID_PPV_ARGS(&browser)):E_NOINTERFACE;
        if(SUCCEEDED(hr)&&browser)hr=browser->QueryActiveShellView(&view);
        else if(SUCCEEDED(hr))hr=E_NOINTERFACE;
        HWND actual=nullptr;
        if(SUCCEEDED(hr)&&view)hr=view->GetWindow(&actual);else if(SUCCEEDED(hr))hr=E_NOINTERFACE;
        if(SUCCEEDED(hr)){
            snapshot::Status current;ULONGLONG currentGeneration=0;
            const bool valid=expected&&actual==expected&&window_==expected&&site_==site.value
                &&siteRevision_==siteRevision&&budget_.active&&budget_.episode==episode
                &&GetTickCount64()<budget_.deadline&&signal_->Read(current,&currentGeneration)
                &&current==snapshot::Status::Loading&&currentGeneration==generation;
            hr=valid?view->Refresh():S_FALSE;
        }
        if(view)view->Release();if(browser)browser->Release();
        refreshing_=false;
        if(FAILED(hr)){budget_.Stop();StopTimer();return;}
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
    Callback(IUnknown* owner,const std::shared_ptr<Signal>& signal):owner_(owner),signal_(signal){owner_->AddRef();liveCallbacks.fetch_add(1);}
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
        if(GetCurrentThreadId()!=thread_)return RPC_E_WRONG_THREAD;
        Reference<Callback> invocation(this);if(site)site->AddRef();auto previous=site_;site_=site;++siteRevision_;
        if(previous)previous->Release();
        if(!site_){Detach();return S_OK;}
        if(window_)StatusChanged();return S_OK;
    }
    HRESULT STDMETHODCALLTYPE GetSite(REFIID iid,void** result) override {
        if(!result)return E_POINTER;*result=nullptr;
        if(GetCurrentThreadId()!=thread_)return RPC_E_WRONG_THREAD;
        Reference<Callback> invocation(this);Reference<IUnknown> site(site_);
        return site.value?site.value->QueryInterface(iid,result):E_FAIL;
    }
    HRESULT STDMETHODCALLTYPE MessageSFVCB(UINT message,WPARAM wParam,LPARAM) override {
        if(message!=SFVM_WINDOWCREATED)return E_NOTIMPL;
        if(GetCurrentThreadId()!=thread_)return RPC_E_WRONG_THREAD;
        Reference<Callback> invocation(this);const auto window=reinterpret_cast<HWND>(wParam);
        if(!window||window_||GetWindowThreadProcessId(window,nullptr)!=thread_)return E_INVALIDARG;
        if(!ReserveSlot())return HRESULT_FROM_WIN32(ERROR_BUSY);
        if(!SetWindowSubclass(window,WindowProc,signal_->cookie,reinterpret_cast<DWORD_PTR>(this))){ReleaseSlot();return E_FAIL;}
        budget_=Budget{};
        AddRef();window_=window;signal_->window=window;StatusChanged();return S_OK;
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
    if(target&&notification&&!queued.exchange(true))if(!PostMessageW(target,notification,cookie,0))queued=false;
}
bool Signal::Read(snapshot::Status& value,ULONGLONG* resultGeneration) const noexcept {
    const auto generation=started.load(),current=published.load();
    if((current>>3)!=generation||started.load()!=generation)return false;
    value=static_cast<snapshot::Status>(current&7);if(resultGeneration)*resultGeneration=generation;return true;
}
ULONG ActiveViews() noexcept {return activeViews.load();}
ULONG LiveCallbacks() noexcept {return liveCallbacks.load();}
HRESULT CreateCallback(IUnknown* owner,const std::shared_ptr<Signal>& signal,IShellFolderViewCB** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;if(!owner||!signal||!signal->notification)return E_INVALIDARG;
    ULONG count=activeViews.load();do{if(count>=MaxViews)return HRESULT_FROM_WIN32(ERROR_BUSY);}while(!activeViews.compare_exchange_weak(count,count+1));
    auto callback=new(std::nothrow) Callback(owner,signal);
    if(!callback){activeViews.fetch_sub(1);return E_OUTOFMEMORY;}*result=callback;return S_OK;
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
