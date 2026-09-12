#include "ViewRequests.h"
#include "../LocalPipePeer.h"
#include <algorithm>
#include <atomic>
#include <mutex>
#include <new>

namespace gallery {
namespace {
constexpr unsigned MaxPageWork=4,MaxImageWork=64;
std::atomic_ulong pageWorkCount{0},imageWorkCount{0};
std::atomic<std::uint64_t> nextTicket{1};
bool Reserve(std::atomic_ulong& count,ULONG maximum) noexcept {
    auto current=count.load();do{if(current>=maximum)return false;}while(!count.compare_exchange_weak(current,current+1));return true;
}
struct Cancellation {
    local_pipe::Handle event{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
    Cancellation(){if(!event.value)throw std::bad_alloc();}
    void Stop()const noexcept {SetEvent(event.value);}
    bool Stopped()const noexcept {return WaitForSingleObject(event.value,0)==WAIT_OBJECT_0;}
};
struct Ticket {
    std::shared_ptr<Cancellation> cancel=std::make_shared<Cancellation>();
    std::uint64_t generation=0;
    const std::uint64_t serial=nextTicket.fetch_add(1);
    UINT index=0;
    snapshot::Location location;
    std::atomic_bool finished{false};
    bool resolved=false; // UI-owned terminal state until this item leaves the viewport.
};
struct PixelBudget {std::atomic_size_t bytes{0};};
struct PixelLease {
    std::shared_ptr<PixelBudget> budget;
    std::shared_ptr<const Pbgra> image;
    size_t bytes;
    PixelLease(std::shared_ptr<PixelBudget> value,std::shared_ptr<const Pbgra> pixels,size_t size)
        :budget(std::move(value)),image(std::move(pixels)),bytes(size){}
    ~PixelLease(){image.reset();budget->bytes.fetch_sub(bytes);}
};
std::shared_ptr<const Pbgra> AdmitImage(const std::shared_ptr<PixelBudget>& budget,std::shared_ptr<const Pbgra> image){
    if(!image)return {};
    const auto bytes=image->pixels.capacity();auto current=budget->bytes.load();
    do{if(bytes>MaxImageBytes-current)return {};}while(!budget->bytes.compare_exchange_weak(current,current+bytes));
    try{
        auto lease=std::make_shared<PixelLease>(budget,image,bytes);
        return std::shared_ptr<const Pbgra>(std::move(lease),image.get());
    }catch(const std::bad_alloc&){budget->bytes.fetch_sub(bytes);throw;}
}
struct Record {Completion value;std::shared_ptr<Ticket> ticket;};
struct ImagePool {
    PTP_POOL pool=CreateThreadpool(nullptr);
    TP_CALLBACK_ENVIRON environment{};
    ImagePool(){InitializeThreadpoolEnvironment(&environment);if(pool){SetThreadpoolThreadMaximum(pool,2);SetThreadpoolCallbackPool(&environment,pool);}}
    ~ImagePool(){DestroyThreadpoolEnvironment(&environment);if(pool)CloseThreadpool(pool);}
};
ImagePool& Images(){static ImagePool pool;return pool;}
bool Pin(LPCWSTR address,HMODULE& module) noexcept {
    HMODULE owner=nullptr;
    if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,address,&owner))return false;
    return owner==GetModuleHandleW(nullptr)||GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,address,&module)!=FALSE;
}
}
struct RequestState {
    const DWORD thread=GetCurrentThreadId();
    Sources sources;
    std::atomic<HWND> target{nullptr};
    UINT message=0;
    std::atomic<std::uint64_t> generation{0};
    std::atomic_bool deliveryFailed{false};
    snapshot::Location location;
    std::shared_ptr<Cancellation> page;
    std::array<std::shared_ptr<Ticket>,snapshot::MaxItems> images{};
    // Cancel retires viewport identity, but cannot release a still-running slot.
    std::array<std::shared_ptr<Ticket>,MaxImageRequestsPerView> inFlight{};
    const std::shared_ptr<PixelBudget> pixels=std::make_shared<PixelBudget>();
    std::mutex mutex;
    std::vector<Record> completed;
    explicit RequestState(Sources value):sources(value){completed.reserve(17);}
    void Publish(Completion value,std::shared_ptr<Ticket> ticket={}) noexcept {
        const auto current=value.generation;
        try{
            if(current!=generation.load()||!target.load()||(ticket&&ticket->cancel->Stopped()))return;
            {
                std::lock_guard<std::mutex> lock(mutex);
                if(current!=generation.load()||!target.load())return;
                completed.erase(std::remove_if(completed.begin(),completed.end(),[](const Record& record){return record.ticket&&record.ticket->cancel->Stopped();}),completed.end());
                if(completed.size()>=17){deliveryFailed=true;const auto window=target.load();if(window)PostMessageW(window,message,static_cast<WPARAM>(current),0);return;}
                completed.push_back({std::move(value),std::move(ticket)});
            }
            const auto window=target.load();if(window)PostMessageW(window,message,static_cast<WPARAM>(current),0);
        }catch(const std::bad_alloc&){if(current==generation.load()){deliveryFailed=true;const auto window=target.load();if(window)PostMessageW(window,message,static_cast<WPARAM>(current),0);}}
    }
};
namespace {
struct PageWork {
    std::shared_ptr<RequestState> state;
    std::shared_ptr<Cancellation> cancel;
    snapshot::Location location;
    std::uint64_t generation;
    HMODULE module=nullptr;
    ~PageWork(){--pageWorkCount;}
};
void CALLBACK ReadPage(PTP_CALLBACK_INSTANCE instance,void* context){
    std::unique_ptr<PageWork> work(static_cast<PageWork*>(context));const auto module=work->module;
    {
        const auto until=GetTickCount64()+10000;Completion completed;completed.page=true;completed.generation=work->generation;
        for(unsigned attempt=0;attempt<20&&!work->cancel->Stopped()&&GetTickCount64()<until;++attempt){
            completed.snapshot=work->state->sources.page(work->location,work->cancel->event.value);
            if(completed.snapshot.status!=snapshot::Status::Loading)break;
            const auto now=GetTickCount64();if(now>=until)break;
            if(WaitForSingleObject(work->cancel->event.value,static_cast<DWORD>(std::min<ULONGLONG>(500,until-now)))==WAIT_OBJECT_0)break;
        }
        if(completed.snapshot.status==snapshot::Status::Loading||GetTickCount64()>=until){completed.snapshot={};completed.snapshot.status=snapshot::Status::Unavailable;}
        if(!work->cancel->Stopped())work->state->Publish(std::move(completed));
    }
    work.reset();if(module)FreeLibraryWhenCallbackReturns(instance,module);
}
struct ImageWork {
    std::shared_ptr<RequestState> state;
    std::shared_ptr<Ticket> ticket;
    PTP_WORK work=nullptr;
    HMODULE module=nullptr;
    ~ImageWork(){--imageWorkCount;}
};
void CALLBACK ReadImage(PTP_CALLBACK_INSTANCE instance,void* context,PTP_WORK){
    std::unique_ptr<ImageWork> work(static_cast<ImageWork*>(context));const auto module=work->module;
    {
        const auto ticket=work->ticket;const auto state=work->state;
        if(!ticket->cancel->Stopped()){
            Completion completed;completed.generation=ticket->generation;completed.ticket=ticket->serial;completed.index=ticket->index;
            completed.thumbnail=state->sources.image(ticket->location,ticket->cancel->event.value);
            if(!ticket->cancel->Stopped()){
                try{
                    if(completed.thumbnail.image){
                        completed.thumbnail.image=AdmitImage(state->pixels,std::move(completed.thumbnail.image));
                        if(!completed.thumbnail.image)completed.thumbnail.status=thumbnail::Status::Busy;
                    }
                }catch(const std::bad_alloc&){completed.thumbnail.image.reset();completed.thumbnail.status=thumbnail::Status::Unavailable;}
                state->Publish(std::move(completed),ticket);
            }
        }
        CloseThreadpoolWork(work->work);work.reset();ticket->finished=true;
        // Even an obsolete task releases admission capacity. Wake the current UI
        // generation without publishing any old response or borrowing UI state.
        const auto generation=state->generation.load();const auto window=state->target.load();
        if(window)PostMessageW(window,state->message,static_cast<WPARAM>(generation),0);
    }
    if(module)FreeLibraryWhenCallbackReturns(instance,module);
}
HRESULT QueueImage(const std::shared_ptr<RequestState>& state,const std::shared_ptr<Ticket>& ticket) noexcept {
    if(!Reserve(imageWorkCount,MaxImageWork))return HRESULT_FROM_WIN32(ERROR_BUSY);
    ImageWork* work=nullptr;
    try{work=new ImageWork{state,ticket};}catch(const std::bad_alloc&){--imageWorkCount;return E_OUTOFMEMORY;}
    auto& pool=Images();
    if(pool.pool&&Pin(reinterpret_cast<LPCWSTR>(&ReadImage),work->module))work->work=CreateThreadpoolWork(ReadImage,work,&pool.environment);
    if(!work->work){const auto module=work->module;delete work;if(module)FreeLibrary(module);return E_OUTOFMEMORY;}
    SubmitThreadpoolWork(work->work);return S_OK;
}
}
Requests::Requests(Sources sources):state_(std::make_shared<RequestState>(sources)){}
Requests::~Requests(){Close();}
void Requests::Attach(HWND target,UINT message) noexcept {state_->message=message;state_->target=target;}
void Requests::Cancel(std::uint64_t generation) noexcept {
    state_->generation=generation;
    state_->deliveryFailed=false;
    if(state_->page){state_->page->Stop();state_->page.reset();}
    for(auto& image:state_->images){if(image)image->cancel->Stop();image.reset();}
    std::lock_guard<std::mutex> lock(state_->mutex);state_->completed.clear();
}
void Requests::Close() noexcept {state_->target=nullptr;Cancel(0);}
HRESULT Requests::Begin(snapshot::Location location,std::uint64_t generation) noexcept {
    if(GetCurrentThreadId()!=state_->thread)return RPC_E_WRONG_THREAD;
    if(!generation||!state_->target||!state_->sources.page||!state_->sources.image)return E_INVALIDARG;
    Cancel(generation);
    state_->location=location;
    if(!Reserve(pageWorkCount,MaxPageWork))return HRESULT_FROM_WIN32(ERROR_BUSY);
    PageWork* work=nullptr;
    try{
        state_->page=std::make_shared<Cancellation>();work=new PageWork{state_,state_->page,location,generation};
    }catch(const std::bad_alloc&){--pageWorkCount;return E_OUTOFMEMORY;}
    if(!Pin(reinterpret_cast<LPCWSTR>(&ReadPage),work->module)||!TrySubmitThreadpoolCallback(ReadPage,work,nullptr)){
        const auto module=work->module;delete work;if(module)FreeLibrary(module);return E_OUTOFMEMORY;
    }
    return S_OK;
}
HRESULT Requests::Visible(const snapshot::Page& page,const VisibleFiles& visible,std::uint64_t generation) noexcept {
    if(GetCurrentThreadId()!=state_->thread)return RPC_E_WRONG_THREAD;
    if(generation!=state_->generation||page.status!=snapshot::Status::Ready||page.epoch!=state_->location.epoch)return S_FALSE;
    if(visible.count>MaxVisibleFiles||page.entries.size()>snapshot::MaxItems)return E_INVALIDARG;
    std::array<bool,snapshot::MaxItems> keep{};
    for(UINT at=0;at<visible.count;++at){const auto index=visible.indices[at];
        if(index>=page.entries.size()||page.entries[index].kind!=snapshot::Kind::File||page.entries[index].epoch!=page.epoch||snapshot::Zero(page.entries[index].node)||keep[index])return E_INVALIDARG;keep[index]=true;}
    for(UINT index=0;index<snapshot::MaxItems;++index)if(!keep[index]&&state_->images[index]){state_->images[index]->cancel->Stop();state_->images[index].reset();}
    for(auto& ticket:state_->inFlight)if(ticket&&ticket->finished&&(ticket->resolved||ticket->cancel->Stopped()))ticket.reset();
    try{
        for(UINT at=0;at<visible.count;++at){const auto index=visible.indices[at];if(state_->images[index])continue;
            const auto slot=std::find(state_->inFlight.begin(),state_->inFlight.end(),nullptr);
            if(slot==state_->inFlight.end())break;
            auto ticket=std::make_shared<Ticket>();ticket->generation=generation;ticket->index=index;ticket->location={page.entries[index].epoch,page.entries[index].node};
            state_->images[index]=ticket;*slot=ticket;
            const auto hr=QueueImage(state_,ticket);
            if(FAILED(hr)){ticket->finished=true;Completion completed;completed.generation=generation;completed.ticket=ticket->serial;completed.index=index;completed.thumbnail.status=thumbnail::Status::Busy;state_->Publish(std::move(completed),ticket);}
        }
        return S_OK;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
std::vector<Completion> Requests::Take(){
    std::vector<Record> records;
    {std::lock_guard<std::mutex> lock(state_->mutex);records.swap(state_->completed);state_->completed.reserve(17);}
    std::vector<Completion> result;result.reserve(records.size());
    if(state_->deliveryFailed.exchange(false)){Completion failed;failed.generation=state_->generation;failed.page=true;result.push_back(std::move(failed));return result;}
    for(auto& record:records){
        if(record.value.generation!=state_->generation)continue;
        if(record.ticket&&(record.ticket->cancel->Stopped()||record.value.index>=snapshot::MaxItems||state_->images[record.value.index]!=record.ticket))continue;
        if(record.ticket)record.ticket->resolved=true;
        result.push_back(std::move(record.value));
    }
    return result;
}
ULONG PendingWork() noexcept {return pageWorkCount.load()+imageWorkCount.load();}
bool Requests::Current(const Completion& completion)const noexcept {
    if(GetCurrentThreadId()!=state_->thread||completion.generation!=state_->generation)return false;
    if(completion.page)return true;
    if(completion.index>=snapshot::MaxItems)return false;
    const auto& ticket=state_->images[completion.index];
    return ticket&&ticket->serial==completion.ticket&&!ticket->cancel->Stopped();
}
}
