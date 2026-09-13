#include "gallery/ThumbnailClient.h"
#include "LocalPipePeer.h"
#include "ThumbnailVectors.generated.h"
#include "PreviewVectors.generated.h"
#include <sddl.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <stdexcept>
#include <thread>

namespace {
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
using Bytes=std::vector<BYTE>;
Bytes Hex(const char* text){
    Bytes bytes;const auto length=strlen(text);Check(length%2==0,"vector hex length");
    const auto digit=[](char c){return c<='9'?c-'0':c-'a'+10;};
    for(size_t i=0;i<length;i+=2)bytes.push_back(static_cast<BYTE>(digit(text[i])*16+digit(text[i+1])));return bytes;
}
ULONG U32(const BYTE* p){ULONG value;std::memcpy(&value,p,4);return value;}
void Put32(BYTE* p,ULONG value){std::memcpy(p,&value,4);}
snapshot::Location Location(){
    const auto request=Hex(ThumbnailVectors[0].hex);snapshot::Location location;
    std::memcpy(&location.epoch,request.data()+16,16);std::memcpy(&location.node,request.data()+32,16);return location;
}
void Vectors(){
    const auto location=Location();unsigned checked=0;
    for(const auto& vector:ThumbnailVectors){
        auto bytes=Hex(vector.hex);bool valid=false;
        if(strcmp(vector.direction,"request")==0){
            snapshot::Location requested;std::memcpy(&requested.epoch,bytes.data()+16,16);std::memcpy(&requested.node,bytes.data()+32,16);
            try{const auto encoded=thumbnail::Request(requested,U32(bytes.data()+12));valid=bytes.size()==encoded.size()&&std::equal(bytes.begin(),bytes.end(),encoded.begin());}
            catch(const std::invalid_argument&){valid=false;}
        }else{
            ULONG length=0;thumbnail::Result decoded;
            valid=thumbnail::ResponseHeader(bytes.data(),17,length)&&bytes.size()==length+16
                &&thumbnail::Decode(bytes.data()+16,length,location,decoded);
            if(valid&&decoded.status==thumbnail::Status::Ready)Check(decoded.image&&decoded.image->width==2&&decoded.image->height==2&&decoded.image->pixels.size()==16,"independent alpha image");
            if(!valid)Check(!decoded.image,"invalid vector cannot retain pixels");
        }
        if(valid!=vector.valid){fprintf(stderr,"vector=%s\n",vector.name);Check(false,"independent thumbnail vector decision");}++checked;
    }
    auto ready=Hex(ThumbnailVectors[1].hex);thumbnail::Result decoded;
    Check(thumbnail::Decode(ready.data()+16,ready.size()-16,location,decoded)&&decoded.image,"prior pixel state");
    ready[16+4]^=1;Check(!thumbnail::Decode(ready.data()+16,ready.size()-16,location,decoded)&&!decoded.image,"wrong epoch clears prior pixels");ready[20]^=1;
    ready[16+20]^=1;Check(!thumbnail::Decode(ready.data()+16,ready.size()-16,location,decoded),"wrong node rejected");ready[36]^=1;
    ready[16+56]=1;ready[16+56+3]=0;Check(!thumbnail::Decode(ready.data()+16,ready.size()-16,location,decoded),"non-premultiplied alpha rejected");
    auto absent=Hex(ThumbnailVectors[2].hex);
    for(ULONG status=1;status<=7;++status){Put32(absent.data()+16,status);Check(thumbnail::Decode(absent.data()+16,56,location,decoded)&&!decoded.image,"non-Ready statuses never carry prior pixels");}
    Put32(absent.data()+16+36,1);Check(!thumbnail::Decode(absent.data()+16,56,location,decoded),"non-Ready dimensions rejected");
    ULONG length=0;Put32(ready.data()+8,thumbnail::MaxPayload+1);Check(!thumbnail::ResponseHeader(ready.data(),17,length),"header maximum before allocation");
    Bytes maximum(56+thumbnail::MaxPixelBytes,0);std::memcpy(maximum.data()+4,&location.epoch,16);std::memcpy(maximum.data()+20,&location.node,16);
    Put32(maximum.data()+36,512);Put32(maximum.data()+40,512);Put32(maximum.data()+44,2048);Put32(maximum.data()+48,1);Put32(maximum.data()+52,thumbnail::MaxPixelBytes);
    Check(thumbnail::Decode(maximum.data(),maximum.size(),location,decoded)&&decoded.image->pixels.size()==1048576,"exact maximum image accepted");
    Put32(maximum.data()+40,0xffffffff);Check(!thumbnail::Decode(maximum.data(),maximum.size(),location,decoded)&&!decoded.image,"dimension overflow rejected");
    printf("thumbnail_vectors=%u; limits=passed; PBGRA=validated\n",checked);
}
bool Io(HANDLE pipe,HANDLE stop,bool write,BYTE* bytes,DWORD count,DWORD timeout){
    local_pipe::Handle event(CreateEventW(nullptr,TRUE,FALSE,nullptr));if(!event.value)return false;
    OVERLAPPED overlapped{};overlapped.hEvent=event.value;DWORD transferred=0;
    BOOL complete=write?WriteFile(pipe,bytes,count,&transferred,&overlapped):ReadFile(pipe,bytes,count,&transferred,&overlapped);
    if(!complete){
        if(GetLastError()!=ERROR_IO_PENDING)return false;
        const HANDLE waits[]={event.value,stop};
        if(WaitForMultipleObjects(2,waits,FALSE,timeout)!=WAIT_OBJECT_0){
            CancelIoEx(pipe,&overlapped);
            if(WaitForSingleObject(event.value,1000)!=WAIT_OBJECT_0)TerminateProcess(GetCurrentProcess(),70);
            return false;
        }
        complete=GetOverlappedResult(pipe,&overlapped,&transferred,FALSE);
    }
    return complete&&transferred==count;
}

void PreviewVectorsAndLimits(){
    const auto location=Location();
    for(const auto& vector:PreviewVectors){
        const auto bytes=Hex(vector.hex);bool valid=false;
        if(strcmp(vector.direction,"request")==0){const auto request=preview::Request(location,17);valid=bytes.size()==request.size()&&std::equal(bytes.begin(),bytes.end(),request.begin());}
        else{ULONG length=0;preview::Result result;valid=preview::ResponseHeader(bytes.data(),17,length)&&bytes.size()==16+length&&preview::Decode(bytes.data()+16,length,location,result);if(!valid)Check(!result.image,"invalid preview exposes no pixels");}
        if(valid!=vector.valid){fprintf(stderr,"preview vector=%s\n",vector.name);Check(false,"independent preview vector decision");}
    }
    Bytes maximum(preview::MaxPayload,0);std::memcpy(maximum.data()+4,&location.epoch,16);std::memcpy(maximum.data()+20,&location.node,16);
    Put32(maximum.data()+36,1600);Put32(maximum.data()+40,1600);Put32(maximum.data()+44,6400);Put32(maximum.data()+48,1);Put32(maximum.data()+52,preview::MaxPixelBytes);
    preview::Result result;Check(preview::Decode(maximum.data(),maximum.size(),location,result)&&result.image->pixels.size()==10240000,"1600 square preview maximum accepted");
    Check(!thumbnail::Decode(maximum.data(),maximum.size(),location,result)&&!result.image,"thumbnail never accepts preview allocation");
    Put32(maximum.data()+40,1601);Check(!preview::Decode(maximum.data(),maximum.size(),location,result),"preview height overflow denied");
    puts("preview_vectors=20; maximum=1600x1600; thumbnail_boundary=preserved");
}
enum class Mode {Ready,Invalid,Partial,Silent};
class Server {
    local_pipe::Handle stop_{CreateEventW(nullptr,TRUE,FALSE,nullptr)};
    std::vector<std::unique_ptr<local_pipe::Handle>> pipes_;
    std::vector<std::thread> workers_;
public:
    std::atomic_ulong received{0},listening{0};
    explicit Server(Mode mode,unsigned instances=1,bool preview=false){
        std::vector<BYTE> token;Check(local_pipe::UserSid(GetCurrentProcess(),token),"test actual TokenUser");LPWSTR sid=nullptr;
        Check(ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(token.data())->User.Sid,&sid)!=FALSE,"test SID");
        const std::wstring sddl=L"D:P(A;;GA;;;"+std::wstring(sid)+L")";LocalFree(sid);
        PSECURITY_DESCRIPTOR descriptor=nullptr;Check(ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl.c_str(),SDDL_REVISION_1,&descriptor,nullptr)!=FALSE,"test DACL");
        SECURITY_ATTRIBUTES security{sizeof(security),descriptor,FALSE};
        for(unsigned i=0;i<instances;++i){
            HANDLE pipe=CreateNamedPipeW((preview?preview::PipeName():thumbnail::PipeName()).c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_OVERLAPPED|(i==0?FILE_FLAG_FIRST_PIPE_INSTANCE:0),
                PIPE_TYPE_BYTE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,2,65536,65536,0,&security);
            if(pipe==INVALID_HANDLE_VALUE){LocalFree(descriptor);throw std::runtime_error("thumbnail test endpoint unavailable; do not replace a real Host");}
            pipes_.push_back(std::make_unique<local_pipe::Handle>(pipe));
        }
        LocalFree(descriptor);
        for(const auto& pipe:pipes_)workers_.emplace_back([&,handle=pipe->value,mode,preview]{
            local_pipe::Handle event(CreateEventW(nullptr,TRUE,FALSE,nullptr));OVERLAPPED connection{};connection.hEvent=event.value;
            BOOL connected=ConnectNamedPipe(handle,&connection);const auto error=connected?ERROR_SUCCESS:GetLastError();++listening;
            if(!connected&&error==ERROR_IO_PENDING){
                const HANDLE waits[]={event.value,stop_.value};
                if(WaitForMultipleObjects(2,waits,FALSE,3000)!=WAIT_OBJECT_0){CancelIoEx(handle,&connection);if(WaitForSingleObject(event.value,1000)!=WAIT_OBJECT_0)TerminateProcess(GetCurrentProcess(),70);return;}
                DWORD ignored=0;connected=GetOverlappedResult(handle,&connection,&ignored,FALSE);
            }else if(error==ERROR_PIPE_CONNECTED)connected=TRUE;
            if(!connected)return;
            std::array<BYTE,48> request{};if(!Io(handle,stop_.value,false,request.data(),48,500))return;++received;
            if(mode!=Mode::Silent){
                auto response=Hex(preview?PreviewVectors[1].hex:ThumbnailVectors[1].hex);Put32(response.data()+12,U32(request.data()+12));
                if(mode==Mode::Invalid)response[4]=2;if(mode==Mode::Partial)response.resize(24);
                if(!Io(handle,stop_.value,true,response.data(),static_cast<DWORD>(response.size()),500))return;
            }
            BYTE extra=0;Io(handle,stop_.value,false,&extra,1,25000); // Bounded client-close drain, no fake ACK.
        });
        const auto until=GetTickCount64()+1500;while(listening<instances&&GetTickCount64()<until)Sleep(1);Check(listening==instances,"thumbnail server listening");
    }
    ~Server(){SetEvent(stop_.value);for(const auto& pipe:pipes_)CancelIoEx(pipe->value,nullptr);for(auto& worker:workers_)worker.join();}
    Server(const Server&)=delete;Server& operator=(const Server&)=delete;
};
void Reclaimed(){const auto until=GetTickCount64()+2000;while(thumbnail::PendingOperations()&&GetTickCount64()<until)Sleep(1);Check(!thumbnail::PendingOperations(),"canceled operations actually reclaimed");}
void Pipes(){
    const auto location=Location();Check(thumbnail::Query({},nullptr).status==thumbnail::Status::InvalidResponse,"root is not an image request");
    {Server server(Mode::Ready);auto image=thumbnail::Query(location,nullptr);Check(image.status==thumbnail::Status::Ready&&image.image,"real pipe PBGRA frame");}Reclaimed();
    {Server server(Mode::Ready,1,true);auto image=preview::Query(location,nullptr);Check(image.status==thumbnail::Status::Ready&&image.image,"preview endpoint frame uses shared transfer");}Reclaimed();
    {Server server(Mode::Invalid);Check(thumbnail::Query(location,nullptr).status==thumbnail::Status::InvalidResponse,"bad response header");}Reclaimed();
    {Server server(Mode::Partial);local_pipe::Handle cancel(CreateEventW(nullptr,TRUE,FALSE,nullptr));
        std::thread stop([&]{Sleep(40);SetEvent(cancel.value);});const auto result=thumbnail::Query(location,cancel.value);stop.join();Check(result.status==thumbnail::Status::Unavailable&&!result.image,"partial frame canceled without stale image");}Reclaimed();
    {Server server(Mode::Silent,2);local_pipe::Handle cancel(CreateEventW(nullptr,TRUE,FALSE,nullptr));thumbnail::Result first,second;
        std::thread a([&]{first=thumbnail::Query(location,cancel.value);}),b([&]{second=thumbnail::Query(location,cancel.value);});
        const auto until=GetTickCount64()+1000;while(server.received<2&&GetTickCount64()<until)Sleep(1);
        const bool atCapacity=server.received==2&&thumbnail::PendingOperations()==2;
        const auto third=preview::Query(location,cancel.value);SetEvent(cancel.value);a.join();b.join();
        Check(atCapacity&&third.status==thumbnail::Status::Busy,"third image cannot exceed two retained I/O operations");
        Check(!first.image&&!second.image,"cancellation has no image");}Reclaimed();
    {Server server(Mode::Silent);const auto start=GetTickCount64();auto result=thumbnail::Query(location,nullptr);const auto elapsed=GetTickCount64()-start;
        Check(result.status==thumbnail::Status::Unavailable&&elapsed>=19000&&elapsed<21000,"one twenty-second total image budget");printf("thumbnail_deadline_ms=%llu\n",elapsed);}Reclaimed();
    puts("thumbnail_pipe=passed; cancel_reclaimed=passed; image_slots=2; registry=0; GUI=0; network=0");
}
}
int main(){
    try{Vectors();PreviewVectorsAndLimits();Pipes();return 0;}catch(const std::exception& error){fprintf(stderr,"GalleryThumbnailTests: %s\n",error.what());return 1;}
}
