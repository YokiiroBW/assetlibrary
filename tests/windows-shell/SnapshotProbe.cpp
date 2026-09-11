#include "SnapshotTestSupport.h"
#include <deque>

namespace {
void Print(const char* stage,const snapshot::Entry& entry){
    printf("{\"stage\":\"%s\",\"kind\":%u,\"status\":%lu,\"name\":\"",stage,static_cast<unsigned>(entry.kind),static_cast<ULONG>(entry.status));
    for(wchar_t c:entry.name)printf("\\u%04x",static_cast<unsigned>(c));
    puts("\"}");
}
struct Visit { proof::Com<IShellFolder2> folder;const char* stage; };
int Run(int argc,wchar_t** argv){
    const wchar_t* dll=nullptr;DWORD budget=8000;bool once=false,require=false;
    for(int i=1;i<argc;++i){
        if(wcscmp(argv[i],L"--dll")==0&&i+1<argc)dll=argv[++i];
        else if(wcscmp(argv[i],L"--budget-ms")==0&&i+1<argc){wchar_t* end=nullptr;auto parsed=wcstoul(argv[++i],&end,10);proof::Check(end&&!*end&&parsed>=150&&parsed<=30000,"budget must be 150..30000");budget=parsed;}
        else if(wcscmp(argv[i],L"--once")==0)once=true;
        else if(wcscmp(argv[i],L"--require-navigation")==0)require=true;
        else throw std::runtime_error("usage: ExplorerSnapshotProbe --dll <absolute DLL> [--budget-ms 8000] [--once] [--require-navigation]");
    }
    proof::Check(dll!=nullptr,"--dll required");proof::Library library(dll);
    const auto deadline=GetTickCount64()+budget;bool seenLibrary=false,seenDirectory=false,seenNext=false;size_t visited=0;
    std::deque<Visit> pending;pending.push_back({library.Root(),"Root"});
    while(!pending.empty()&&visited<16){
        auto visit=std::move(pending.front());pending.pop_front();std::vector<proof::Item> items;
        snapshot::Status last=snapshot::Status::Ready;
        for(;;){
            proof::Check(GetTickCount64()<deadline,"probe total retry budget expired");
            items=proof::Enumerate(visit.folder.value);
            if(items.empty()||proof::Read(items[0]).kind!=snapshot::Kind::StatusRow)break;
            auto entry=proof::Read(items[0]);if(entry.status!=last){Print(visit.stage,entry);last=entry.status;}
            if(once||entry.status==snapshot::Status::Expired||entry.status==snapshot::Status::AccessDenied||entry.status==snapshot::Status::InvalidResponse)return 2;
            if(GetTickCount64()+250>=deadline)return 2;Sleep(250);
        }
        printf("{\"stage\":\"%s\",\"status\":0,\"count\":%zu}\n",visit.stage,items.size());++visited;
        bool queuedDirectory=false,queuedNext=false,queuedLibrary=false;
        for(const auto& item:items){auto entry=proof::Read(item);Print(visit.stage,entry);
            if(once)continue;
            const char* stage=nullptr;
            if(entry.kind==snapshot::Kind::Library&&!queuedLibrary&&!seenLibrary){stage="Library";queuedLibrary=true;seenLibrary=true;}
            else if(entry.kind==snapshot::Kind::Directory&&!queuedDirectory&&!seenDirectory){stage="Directory";queuedDirectory=true;seenDirectory=true;}
            else if(entry.kind==snapshot::Kind::NextPage&&!queuedNext&&!seenNext){stage="NextPage";queuedNext=true;seenNext=true;}
            if(stage)pending.push_back({proof::Bind(visit.folder.value,item),stage});
        }
    }
    proof::Check(pending.empty(),"probe page bound");
    if(require)proof::Check(seenLibrary&&seenDirectory&&seenNext,"fixture must expose Library, Directory and NextPage");
    puts("SnapshotProbe=passed; Registration=false; GUI=false");return 0;
}
}
int wmain(int argc,wchar_t** argv){
    setvbuf(stdout,nullptr,_IONBF,0);if(FAILED(CoInitializeEx(nullptr,COINIT_APARTMENTTHREADED)))return 1;
    int result=1;try{result=Run(argc,argv);}catch(const std::exception& error){fprintf(stderr,"SnapshotProbe: %s\n",error.what());}
    CoUninitialize();return result;
}
