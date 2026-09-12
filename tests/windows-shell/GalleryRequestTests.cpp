#include "gallery/ViewRequests.h"
#include "LocalPipePeer.h"
#include <atomic>
#include <cstdio>
#include <stdexcept>

namespace {
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
template<class F> void Wait(F ready,DWORD timeout=2000){const auto until=GetTickCount64()+timeout;while(!ready()&&GetTickCount64()<until)Sleep(1);Check(ready(),"bounded test wait");}
enum class Mode {Ready,PageWait,Loading,ImageWait,LateFirst,ImageGate,Failure};
struct Control {
    const DWORD ui=GetCurrentThreadId();
    std::atomic<Mode> mode{Mode::Ready};
    std::atomic_ulong pages{0},images{0},liveImages{0},maximumImages{0},canceled{0},uiQueries{0};
    UINT items=16;
    size_t pixelBytes=4;
    local_pipe::Handle release{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
};
Control* control=nullptr;
snapshot::Page Page(const snapshot::Location& location,HANDLE cancel) noexcept {
    if(GetCurrentThreadId()==control->ui)++control->uiQueries;
    const auto ordinal=++control->pages;const auto mode=control->mode.load();
    if(mode==Mode::PageWait){if(WaitForSingleObject(cancel,5000)==WAIT_OBJECT_0)++control->canceled;}
    snapshot::Page page;page.epoch=location.epoch;
    if(mode==Mode::Loading&&ordinal==1){page.status=snapshot::Status::Loading;return page;}
    try{
        page.status=snapshot::Status::Ready;
        for(ULONG index=0;index<control->items;++index)page.entries.push_back({location.epoch,{index+1},snapshot::Kind::File,L"fixture"});
    }catch(const std::bad_alloc&){page={};}
    return page;
}
thumbnail::Result Image(const snapshot::Location& location,HANDLE cancel) noexcept {
    if(GetCurrentThreadId()==control->ui)++control->uiQueries;
    const auto ordinal=++control->images,live=++control->liveImages;auto maximum=control->maximumImages.load();
    while(maximum<live&&!control->maximumImages.compare_exchange_weak(maximum,live)){}
    const auto mode=control->mode.load();
    if(mode==Mode::ImageWait){if(WaitForSingleObject(cancel,5000)==WAIT_OBJECT_0)++control->canceled;}
    if(mode==Mode::ImageGate)WaitForSingleObject(control->release.value,5000);
    if(mode==Mode::LateFirst&&ordinal==1)WaitForSingleObject(control->release.value,5000); // Deliberately returns after obsolete cancellation.
    thumbnail::Result result;result.location=location;result.status=thumbnail::Status::Ready;
    try{auto image=std::make_shared<gallery::Pbgra>();image->width=image->height=control->pixelBytes==4?1:512;image->stride=image->width*4;
        image->pixels.resize(control->pixelBytes);image->pixels[0]=static_cast<BYTE>(ordinal);image->pixels[3]=255;result.image=std::move(image);}
    catch(const std::bad_alloc&){result={};}
    if(mode==Mode::Failure){result.image.reset();result.status=thumbnail::Status::Unsupported;}
    --control->liveImages;return result;
}
struct Window {
    HWND value=CreateWindowExW(0,L"STATIC",L"",0,0,0,1,1,HWND_MESSAGE,nullptr,GetModuleHandleW(nullptr),nullptr);
    Window(){if(!value)throw std::runtime_error("owned hidden test window");}
    ~Window(){DestroyWindow(value);}
};
constexpr UINT Message=WM_APP+0x321;
const snapshot::Location location{{101},{201}};
gallery::VisibleFiles AllVisible(UINT count=16){gallery::VisibleFiles visible;visible.count=count;for(UINT i=0;i<count;++i)visible.indices[i]=i;return visible;}
void ReadyAndLoading(){
    Control state;control=&state;Window window;gallery::Requests requests({Page,Image});requests.Attach(window.value,Message);
    Check(SUCCEEDED(requests.Begin(location,1)),"page queued");Wait([]{return gallery::PendingWork()==0;});
    auto completed=requests.Take();Check(completed.size()==1&&completed[0].page&&completed[0].generation==1&&completed[0].snapshot.entries.size()==16,"complete page delivered from background");
    MSG message{};Check(PeekMessageW(&message,window.value,Message,Message,PM_REMOVE)&&message.wParam==1&&message.lParam==0,"completion message contains only generation hint");
    state.mode=Mode::Loading;state.pages=0;Check(SUCCEEDED(requests.Begin(location,2)),"new Loading episode");Wait([]{return gallery::PendingWork()==0;});
    completed=requests.Take();Check(state.pages==2&&completed.size()==1&&completed[0].snapshot.status==snapshot::Status::Ready,"bounded Loading retry publishes ready page");
    const auto calls=state.pages.load();Sleep(50);Check(state.pages==calls,"Ready has no polling");Check(!state.uiQueries,"no page query on UI thread");
}
void ObsoletePageAndCapacity(){
    Control state;control=&state;state.mode=Mode::PageWait;Window window;
    std::array<std::unique_ptr<gallery::Requests>,5> requests;
    for(UINT i=0;i<5;++i){requests[i]=std::make_unique<gallery::Requests>(gallery::Sources{Page,Image});requests[i]->Attach(window.value,Message);}
    for(UINT i=0;i<4;++i)Check(requests[i]->Begin(location,i+10)==S_OK,"four page workers");
    Wait([&]{return state.pages==4;});Check(requests[4]->Begin(location,20)==HRESULT_FROM_WIN32(ERROR_BUSY),"fifth page worker fails bounded");
    const auto before=GetTickCount64();for(UINT i=0;i<4;++i)requests[i]->Close();Check(GetTickCount64()-before<150,"close signals cancellation without joining workers");
    Wait([]{return gallery::PendingWork()==0;});Check(state.canceled==4,"all canceled page sources observed cancellation");
    for(UINT i=0;i<4;++i)Check(requests[i]->Take().empty(),"late canceled pages are discarded");
    state.mode=Mode::Ready;Check(requests[4]->Begin(location,21)==S_OK,"page capacity reusable");Wait([]{return gallery::PendingWork()==0;});
    auto current=requests[4]->Take();Check(current.size()==1&&current[0].generation==21,"new generation alone publishes");
}
void VisibleImages(){
    Control state;control=&state;Window window;gallery::Requests requests({Page,Image});requests.Attach(window.value,Message);
    Check(requests.Begin(location,30)==S_OK,"image page");Wait([]{return gallery::PendingWork()==0;});auto page=requests.Take()[0].snapshot;
    state.mode=Mode::ImageWait;Check(requests.Visible(page,AllVisible(),30)==S_OK,"sixteen visible candidates queued");
    Wait([&]{return state.liveImages==2;});Check(state.maximumImages==2&&gallery::PendingWork()==16,"only two image workers run");
    Check(requests.Visible(page,{},30)==S_OK,"viewport departure");Wait([]{return gallery::PendingWork()==0;});
    Check(state.canceled==2&&state.images==2&&requests.Take().empty(),"departure cancels active and queued candidates without stale images");
    state.mode=Mode::Ready;Check(requests.Visible(page,AllVisible(),30)==S_OK,"viewport re-entry");Wait([]{return gallery::PendingWork()==0;});
    auto images=requests.Take();Check(images.size()==16,"all visible candidates finish");
    std::array<bool,16> seen{};for(const auto& image:images){Check(!image.page&&image.generation==30&&image.index<16&&!seen[image.index]&&image.thumbnail.image,"validated current image completion");seen[image.index]=true;}
    const auto calls=state.images.load();Check(requests.Visible(page,AllVisible(),30)==S_OK&&gallery::PendingWork()==0&&state.images==calls,"completed visible images are not polled");
    Check(requests.Current(images[0]),"taken image remains current before UI call-outs");
    auto invalid=AllVisible();invalid.count=102;Check(requests.Visible(page,invalid,30)==E_INVALIDARG,"more than one page of candidates denied");
    invalid=AllVisible();invalid.indices[1]=0;Check(requests.Visible(page,invalid,30)==E_INVALIDARG,"duplicate candidates denied");
    auto stale=page;stale.epoch={999};Check(requests.Visible(stale,AllVisible(),30)==S_FALSE,"wrong page epoch denied");
    Check(!state.uiQueries&&state.maximumImages<=2,"image IPC stays off UI and bounded");
    Check(requests.Visible(page,{},30)==S_OK&&!requests.Current(images[0]),"viewport change during a taken UI batch invalidates the old ticket");
}
void ViewportTicketIdentity(){
    Control state;control=&state;Window window;gallery::Requests requests({Page,Image});requests.Attach(window.value,Message);
    Check(requests.Begin(location,40)==S_OK,"late-image page");Wait([]{return gallery::PendingWork()==0;});auto page=requests.Take()[0].snapshot;
    state.mode=Mode::LateFirst;gallery::VisibleFiles one;one.count=1;one.indices[0]=0;
    Check(requests.Visible(page,one,40)==S_OK,"first viewport ticket");Wait([&]{return state.images==1;});
    Check(requests.Visible(page,{},40)==S_OK&&requests.Visible(page,one,40)==S_OK,"same index leaves and re-enters same generation");
    Wait([&]{return state.images==2&&state.liveImages==1;});auto images=requests.Take();
    Check(images.size()==1&&images[0].thumbnail.image->pixels[0]==2,"only new viewport ticket publishes");
    SetEvent(state.release.value);Wait([]{return gallery::PendingWork()==0;});Check(requests.Take().empty(),"late old viewport response stays discarded");
    requests.Close();Check(requests.Begin(location,41)==E_INVALIDARG,"closed target cannot queue more work");
}
std::vector<gallery::Completion> Collect(gallery::Requests& requests,const snapshot::Page& page,UINT count,std::uint64_t generation){
    std::vector<gallery::Completion> result;std::array<bool,snapshot::MaxItems> seen{};
    Wait([&]{
        for(auto& item:requests.Take()){
            Check(!item.page&&requests.Current(item)&&item.index<count&&!seen[item.index],"one terminal result per current viewport ticket");
            seen[item.index]=true;result.push_back(std::move(item));
        }
        Check(requests.Visible(page,AllVisible(count),generation)==S_OK,"completion refills unchanged visible candidates");
        Check(gallery::PendingWork()<=gallery::MaxImageRequestsPerView,"per-view pipeline remains at most sixteen");
        return result.size()==count;
    });
    Wait([]{return gallery::PendingWork()==0;});return result;
}
void CompleteVisiblePage(){
    for(const auto mode:{Mode::Ready,Mode::Failure}){
        Control state;control=&state;state.items=29;Window window;gallery::Requests requests({Page,Image});requests.Attach(window.value,Message);
        Check(requests.Begin(location,50)==S_OK,"wide image page");Wait([]{return gallery::PendingWork()==0;});auto page=requests.Take()[0].snapshot;
        state.mode=Mode::ImageGate;Check(requests.Visible(page,AllVisible(29),50)==S_OK,"entire visible page accepted");
        Wait([&]{return state.liveImages==2;});Check(gallery::PendingWork()==16,"only sixteen unfinished tickets admitted from twenty-nine candidates");
        state.mode=mode;SetEvent(state.release.value);auto images=Collect(requests,page,29,50);
        Check(images.size()==29&&state.images==29,"every same-screen item eventually reaches a terminal state");
        UINT failed=0;for(const auto& item:images)if(!item.thumbnail.image)++failed;
        Check(mode==Mode::Ready?failed==0:failed==27,"unsupported images are terminal without retries");
        for(UINT i=0;i<5;++i){Check(requests.Visible(page,AllVisible(29),50)==S_OK,"stable completed viewport");Sleep(1);Check(requests.Take().empty(),"no repeated completion on stable viewport");}
        Check(state.images==29&&!state.uiQueries&&state.maximumImages==2,"stable viewport neither polls nor exceeds two image sources");
    }
}
void CanceledSlotsWakeNewGeneration(){
    Control state;control=&state;state.items=29;Window window;gallery::Requests requests({Page,Image});requests.Attach(window.value,Message);
    Check(requests.Begin(location,60)==S_OK,"cancel race page");Wait([]{return gallery::PendingWork()==0;});auto page=requests.Take()[0].snapshot;
    state.mode=Mode::ImageGate;Check(requests.Visible(page,AllVisible(29),60)==S_OK,"old generation fills pipeline");Wait([&]{return state.liveImages==2;});
    Check(requests.Begin(location,61)==S_OK,"new generation cancels old pipeline without joining");
    Wait([&]{return state.pages==2&&gallery::PendingWork()==16;});auto fresh=requests.Take();Check(fresh.size()==1&&fresh[0].page,"new page delivered while canceled image sources remain inside callbacks");page=std::move(fresh[0].snapshot);
    Check(requests.Visible(page,AllVisible(29),61)==S_OK&&gallery::PendingWork()==16&&state.images==2,"canceled but unfinished tickets still consume all slots");
    MSG message{};while(PeekMessageW(&message,window.value,Message,Message,PM_REMOVE)){}
    state.mode=Mode::Ready;SetEvent(state.release.value);Wait([]{return gallery::PendingWork()==0;});
    bool currentWake=false;while(PeekMessageW(&message,window.value,Message,Message,PM_REMOVE)){
        Check(message.lParam==0,"slot recycle wake borrows no payload");currentWake=currentWake||message.wParam==61;
    }
    Check(currentWake&&requests.Take().empty(),"obsolete work wakes new generation without publishing obsolete images");
    auto images=Collect(requests,page,29,61);Check(images.size()==29&&state.images==31,"all new candidates progress after obsolete slots retire");
}
void FourViewImageCapacity(){
    Control state;control=&state;state.items=29;Window window;
    std::array<std::unique_ptr<gallery::Requests>,4> requests;
    std::array<snapshot::Page,4> pages;std::array<UINT,4> totals{};
    for(UINT i=0;i<4;++i){requests[i]=std::make_unique<gallery::Requests>(gallery::Sources{Page,Image});requests[i]->Attach(window.value,Message);Check(requests[i]->Begin(location,80+i)==S_OK,"four views begin");}
    Wait([]{return gallery::PendingWork()==0;});for(UINT i=0;i<4;++i)pages[i]=requests[i]->Take()[0].snapshot;
    state.mode=Mode::ImageGate;for(UINT i=0;i<4;++i)Check(requests[i]->Visible(pages[i],AllVisible(29),80+i)==S_OK,"four bounded image pipelines");
    Wait([&]{return state.liveImages==2;});Check(gallery::PendingWork()==64,"four view pipelines fill exactly sixty-four module work slots");
    state.mode=Mode::Ready;SetEvent(state.release.value);
    Wait([&]{UINT total=0;for(UINT i=0;i<4;++i){for(const auto& item:requests[i]->Take()){Check(requests[i]->Current(item)&&item.thumbnail.image,"four-view terminal image");++totals[i];}
            Check(requests[i]->Visible(pages[i],AllVisible(29),80+i)==S_OK,"four-view completion refills");total+=totals[i];}
        Check(gallery::PendingWork()<=64,"module work never exceeds sixty-four");return total==116;});
    Wait([]{return gallery::PendingWork()==0;});for(const auto total:totals)Check(total==29,"each of four views completes its whole visible page");
    Check(state.images==116&&state.maximumImages==2,"four views share only two image I/O workers");
}
size_t Pixels(const std::vector<gallery::Completion>& images){size_t total=0;for(const auto& item:images)if(item.thumbnail.image)total+=item.thumbnail.image->pixels.capacity();return total;}
void ResidentPixelLeases(){
    Control state;control=&state;state.items=29;state.pixelBytes=1024u*1024u;Window window;
    gallery::Requests requests({Page,Image});requests.Attach(window.value,Message);
    Check(requests.Begin(location,90)==S_OK,"pixel budget page");Wait([]{return gallery::PendingWork()==0;});auto page=requests.Take()[0].snapshot;
    auto cached=Collect(requests,page,16,90);Check(Pixels(cached)==gallery::MaxImageBytes,"first batch fills resident sixteen MiB");
    Check(requests.Visible(page,AllVisible(29),90)==S_OK,"later visible images are still attempted within same byte budget");Wait([]{return gallery::PendingWork()==0;});auto later=requests.Take();
    Check(later.size()==13&&Pixels(later)==0&&Pixels(cached)+Pixels(later)==gallery::MaxImageBytes,"cache pointers plus completed second batch cannot double resident bytes");
    for(const auto& item:later)Check(item.thumbnail.status==thumbnail::Status::Busy&&!item.thumbnail.image,"byte rejection is an honest terminal fallback");later.clear();
    Check(requests.Begin(location,91)==S_OK,"new generation shares still-held pixel budget");Wait([]{return gallery::PendingWork()==0;});page=requests.Take()[0].snapshot;
    auto denied=Collect(requests,page,1,91);Check(Pixels(denied)==0,"Cancel never resets budget while old generation images remain referenced");denied.clear();
    for(UINT i=0;i<8;++i)cached[i].thumbnail.image.reset();
    Check(requests.Begin(location,92)==S_OK,"partial old-lease retirement");Wait([]{return gallery::PendingWork()==0;});page=requests.Take()[0].snapshot;
    auto next=Collect(requests,page,9,92);Check(Pixels(next)==8u*1024u*1024u&&Pixels(next)+Pixels(cached)==gallery::MaxImageBytes,"only actually released bytes admit next generation");
    cached.clear();next.clear();
    Check(requests.Begin(location,93)==S_OK,"all leases retired");Wait([]{return gallery::PendingWork()==0;});page=requests.Take()[0].snapshot;
    auto recovered=Collect(requests,page,16,93);Check(Pixels(recovered)==gallery::MaxImageBytes,"delayed old lease destruction restores exact capacity without underflow");
    requests.Close();Check(Pixels(recovered)==gallery::MaxImageBytes,"external plain leases safely outlive closed request controller");
}
}
int main(){
    try{ReadyAndLoading();ObsoletePageAndCapacity();VisibleImages();ViewportTicketIdentity();CompleteVisiblePage();CanceledSlotsWakeNewGeneration();FourViewImageCapacity();ResidentPixelLeases();Check(!gallery::PendingWork(),"all background state reclaimed");
        puts("gallery_requests=passed; stale_pages=discarded; old_viewport_images=discarded; page_work=4; image_threads=2; candidates=101; unfinished_per_view=16; resident_pixels_per_view=16MiB; UI_query=0; GUI=0; pipe=0");return 0;
    }catch(const std::exception& error){fprintf(stderr,"GalleryRequestTests: %s\n",error.what());return 1;}
}
