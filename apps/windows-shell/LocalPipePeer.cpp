#include "LocalPipePeer.h"
#include <sddl.h>

namespace local_pipe {
bool UserSid(HANDLE process,std::vector<BYTE>& storage){
    HANDLE raw=nullptr;if(!OpenProcessToken(process,TOKEN_QUERY,&raw))return false;Handle token(raw);
    DWORD needed=0;GetTokenInformation(token.value,TokenUser,nullptr,0,&needed);
    if(GetLastError()!=ERROR_INSUFFICIENT_BUFFER||needed<sizeof(TOKEN_USER)||needed>4096)return false;
    storage.resize(needed);
    if(!GetTokenInformation(token.value,TokenUser,storage.data(),needed,&needed))return false;
    auto sid=reinterpret_cast<TOKEN_USER*>(storage.data())->User.Sid;
    return IsValidSid(sid)!=FALSE;
}
bool SamePeer(HANDLE pipe,HANDLE& peer,ULONGLONG started){
    ULONG pid=0;if(!GetNamedPipeServerProcessId(pipe,&pid)||!pid)return false;
    Handle process(OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION|SYNCHRONIZE,FALSE,pid));if(!process.value)return false;
    FILETIME created{},ended{},kernel{},user{};
    if(!GetProcessTimes(process.value,&created,&ended,&kernel,&user))return false;
    const ULONGLONG birth=(static_cast<ULONGLONG>(created.dwHighDateTime)<<32)|created.dwLowDateTime;
    if(birth>started)return false;
    DWORD ownSession=0,peerSession=0;
    if(!ProcessIdToSessionId(GetCurrentProcessId(),&ownSession)||!ProcessIdToSessionId(pid,&peerSession)||ownSession!=peerSession)return false;
    std::vector<BYTE> ownSid,peerSid;
    if(!UserSid(GetCurrentProcess(),ownSid)||!UserSid(process.value,peerSid))return false;
    if(!EqualSid(reinterpret_cast<TOKEN_USER*>(ownSid.data())->User.Sid,reinterpret_cast<TOKEN_USER*>(peerSid.data())->User.Sid))return false;
    ULONG afterPid=0;
    if(!GetNamedPipeServerProcessId(pipe,&afterPid)||afterPid!=pid||WaitForSingleObject(process.value,0)!=WAIT_TIMEOUT)return false;
    peer=process.value;process.value=nullptr;return true;
}
std::wstring Name(const wchar_t* prefix){
    std::vector<BYTE> token;if(!UserSid(GetCurrentProcess(),token))return {};
    LPWSTR sid=nullptr;
    if(!ConvertSidToStringSidW(reinterpret_cast<TOKEN_USER*>(token.data())->User.Sid,&sid))return {};
    std::wstring name;
    try{name=prefix;name+=sid;}catch(...){LocalFree(sid);throw;}
    LocalFree(sid);DWORD session=0;if(!ProcessIdToSessionId(GetCurrentProcessId(),&session))return {};
    return name+L"."+std::to_wstring(session);
}
}
