#include "SnapshotTestSupport.h"
#include <sddl.h>
#include <atomic>
#include <functional>
#include <thread>
#include <algorithm>
#include <process.h>

using proof::Check;
namespace {
using Bytes=std::vector<BYTE>;
constexpr GUID epoch={0x00112233,0x4455,0x6677,{0x88,0x99,0xaa,0xbb,0xcc,0xdd,0xee,0xff}};
void Put(Bytes& b,size_t at,ULONG value,size_t width=4){for(size_t i=0;i<width;++i)b.at(at+i)=static_cast<BYTE>(value>>(i*8));}
ULONG Get(const BYTE* b){return b[0]|static_cast<ULONG>(b[1])<<8|static_cast<ULONG>(b[2])<<16|static_cast<ULONG>(b[3])<<24;}
Bytes Hex(const char* text){Bytes b;for(size_t i=0;text[i];i+=2){unsigned n=0;Check(sscanf_s(text+i,"%2x",&n)==1,"hex");b.push_back(static_cast<BYTE>(n));}return b;}
// Frozen literal frames from contracts/windows-shell/wire-vectors-v1.json;
// no production encoder is used to construct these expectations.
const auto rootVector=Hex("414c53310100010020000000070000000000000000000000000000000000000000000000000000000000000000000000");
const auto readyVector=Hex("414c53310100020032000000070000000000000033221100554477668899aabbccddeeff01000000ccddeeffaabb8899776655443322110001000300448d9965935e");
const auto loadingVector=Hex("414c53310100020018000000070000000100000033221100554477668899aabbccddeeff00000000");
struct Record { ULONG token;USHORT kind;std::wstring name; };
Bytes Frame(ULONG id,ULONG status=0,std::vector<Record> records={},GUID hostEpoch=epoch){
    Bytes b(40);Put(b,0,0x31534c41);Put(b,4,1,2);Put(b,6,2,2);Put(b,12,id);Put(b,16,status);
    std::memcpy(b.data()+20,&hostEpoch,16);Put(b,36,static_cast<ULONG>(records.size()));
    for(const auto& row:records){auto at=b.size();b.resize(at+20+row.name.size()*2);Put(b,at,row.token);Put(b,at+16,row.kind,2);Put(b,at+18,static_cast<ULONG>(row.name.size()),2);
        for(size_t i=0;i<row.name.size();++i)Put(b,at+20+i*2,row.name[i],2);}
    Put(b,8,static_cast<ULONG>(b.size()-16));return b;
}
void Invalid(Bytes b){snapshot::Page page;Check(!snapshot::Decode(b.data()+16,b.size()-16,page)&&page.status==snapshot::Status::InvalidResponse&&page.entries.empty(),"malformed payload rejected");}
void Wire(){
    const auto request=snapshot::Request({},7);Check(std::equal(request.begin(),request.end(),rootVector.begin(),rootVector.end()),"literal request");
    ULONG length=0;snapshot::Page page;
    Check(snapshot::ResponseHeader(readyVector.data(),7,length)&&length==50&&snapshot::Decode(readyVector.data()+16,length,page),"literal ready");
    GUID node={0xffeeddcc,0xbbaa,0x9988,{0x77,0x66,0x55,0x44,0x33,0x22,0x11,0x00}};
    Check(page.epoch==epoch&&page.entries.size()==1&&page.entries[0].node==node&&page.entries[0].name==L"资料库","GUID byte order/name");
    Check(snapshot::Decode(loadingVector.data()+16,24,page)&&page.status==snapshot::Status::Loading&&page.entries.empty(),"literal loading");
    for(size_t at:{size_t(0),size_t(4),size_t(6),size_t(12)}){auto b=readyVector;b[at]^=1;Check(!snapshot::ResponseHeader(b.data(),7,length),"bad header");}
    for(ULONG size:{0ul,23ul,65537ul,0xfffffffful}){auto b=readyVector;Put(b,8,size);Check(!snapshot::ResponseHeader(b.data(),7,length),"header allocation bound");}
    bool rejected=false;try{snapshot::Request({epoch,{}},7);}catch(const std::invalid_argument&){rejected=true;}Check(rejected,"half zero request");
    rejected=false;try{snapshot::Request({},0);}catch(const std::invalid_argument&){rejected=true;}Check(rejected,"zero request id");
    for(size_t size=16;size<readyVector.size();++size){auto b=readyVector;b.resize(size);Invalid(b);}
    auto b=readyVector;b.push_back(0);Invalid(b);
    b=readyVector;Put(b,16,7);Invalid(b);b=readyVector;Put(b,16,1);Invalid(b);
    b=readyVector;std::fill(b.begin()+20,b.begin()+36,BYTE{0});Invalid(b);
    b=readyVector;std::fill(b.begin()+40,b.begin()+56,BYTE{0});Invalid(b);
    for(USHORT kind:{USHORT(0),USHORT(6),USHORT(0x8000)}){b=readyVector;Put(b,56,kind,2);Invalid(b);}
    for(USHORT c:{USHORT(0),USHORT(0x1f),USHORT(0x7f),USHORT(0x9f),USHORT(0xd800),USHORT(0xdc00)}){b=readyVector;Put(b,60,c,2);Invalid(b);}
    for(USHORT count:{USHORT(0),USHORT(256),USHORT(65535)}){b=readyVector;Put(b,58,count,2);Invalid(b);}
    Invalid(Frame(7,0,{{1,1,L"a"},{1,2,L"b"}}));Invalid(Frame(7,0,{{1,5,L"next"},{2,5,L"next"}}));
    std::vector<Record> rows;for(ULONG i=1;i<=100;++i)rows.push_back({i,3,L"same"});rows.push_back({101,5,L"custom"});
    b=Frame(7,0,rows);Check(snapshot::Decode(b.data()+16,b.size()-16,page)&&page.entries.size()==101&&page.entries.back().name==L"下一页（导航）","page boundary");
    rows.back().kind=3;Invalid(Frame(7,0,rows));rows.push_back({102,3,L"x"});Invalid(Frame(7,0,rows));
    b=Frame(7);Check(snapshot::Decode(b.data()+16,24,page)&&page.status==snapshot::Status::Ready&&page.entries.empty(),"genuine empty");
    Check(snapshot::ValidName(std::wstring(255,L'x'))&&!snapshot::ValidName(std::wstring(256,L'x'))&&snapshot::ValidName(L"\xd83d\xde00"),"UTF16 length/scalar boundaries");
    puts("wire=passed; literal_vectors=3; malformed_and_boundaries=passed");
}
void Pidls(){
    snapshot::Entry entry{epoch,{123},snapshot::Kind::Directory,L"same"};proof::Item item(snapshot::MakePidl(entry));if(!item.value)throw std::runtime_error("make PIDL");
    auto read=proof::Read(item);Check(read.node==entry.node&&read.epoch==epoch&&read.name==entry.name,"PIDL roundtrip");
    UINT bytes=0,count=0;Check(snapshot::BoundedList(item.value,bytes,count)&&count==1&&bytes==58,"PIDL bounded length");
    auto p=reinterpret_cast<BYTE*>(item.value);p[6]=2;Check(!snapshot::ReadPidl(item.value,read),"PIDL version");p[6]=1;p[42]=0;Check(!snapshot::ReadPidl(item.value,read),"PIDL length");
    snapshot::Entry status{{},{},snapshot::Kind::StatusRow,snapshot::StatusText(snapshot::Status::Unavailable),snapshot::Status::Unavailable};proof::Item row(snapshot::MakePidl(status));
    Check(row.value&&!snapshot::Navigable(proof::Read(row).kind)&&snapshot::ParsingName(status).empty(),"status not identity/navigation");
    Check(!snapshot::Navigable(snapshot::Kind::Reparse)&&!snapshot::Navigable(snapshot::Kind::File),"non-navigable kinds");
    puts("pidl=passed");
}
struct Handle { HANDLE value=INVALID_HANDLE_VALUE;explicit Handle(HANDLE h=INVALID_HANDLE_VALUE):value(h){}~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;};
// All mock I/O belongs to a test thread. Stop cancels overlapped operations;
// an unconfirmed cancellation fails this process instead of releasing live storage.
bool Transfer(HANDLE pipe,HANDLE stop,bool write,BYTE* bytes,DWORD length){
    Handle event(CreateEventW(nullptr,TRUE,FALSE,nullptr));OVERLAPPED ov{};ov.hEvent=event.value;DWORD at=0;
    while(at<length){ResetEvent(event.value);DWORD done=0;BOOL ok=write?WriteFile(pipe,bytes+at,length-at,&done,&ov):ReadFile(pipe,bytes+at,length-at,&done,&ov);
        if(!ok){if(GetLastError()!=ERROR_IO_PENDING)return false;HANDLE waits[]={stop,event.value};
            if(WaitForMultipleObjects(2,waits,FALSE,2000)!=WAIT_OBJECT_0+1){CancelIoEx(pipe,&ov);if(WaitForSingleObject(event.value,2000)!=WAIT_OBJECT_0)std::terminate();return false;}
            if(!GetOverlappedResult(pipe,&ov,&done,FALSE))return false;}
        if(!done)return false;at+=done;}
    return true;
}
using Reply=std::function<void(HANDLE,HANDLE,const Bytes&)>;
class Server {
    Handle stop_{CreateEventW(nullptr,TRUE,FALSE,nullptr)};std::vector<HANDLE> pipes_;std::vector<std::thread> threads_;
public:
    std::atomic_uint requests{0},listening{0};std::atomic_bool identification{true};
    explicit Server(Reply reply,unsigned instances=1){
        const auto name=snapshot::PipeName();Check(!name.empty(),"endpoint");
        for(unsigned i=0;i<instances;++i){
            HANDLE pipe=CreateNamedPipeW(name.c_str(),PIPE_ACCESS_DUPLEX|FILE_FLAG_OVERLAPPED|(i==0?FILE_FLAG_FIRST_PIPE_INSTANCE:0),PIPE_TYPE_BYTE|PIPE_READMODE_BYTE|PIPE_WAIT|PIPE_REJECT_REMOTE_CLIENTS,4,65536,65536,0,nullptr);
            if(pipe==INVALID_HANDLE_VALUE){Stop();throw std::runtime_error("mock pipe occupied; no existing Host was touched");}pipes_.push_back(pipe);
        }
        for(HANDLE pipe:pipes_)threads_.emplace_back([this,pipe,reply]{
            while(WaitForSingleObject(stop_.value,0)==WAIT_TIMEOUT){
                Handle event(CreateEventW(nullptr,TRUE,FALSE,nullptr));OVERLAPPED ov{};ov.hEvent=event.value;
                ++listening;BOOL connected=ConnectNamedPipe(pipe,&ov);DWORD error=connected?ERROR_SUCCESS:GetLastError();
                if(!connected&&error==ERROR_IO_PENDING){HANDLE waits[]={stop_.value,event.value};
                    if(WaitForMultipleObjects(2,waits,FALSE,5000)!=WAIT_OBJECT_0+1){CancelIoEx(pipe,&ov);if(WaitForSingleObject(event.value,2000)!=WAIT_OBJECT_0)std::terminate();break;}
                }else if(!connected&&error!=ERROR_PIPE_CONNECTED)break;
                --listening;Bytes request(48);
                if(Transfer(pipe,stop_.value,false,request.data(),48)){
                    ++requests;bool validLevel=false;
                    if(ImpersonateNamedPipeClient(pipe)){
                        HANDLE raw=nullptr;if(OpenThreadToken(GetCurrentThread(),TOKEN_QUERY,TRUE,&raw)){Handle token(raw);SECURITY_IMPERSONATION_LEVEL level{};DWORD size=0;
                            validLevel=GetTokenInformation(raw,TokenImpersonationLevel,&level,sizeof(level),&size)&&level==SecurityIdentification;}
                        if(!RevertToSelf())std::terminate();
                    }
                    if(!validLevel)identification=false;
                    reply(pipe,stop_.value,request);
                    BYTE ignored=0;Transfer(pipe,stop_.value,false,&ignored,1); // Wait for client close before discarding buffers.
                }
                DisconnectNamedPipe(pipe);
            }
        });
        Ready();
    }
    void Ready(){const auto end=GetTickCount64()+1000;while(!listening&&GetTickCount64()<end)Sleep(1);Check(listening!=0,"mock listening before request");}
    void Stop(){SetEvent(stop_.value);for(auto& thread:threads_)if(thread.joinable())thread.join();threads_.clear();for(HANDLE pipe:pipes_)CloseHandle(pipe);pipes_.clear();}
    ~Server(){Stop();}
    Server(const Server&)=delete;Server& operator=(const Server&)=delete;
};
void Send(HANDLE pipe,HANDLE stop,Bytes bytes){Transfer(pipe,stop,true,bytes.data(),static_cast<DWORD>(bytes.size()));}
void Reaped(){const auto end=GetTickCount64()+2000;while(snapshot::PendingOperations()&&GetTickCount64()<end)Sleep(1);Check(!snapshot::PendingOperations(),"all cancelled operations reaped");}
void Pipes(){
    Check(snapshot::Query({}).status==snapshot::Status::Unavailable,"absent Host is unavailable");
    {Server server([](HANDLE p,HANDLE s,const Bytes& r){Send(p,s,Frame(Get(r.data()+12),0,{{1,1,L"library"}}));WaitForSingleObject(s,300);});
        auto start=GetTickCount64();auto page=snapshot::Query({});auto elapsed=GetTickCount64()-start;
        printf("pipe status=%lu elapsed=%llu requests=%u\n",static_cast<ULONG>(page.status),elapsed,server.requests.load());Check(page.status==snapshot::Status::Ready&&page.entries.size()==1&&elapsed<150,"valid frame without EOF wait");Check(server.identification,"SQOS identification only");}
    {Server server([](HANDLE p,HANDLE s,const Bytes& r){auto b=Frame(Get(r.data()+12));b[4]=2;Send(p,s,b);});Check(snapshot::Query({}).status==snapshot::Status::InvalidResponse,"invalid header status");}
    {Server server([](HANDLE p,HANDLE s,const Bytes& r){auto b=Frame(Get(r.data()+12));WaitForSingleObject(s,85);Transfer(p,s,true,b.data(),16);WaitForSingleObject(s,85);Transfer(p,s,true,b.data()+16,24);});
        auto start=GetTickCount64();Check(snapshot::Query({}).status==snapshot::Status::Unavailable,"cumulative deadline");auto elapsed=GetTickCount64()-start;Check(elapsed>=120&&elapsed<230,"single 150ms budget with scheduler tolerance");printf("deadline_elapsed_ms=%llu\n",elapsed);}
    Reaped();
    {Server server([](HANDLE p,HANDLE s,const Bytes& r){auto b=Frame(Get(r.data()+12));b.resize(20);Send(p,s,b);});Check(snapshot::Query({}).status==snapshot::Status::Unavailable,"partial body");}
    {Server server([](HANDLE,HANDLE s,const Bytes&){WaitForSingleObject(s,1000);});Handle cancel(CreateEventW(nullptr,TRUE,FALSE,nullptr));
        std::thread trigger([&]{Sleep(25);SetEvent(cancel.value);});auto start=GetTickCount64();auto page=snapshot::Query({},cancel.value);auto elapsed=GetTickCount64()-start;trigger.join();
        Check(page.status==snapshot::Status::Unavailable&&elapsed<130,"cancel prompt return");}
    Reaped();
    {Server server([](HANDLE,HANDLE s,const Bytes&){WaitForSingleObject(s,1000);},4);
        std::vector<std::thread> callers;for(int i=0;i<4;++i)callers.emplace_back([]{Check(snapshot::Query({}).status==snapshot::Status::Unavailable,"held caller timeout");});
        const auto end=GetTickCount64()+100;while(server.requests<4&&GetTickCount64()<end)Sleep(1);
        Check(server.requests==4&&snapshot::PendingOperations()==4,"four in-flight operations");Check(snapshot::Query({}).status==snapshot::Status::Busy,"fifth operation busy");
        for(auto& caller:callers)caller.join();}
    Reaped();puts("pipe=passed; peer_same_user_session=passed; cancel_and_capacity=passed");
}
void ComTests(const wchar_t* dll,const wchar_t* probe){
    std::atomic<ULONG> status{0};std::atomic_bool restart{false};
    Server server([&](HANDLE p,HANDLE s,const Bytes& r){auto hostEpoch=epoch;if(restart)++hostEpoch.Data1;
        const auto node=Get(r.data()+32);const auto requestEpoch=Get(r.data()+16);
        if(requestEpoch&&requestEpoch!=hostEpoch.Data1){Send(p,s,Frame(Get(r.data()+12),4,{},hostEpoch));return;}
        std::vector<Record> rows;
        if(node==0)rows={{1,1,L"library"}};
        if(node==1)rows={{2,2,L"same"},{3,3,L"same"},{4,4,L"link"},{5,5,L"not a folder"}};
        if(node==5)rows={{6,3,L"page two"}};
        Send(p,s,Frame(Get(r.data()+12),status,status?std::vector<Record>{}:rows,hostEpoch));
    });
    auto enumerate=[&](IShellFolder2* folder){server.Ready();return proof::Enumerate(folder);};
    proof::Library library(dll);
    {auto root=library.Root();auto items=enumerate(root.value);printf("COM root count=%zu status=%lu requests=%u\n",items.size(),items.empty()?99:static_cast<ULONG>(proof::Read(items[0]).status),server.requests.load());Check(items.size()==1&&proof::Read(items[0]).kind==snapshot::Kind::Library,"COM root library");
        auto child=proof::Bind(root.value,items[0]);auto contents=enumerate(child.value);Check(contents.size()==4,"COM library page");auto before=server.requests.load();
        for(const auto& item:contents){auto entry=proof::Read(item);PCUITEMID_CHILD raw=item.value;SFGAOF attributes=0xffffffff;
            Check(SUCCEEDED(child.value->GetAttributesOf(1,&raw,&attributes))&&(attributes&SFGAO_READONLY),"read only attributes");
            Check(!(attributes&(SFGAO_CANRENAME|SFGAO_CANDELETE|SFGAO_CANCOPY|SFGAO_CANMOVE|SFGAO_DROPTARGET)),"no write/transfer abilities");
            Check(((attributes&SFGAO_FOLDER)!=0)==snapshot::Navigable(entry.kind),"folder kind attributes");
            STRRET text{};Check(SUCCEEDED(child.value->GetDisplayNameOf(raw,SHGDN_NORMAL,&text))&&text.uType==STRRET_WSTR&&entry.name==text.pOleStr,"display text from PIDL");CoTaskMemFree(text.pOleStr);
            Check(SUCCEEDED(child.value->GetDisplayNameOf(raw,static_cast<SHGDNF>(SHGDN_FORPARSING|SHGDN_INFOLDER),&text)),"parsing name");
            PIDLIST_RELATIVE parsed=nullptr;Check(SUCCEEDED(child.value->ParseDisplayName(nullptr,nullptr,text.pOleStr,nullptr,&parsed,nullptr)),"opaque name parse");CoTaskMemFree(text.pOleStr);proof::Item parsedItem(parsed);
            Check(child.value->CompareIDs(SHCIDS_CANONICALONLY,raw,parsed)==S_OK,"canonical identity");
            Check(child.value->SetNameOf(nullptr,raw,L"rename",SHGDN_NORMAL,nullptr)==E_ACCESSDENIED,"rename denied");
            if(!snapshot::Navigable(entry.kind)){proof::Com<IShellFolder2> denied;Check(child.value->BindToObject(raw,nullptr,IID_PPV_ARGS(&denied.value))==E_ACCESSDENIED&&!denied.value,"file/reparse bind denied");}
        }
        Check(child.value->CompareIDs(0,contents[0].value,contents[1].value)!=S_OK,"same name distinct identity");
        PIDLIST_RELATIVE parsed=nullptr;wchar_t displayName[]=L"same";Check(FAILED(child.value->ParseDisplayName(nullptr,nullptr,displayName,nullptr,&parsed,nullptr))&&!parsed,"name not identity");
        auto freshRoot=library.Root();
        STRRET full{};Check(SUCCEEDED(child.value->GetDisplayNameOf(contents[0].value,SHGDN_FORPARSING,&full)),"nested full parsing name");
        PIDLIST_RELATIVE nested=nullptr;Check(SUCCEEDED(freshRoot.value->ParseDisplayName(nullptr,nullptr,full.pOleStr,nullptr,&nested,nullptr)),"fresh instance nested full roundtrip");CoTaskMemFree(full.pOleStr);proof::Item nestedItem(nested);
        proof::Item expected(ILCombine(items[0].value,contents[0].value));Check(freshRoot.value->CompareIDs(SHCIDS_CANONICALONLY,nested,expected.value)==S_OK,"nested identity preserved");
        auto relative=snapshot::ParsingName(proof::Read(contents[0]));PIDLIST_RELATIVE single=nullptr;
        Check(SUCCEEDED(freshRoot.value->ParseDisplayName(nullptr,nullptr,relative.data(),nullptr,&single,nullptr)),"fresh instance single roundtrip");CoTaskMemFree(single);
        auto broken=relative;broken.back()=L'g';Check(FAILED(freshRoot.value->ParseDisplayName(nullptr,nullptr,broken.data(),nullptr,&single,nullptr))&&!single,"nonhex parsing name rejected");
        broken=relative+L"\\";Check(FAILED(freshRoot.value->ParseDisplayName(nullptr,nullptr,broken.data(),nullptr,&single,nullptr)),"empty final segment rejected");
        broken=snapshot::ParsingName(proof::Read(contents[1]))+L"\\"+relative;Check(FAILED(freshRoot.value->ParseDisplayName(nullptr,nullptr,broken.data(),nullptr,&single,nullptr)),"file cannot be intermediate parent");
        std::wstring deep=relative;for(int i=1;i<65;++i)deep+=L"\\"+relative;
        Check(FAILED(freshRoot.value->ParseDisplayName(nullptr,nullptr,deep.data(),nullptr,&single,nullptr)),"canonical depth bound");
        snapshot::Entry left{epoch,{30},snapshot::Kind::Library,L"a"},right{epoch,{31},snapshot::Kind::Directory,L"z"};
        proof::Item leftItem(snapshot::MakePidl(left)),rightItem(snapshot::MakePidl(right));
        auto sign=[](HRESULT hr){return static_cast<SHORT>(HRESULT_CODE(hr));};
        auto expectedType=wcscmp(snapshot::TypeText(left.kind),snapshot::TypeText(right.kind));
        Check(sign(child.value->CompareIDs(0,leftItem.value,rightItem.value))<0&&expectedType>0&&sign(child.value->CompareIDs(1,leftItem.value,rightItem.value))>0,"type column overrides inverse name order");
        Check(child.value->CompareIDs(2,leftItem.value,rightItem.value)==E_INVALIDARG,"unknown sort column");
        Check(server.requests==before,"no per-item or canonical parsing IPC");
        auto directory=proof::Bind(child.value,contents[0]);Check(enumerate(directory.value).empty(),"genuine empty directory");
        auto next=proof::Bind(child.value,contents[3]);Check(proof::Read(enumerate(next.value)[0]).name==L"page two","next page navigation");
        for(ULONG code=1;code<=6;++code){status=code;auto rows=enumerate(root.value);Check(rows.size()==1&&proof::Read(rows[0]).kind==snapshot::Kind::StatusRow&&static_cast<ULONG>(proof::Read(rows[0]).status)==code,"fixed status row");
            proof::Com<IShellFolder2> denied;Check(root.value->BindToObject(rows[0].value,nullptr,IID_PPV_ARGS(&denied.value))==E_ACCESSDENIED,"status bind denied");}
        status=0;restart=true;auto expired=enumerate(child.value);Check(proof::Read(expired[0]).status==snapshot::Status::Expired,"old identity expired");
        auto fresh=enumerate(root.value);Check(proof::Read(fresh[0]).epoch!=epoch,"root obtains fresh epoch");
    }
    const auto end=GetTickCount64()+2000;while(library.canUnload()!=S_OK&&GetTickCount64()<end)Sleep(1);Check(library.canUnload()==S_OK,"DLL COM/operation lifetime");
    restart=false;status=0;server.Ready();
    Check(_wspawnl(_P_WAIT,probe,probe,L"--dll",dll,L"--require-navigation",L"--budget-ms",L"4000",static_cast<wchar_t*>(nullptr))==0,"standalone LoadLibrary probe traversal");
    puts("COM=passed; root_library_directory_next=passed; stale_identity=passed; per_item_IPC=0");
}
}
int wmain(int argc,wchar_t** argv){
    setvbuf(stdout,nullptr,_IONBF,0);if(argc!=3)return 2;if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    int result=0;try{Wire();Pidls();Pipes();ComTests(argv[1],argv[2]);}catch(const std::exception& error){fprintf(stderr,"SnapshotTests: %s\n",error.what());result=1;}
    CoUninitialize();return result;
}
