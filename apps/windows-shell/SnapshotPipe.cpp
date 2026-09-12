#include "Snapshot.h"
#include "ProductIdentity.h"
#include "G3Measurements.h"
#include "LocalPipePeer.h"
#include <atomic>
#include <memory>
#include <new>
#include <cstring>

namespace snapshot {
namespace {
std::atomic_ulong active{0}, sequence{0};
using local_pipe::Handle;
using local_pipe::SamePeer;
struct Operation;
void CALLBACK Reap(PTP_CALLBACK_INSTANCE instance,void* context,PTP_WAIT,TP_WAIT_RESULT);
struct Operation {
    Handle pipe,event,peer;
    OVERLAPPED overlapped{};
    PTP_WAIT wait=nullptr;
    HMODULE module=nullptr;
    ULONGLONG cancelStarted=0;
    bool pending=false;
    std::array<BYTE,48> request{};
    std::array<BYTE,16> header{};
    std::vector<BYTE> body;
    Operation(){
        event.value=CreateEventW(nullptr,TRUE,FALSE,nullptr);overlapped.hEvent=event.value;
        if(event.value)wait=CreateThreadpoolWait(Reap,this,nullptr);
    }
    ~Operation(){
        if(wait)CloseThreadpoolWait(wait);
    }
    bool Pin(){
        HMODULE owner=nullptr;
        const auto address=reinterpret_cast<LPCWSTR>(&Reap);
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,address,&owner))return false;
        if(owner==GetModuleHandleW(nullptr))return true;
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,address,&module))return false;
        ++g3::measurements.pins;return true;
    }
};
HMODULE ReleaseResources(Operation* operation) noexcept {
    const auto module=operation->module;const auto canceled=operation->cancelStarted;
    // Publish completion only after all handles, buffers AND object storage end.
    // The module remains pinned while member destructors and this code execute.
    delete operation;
    if(canceled){
        const auto elapsed=g3::Now()-canceled;auto& m=g3::measurements;
        m.cancelLast=elapsed;g3::Maximum(m.cancelMaximum,elapsed);++m.reaped;--m.cancelLive;
    }
    active.fetch_sub(1);return module;
}
struct ReleaseOperation {
    void operator()(Operation* operation) const noexcept {
        const auto module=ReleaseResources(operation);
        // The Folder COM caller or gallery background callback owns a module pin.
        // That COM owner keeps the caller's code alive after this final pin return;
        // statically linked test callers belong to the process executable.
        if(module){++g3::measurements.pinSync;--g3::measurements.pins;FreeLibrary(module);}
    }
};
using OwnedOperation=std::unique_ptr<Operation,ReleaseOperation>;
void CALLBACK Reap(PTP_CALLBACK_INSTANCE instance,void* context,PTP_WAIT,TP_WAIT_RESULT){
    auto operation=static_cast<Operation*>(context);
    // The private manual-reset event is signalled only by completion of our I/O.
    const auto module=ReleaseResources(operation);
    // This is a return scheduled with the pool, not an observation of DLL unload.
    if(module){++g3::measurements.pinPool;--g3::measurements.pins;FreeLibraryWhenCallbackReturns(instance,module);}
}
struct Deadline {
    ULONGLONG end=GetTickCount64()+WaitBudgetMs;
    DWORD Remaining()const{const auto now=GetTickCount64();return now>=end?0:static_cast<DWORD>(end-now);}
};
enum class Io { Complete, Failed, Expired };
Io Transfer(Operation& operation,bool write,BYTE* buffer,DWORD length,const Deadline& deadline,HANDLE cancel){
    DWORD at=0;
    while(at<length){
        if(!deadline.Remaining()||(cancel&&WaitForSingleObject(cancel,0)==WAIT_OBJECT_0))return Io::Expired;
        ResetEvent(operation.event.value);DWORD transferred=0;
        BOOL done=write?WriteFile(operation.pipe.value,buffer+at,length-at,&transferred,&operation.overlapped):ReadFile(operation.pipe.value,buffer+at,length-at,&transferred,&operation.overlapped);
        if(!done){
            if(GetLastError()!=ERROR_IO_PENDING)return Io::Failed;
            operation.pending=true;
            const HANDLE events[]={operation.event.value,cancel};
            const DWORD result=WaitForMultipleObjects(cancel?2:1,events,FALSE,deadline.Remaining());
            if(result!=WAIT_OBJECT_0)return Io::Expired;
            done=GetOverlappedResult(operation.pipe.value,&operation.overlapped,&transferred,FALSE);
            if(!done&&GetLastError()==ERROR_IO_INCOMPLETE)return Io::Expired;
            operation.pending=false;
            if(!done)return Io::Failed;
        }
        if(!transferred||transferred>length-at)return Io::Failed;
        at+=transferred;
    }
    return deadline.Remaining()?Io::Complete:Io::Expired;
}
void Finish(OwnedOperation& operation){
    if(!operation||!operation->pending)return;
    operation->cancelStarted=g3::Now();++g3::measurements.cancels;++g3::measurements.cancelLive;
    CancelIoEx(operation->pipe.value,&operation->overlapped);
    DWORD ignored=0;
    if(GetOverlappedResult(operation->pipe.value,&operation->overlapped,&ignored,FALSE)||GetLastError()!=ERROR_IO_INCOMPLETE){operation->pending=false;return;}
    // No foreground wait for cancellation completion. Four slots bound retained storage.
    ++g3::measurements.deferred;
    Operation* retained=operation.release();SetThreadpoolWait(retained->wait,retained->event.value,nullptr);
}
}
ULONG PendingOperations() noexcept { return active.load(); }
std::wstring PipeName(){return local_pipe::Name(product::PipePrefix);}
Page Query(const Location& location,HANDLE cancel) noexcept {
    g3::Call measured(g3::measurements.query);
    Deadline deadline;FILETIME now{};GetSystemTimeAsFileTime(&now);
    const ULONGLONG started=(static_cast<ULONGLONG>(now.dwHighDateTime)<<32)|now.dwLowDateTime;
    Page page;ULONG count=active.load();
    do{if(count>=4){page.status=Status::Busy;return page;}}while(!active.compare_exchange_weak(count,count+1));
    OwnedOperation operation(new(std::nothrow) Operation());
    if(!operation){active.fetch_sub(1);page.status=Status::Busy;return page;}
    try{
        do{
            if(!operation->event.value||!operation->wait||!operation->Pin()){page.status=Status::Busy;break;}
            const auto name=PipeName();if(name.empty()||!deadline.Remaining())break;
            if(cancel&&WaitForSingleObject(cancel,0)==WAIT_OBJECT_0)break;
            constexpr DWORD flags=FILE_FLAG_OVERLAPPED|SECURITY_SQOS_PRESENT|SECURITY_IDENTIFICATION;
            operation->pipe.value=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,flags,nullptr);
            if(operation->pipe.value==INVALID_HANDLE_VALUE&&GetLastError()==ERROR_PIPE_BUSY){
                // Busy is immediate; a later manual refresh can retry.
                page.status=Status::Busy;break;
            }
            if(operation->pipe.value==INVALID_HANDLE_VALUE)break;
            HANDLE peer=nullptr;if(!SamePeer(operation->pipe.value,peer,started)){page.status=Status::AccessDenied;break;}operation->peer.value=peer;
            ULONG id=sequence.fetch_add(1)+1;if(!id)id=sequence.fetch_add(1)+1;
            operation->request=Request(location,id);
            if(Transfer(*operation,true,operation->request.data(),48,deadline,cancel)!=Io::Complete)break;
            if(Transfer(*operation,false,operation->header.data(),16,deadline,cancel)!=Io::Complete)break;
            ULONG bytes=0;if(!ResponseHeader(operation->header.data(),id,bytes)){page.status=Status::InvalidResponse;break;}
            operation->body.resize(bytes);
            if(Transfer(*operation,false,operation->body.data(),bytes,deadline,cancel)!=Io::Complete)break;
            if(WaitForSingleObject(operation->peer.value,0)!=WAIT_TIMEOUT){page.status=Status::Unavailable;break;}
            if(!Decode(operation->body.data(),operation->body.size(),page))break;
            if(!Zero(location.epoch)&&page.status==Status::Ready&&page.epoch!=location.epoch){page.entries.clear();page.status=Status::Expired;}
        }while(false);
    }catch(...){page=Page{};page.status=Status::InvalidResponse;}
    Finish(operation);return page;
}
}
