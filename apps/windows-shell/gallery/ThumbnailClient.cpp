#include "ThumbnailClient.h"
#include "../LocalPipePeer.h"
#include <atomic>
#include <cstring>
#include <new>
#include <stdexcept>

namespace thumbnail {
namespace {
std::atomic_ulong active{0},sequence{0};
USHORT U16(const BYTE* bytes){USHORT value;std::memcpy(&value,bytes,2);return value;}
ULONG U32(const BYTE* bytes){ULONG value;std::memcpy(&value,bytes,4);return value;}
void Put16(BYTE* bytes,USHORT value){std::memcpy(bytes,&value,2);}
void Put32(BYTE* bytes,ULONG value){std::memcpy(bytes,&value,4);}
struct Operation;
void CALLBACK Reap(PTP_CALLBACK_INSTANCE,void*,PTP_WAIT,TP_WAIT_RESULT);
struct Operation {
    local_pipe::Handle pipe,event,peer;
    OVERLAPPED overlapped{};
    PTP_WAIT wait=nullptr;
    HMODULE module=nullptr;
    bool pending=false;
    std::array<BYTE,48> request{};
    std::array<BYTE,16> header{};
    std::vector<BYTE> body;
    Operation(){event.value=CreateEventW(nullptr,TRUE,FALSE,nullptr);overlapped.hEvent=event.value;if(event.value)wait=CreateThreadpoolWait(Reap,this,nullptr);}
    ~Operation(){if(wait)CloseThreadpoolWait(wait);}
    bool Pin(){
        HMODULE owner=nullptr;const auto address=reinterpret_cast<LPCWSTR>(&Reap);
        if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,address,&owner))return false;
        return owner==GetModuleHandleW(nullptr)||GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,address,&module)!=FALSE;
    }
};
HMODULE ReleaseResources(Operation* operation) noexcept {
    const auto module=operation->module;delete operation;--active;return module;
}
struct ReleaseOperation {
    void operator()(Operation* operation)const noexcept {
        const auto module=ReleaseResources(operation);
        // The owning background callback holds its own module pin across Query.
        if(module)FreeLibrary(module);
    }
};
using OwnedOperation=std::unique_ptr<Operation,ReleaseOperation>;
void CALLBACK Reap(PTP_CALLBACK_INSTANCE instance,void* context,PTP_WAIT,TP_WAIT_RESULT){
    const auto module=ReleaseResources(static_cast<Operation*>(context));
    if(module)FreeLibraryWhenCallbackReturns(instance,module);
}
struct Deadline {
    ULONGLONG end=GetTickCount64()+WaitBudgetMs;
    DWORD Remaining()const noexcept {const auto now=GetTickCount64();return now>=end?0:static_cast<DWORD>(end-now);}
};
bool Transfer(Operation& operation,bool write,BYTE* buffer,DWORD length,const Deadline& deadline,HANDLE cancel){
    DWORD at=0;
    while(at<length){
        if(!deadline.Remaining()||(cancel&&WaitForSingleObject(cancel,0)==WAIT_OBJECT_0))return false;
        ResetEvent(operation.event.value);DWORD transferred=0;
        BOOL done=write?WriteFile(operation.pipe.value,buffer+at,length-at,&transferred,&operation.overlapped):ReadFile(operation.pipe.value,buffer+at,length-at,&transferred,&operation.overlapped);
        if(!done){
            if(GetLastError()!=ERROR_IO_PENDING)return false;operation.pending=true;
            const HANDLE events[]={operation.event.value,cancel};
            if(WaitForMultipleObjects(cancel?2:1,events,FALSE,deadline.Remaining())!=WAIT_OBJECT_0)return false;
            done=GetOverlappedResult(operation.pipe.value,&operation.overlapped,&transferred,FALSE);
            if(!done&&GetLastError()==ERROR_IO_INCOMPLETE)return false;
            operation.pending=false;if(!done)return false;
        }
        if(!transferred||transferred>length-at)return false;at+=transferred;
    }
    return deadline.Remaining()!=0;
}
void Finish(OwnedOperation& operation){
    if(!operation||!operation->pending)return;
    CancelIoEx(operation->pipe.value,&operation->overlapped);DWORD ignored=0;
    if(GetOverlappedResult(operation->pipe.value,&operation->overlapped,&ignored,FALSE)||GetLastError()!=ERROR_IO_INCOMPLETE){operation->pending=false;return;}
    // At most two retained image operations, including canceled buffers/handles.
    auto retained=operation.release();SetThreadpoolWait(retained->wait,retained->event.value,nullptr);
}
}
std::array<BYTE,48> Request(const snapshot::Location& location,ULONG requestId){
    if(!requestId||snapshot::Zero(location.epoch)||snapshot::Zero(location.node))throw std::invalid_argument("invalid thumbnail identity");
    std::array<BYTE,48> result{};
    Put32(result.data(),Magic);Put16(result.data()+4,1);Put16(result.data()+6,1);Put32(result.data()+8,32);Put32(result.data()+12,requestId);
    std::memcpy(result.data()+16,&location.epoch,16);std::memcpy(result.data()+32,&location.node,16);return result;
}
bool ResponseHeader(const BYTE* header,ULONG requestId,ULONG& bytes) noexcept {
    bytes=0;if(!header||!requestId||U32(header)!=Magic||U16(header+4)!=1||U16(header+6)!=2||U32(header+12)!=requestId)return false;
    const ULONG size=U32(header+8);if(size<PrefixBytes||size>MaxPayload)return false;bytes=size;return true;
}
bool Decode(const BYTE* payload,size_t bytes,const snapshot::Location& requested,Result& result){
    result=Result{};result.status=Status::InvalidResponse;
    if(!payload||bytes<PrefixBytes||bytes>MaxPayload||snapshot::Zero(requested.epoch)||snapshot::Zero(requested.node))return false;
    Result parsed;const auto status=U32(payload);if(status>static_cast<ULONG>(Status::Unsupported))return false;
    parsed.status=static_cast<Status>(status);std::memcpy(&parsed.location.epoch,payload+4,16);std::memcpy(&parsed.location.node,payload+20,16);
    if(parsed.location.node!=requested.node)return false;
    const auto width=U32(payload+36),height=U32(payload+40),stride=U32(payload+44),format=U32(payload+48),length=U32(payload+52);
    if(parsed.status==Status::Ready){
        if(parsed.location.epoch!=requested.epoch||!width||width>512||!height||height>512||stride!=width*4||format!=1
            ||length!=stride*height||length>MaxPixelBytes||bytes!=PrefixBytes+length)return false;
        for(ULONG at=PrefixBytes;at<bytes;at+=4)if(payload[at]>payload[at+3]||payload[at+1]>payload[at+3]||payload[at+2]>payload[at+3])return false;
        auto image=std::make_shared<gallery::Pbgra>();image->width=width;image->height=height;image->stride=stride;
        image->pixels.assign(payload+PrefixBytes,payload+bytes);parsed.image=std::move(image);
    }else if(width||height||stride||format||length||bytes!=PrefixBytes)return false;
    result=std::move(parsed);return true;
}
std::wstring PipeName(){return local_pipe::Name(L"\\\\.\\pipe\\AssetLibrary.ExplorerThumbnail.v1.");}
ULONG PendingOperations() noexcept {return active.load();}
Result Query(const snapshot::Location& location,HANDLE cancel) noexcept {
    Deadline deadline;Result result;
    if(snapshot::Zero(location.epoch)||snapshot::Zero(location.node)){result.status=Status::InvalidResponse;return result;}
    FILETIME now{};GetSystemTimeAsFileTime(&now);const ULONGLONG started=(static_cast<ULONGLONG>(now.dwHighDateTime)<<32)|now.dwLowDateTime;
    ULONG count=active.load();do{if(count>=2){result.status=Status::Busy;return result;}}while(!active.compare_exchange_weak(count,count+1));
    OwnedOperation operation(new(std::nothrow) Operation());if(!operation){--active;result.status=Status::Busy;return result;}
    try {
        do{
            if(!operation->event.value||!operation->wait||!operation->Pin()){result.status=Status::Busy;break;}
            if(cancel&&WaitForSingleObject(cancel,0)==WAIT_OBJECT_0)break;
            const auto name=PipeName();if(name.empty()||!deadline.Remaining())break;
            operation->pipe.value=CreateFileW(name.c_str(),GENERIC_READ|GENERIC_WRITE,0,nullptr,OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED|SECURITY_SQOS_PRESENT|SECURITY_IDENTIFICATION,nullptr);
            if(operation->pipe.value==INVALID_HANDLE_VALUE){if(GetLastError()==ERROR_PIPE_BUSY)result.status=Status::Busy;break;}
            HANDLE peer=nullptr;if(!local_pipe::SamePeer(operation->pipe.value,peer,started)){result.status=Status::AccessDenied;break;}operation->peer.value=peer;
            ULONG id=sequence.fetch_add(1)+1;if(!id)id=sequence.fetch_add(1)+1;operation->request=thumbnail::Request(location,id);
            if(!Transfer(*operation,true,operation->request.data(),48,deadline,cancel)||!Transfer(*operation,false,operation->header.data(),16,deadline,cancel))break;
            ULONG bytes=0;if(!ResponseHeader(operation->header.data(),id,bytes)){result.status=Status::InvalidResponse;break;}
            operation->body.resize(bytes);if(!Transfer(*operation,false,operation->body.data(),bytes,deadline,cancel))break;
            if(WaitForSingleObject(operation->peer.value,0)!=WAIT_TIMEOUT)break;
            if(cancel&&WaitForSingleObject(cancel,0)==WAIT_OBJECT_0)break;
            Decode(operation->body.data(),operation->body.size(),location,result);
        }while(false);
    }catch(...){result=Result{};result.status=Status::InvalidResponse;}
    Finish(operation);return result;
}
}
