#include "gallery/ViewRequests.h"
#include "LocalPipePeer.h"
#include <atomic>
#include <cstdio>
#include <stdexcept>

namespace {
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
template<class F> void Wait(F ready,DWORD timeout=2000){const auto until=GetTickCount64()+timeout;while(!ready()&&GetTickCount64()<until)Sleep(1);Check(ready(),"bounded test wait");}
enum class Mode {Ready,PageWait,Loading,ImageWait,LateFirst};
struct Control {
    const DWORD ui=GetCurrentThreadId();
    std::atomic<Mode> mode{Mode::Ready};
    std::atomic_ulong pages{0},images{0},liveImages{0},maximumImages{0},canceled{0},uiQueries{0};
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
        for(ULONG index=0;index<16;++index)page.entries.push_back({location.epoch,{index+1},snapshot::Kind::File,L"fixture"});
    }catch(const std::bad_alloc&){page={};}
    return page;
}
thumbnail::Result Image(const snapshot::Location& location,HANDLE cancel) noexcept {
    if(GetCurrentThreadId()==control->ui)++control->uiQueries;
    const auto ordinal=++control->images,live=++control->liveImages;auto maximum=control->maximumImages.load();
    while(maximum<live&&!control->maximumImages.compare_exchange_weak(maximum,live)){}
    const auto mode=control->mode.load();
    if(mode==Mode::ImageWait){if(WaitForSingleObject(cancel,5000)==WAIT_OBJECT_0)++control->canceled;}
    if(mode==Mode::LateFirst&&ordinal==1)WaitForSingleObject(control->release.value,5000); // Deliberately returns after obsolete cancellation.
    thumbnail::Result result;result.location=location;result.status=thumbnail::Status::Ready;
    try{auto image=std::make_shared<gallery::Pbgra>();image->width=image->height=1;image->stride=4;image->pixels={static_cast<BYTE>(ordinal),0,0,255};result.image=std::move(image);}
    catch(const std::bad_alloc&){result={};}
    --control->liveImages;return result;
}
struct Window {
    HWND value=CreateWindowExW(0,L"STATIC",L"",0,0,0,1,1,HWND_MESSAGE,nullptr,GetModuleHandleW(nullptr),nullptr);
    Window(){if(!value)throw std::runtime_error("owned hidden test window");}
    ~Window(){DestroyWindow(value);}
};
constexpr UINT Message=WM_APP+0x321;
const snapshot::Location location{{101},{201}};
gallery::VisibleFiles AllVisible(){gallery::VisibleFiles visible;visible.count=16;for(UINT i=0;i<16;++i)visible.indices[i]=i;return visible;}
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
    auto invalid=AllVisible();invalid.count=17;Check(requests.Visible(page,invalid,30)==E_INVALIDARG,"seventeen candidates denied");
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
}
int main(){
    try{ReadyAndLoading();ObsoletePageAndCapacity();VisibleImages();ViewportTicketIdentity();Check(!gallery::PendingWork(),"all background state reclaimed");
        puts("gallery_requests=passed; stale_pages=discarded; old_viewport_images=discarded; page_work=4; image_threads=2; candidates=16; UI_query=0; GUI=0; pipe=0");return 0;
    }catch(const std::exception& error){fprintf(stderr,"GalleryRequestTests: %s\n",error.what());return 1;}
}
