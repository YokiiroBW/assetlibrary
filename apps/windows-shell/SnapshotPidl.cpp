#include "SnapshotPidl.h"
#include "ProductIdentity.h"
#include <cstring>
#include <limits>
#include <string_view>

namespace snapshot {
namespace {
constexpr ULONG PidlMagic=0x31504941;
constexpr size_t FixedBytes=48;
USHORT Read16(const BYTE* p){USHORT v;std::memcpy(&v,p,2);return v;}
ULONG Read32(const BYTE* p){ULONG v;std::memcpy(&v,p,4);return v;}
void Write16(BYTE* p,USHORT v){std::memcpy(p,&v,2);}
void Write32(BYTE* p,ULONG v){std::memcpy(p,&v,4);}
bool ValidEntry(const Entry& entry){
    if(!ValidName(entry.name))return false;
    if(entry.kind==Kind::StatusRow)return Zero(entry.node)&&entry.status!=Status::Ready&&entry.status<=Status::Busy;
    return entry.kind>=Kind::Library&&entry.kind<=Kind::NextPage&&!Zero(entry.epoch)&&!Zero(entry.node)&&entry.status==Status::Ready;
}
}
PITEMID_CHILD MakePidl(const Entry& entry){
    if(!ValidEntry(entry))return nullptr;
    const size_t size=FixedBytes+entry.name.size()*2;
    auto bytes=static_cast<BYTE*>(CoTaskMemAlloc(size+2));if(!bytes)return nullptr;
    std::memset(bytes,0,size+2);Write16(bytes,static_cast<USHORT>(size));Write32(bytes+2,PidlMagic);Write16(bytes+6,1);Write16(bytes+8,static_cast<USHORT>(entry.kind));
    std::memcpy(bytes+10,&entry.epoch,16);std::memcpy(bytes+26,&entry.node,16);Write16(bytes+42,static_cast<USHORT>(entry.name.size()));Write32(bytes+44,static_cast<ULONG>(entry.status));
    std::memcpy(bytes+FixedBytes,entry.name.data(),entry.name.size()*2);return reinterpret_cast<PITEMID_CHILD>(bytes);
}
bool IsOurPidl(PCUIDLIST_RELATIVE pidl) noexcept {
    if(!pidl||pidl->mkid.cb<6)return false;
    return Read32(reinterpret_cast<const BYTE*>(pidl)+2)==PidlMagic;
}
bool ReadPidl(PCUIDLIST_RELATIVE pidl,Entry& entry){
    if(!IsOurPidl(pidl))return false;
    const auto bytes=reinterpret_cast<const BYTE*>(pidl);const size_t size=Read16(bytes);
    if(size<FixedBytes||size>FixedBytes+510||Read16(bytes+6)!=1)return false;
    const size_t length=Read16(bytes+42);if(!length||length>255||size!=FixedBytes+length*2)return false;
    Entry value;value.kind=static_cast<Kind>(Read16(bytes+8));value.status=static_cast<Status>(Read32(bytes+44));
    std::memcpy(&value.epoch,bytes+10,16);std::memcpy(&value.node,bytes+26,16);value.name.resize(length);std::memcpy(value.name.data(),bytes+FixedBytes,length*2);
    if(!ValidEntry(value))return false;entry=std::move(value);return true;
}
bool BoundedList(PCUIDLIST_RELATIVE pidl,UINT& bytes,UINT& count) noexcept {
    bytes=0;count=0;if(!pidl)return false;
    auto current=reinterpret_cast<const BYTE*>(pidl);
    for(UINT i=0;i<=64;++i){const USHORT size=Read16(current);if(!size){if(bytes>MaxPayload-2)return false;bytes+=2;return true;}
        if(size<2||bytes>MaxPayload-size||i==64)return false;
        bytes+=size;count++;current+=size;
    }
    return false;
}
std::wstring ParsingName(const Entry& entry){
    if(entry.kind==Kind::StatusRow)return {};
    auto pidl=MakePidl(entry);if(!pidl)throw std::bad_alloc();
    std::wstring name;
    try {
        name=L"snapshot-pidl-v1:";const auto bytes=reinterpret_cast<const BYTE*>(pidl);
        constexpr wchar_t hex[]=L"0123456789abcdef";
        for(size_t i=0;i<pidl->mkid.cb;++i){name+=hex[bytes[i]>>4];name+=hex[bytes[i]&15];}
    }catch(...){CoTaskMemFree(pidl);throw;}
    CoTaskMemFree(pidl);return name;
}
bool ParseName(const wchar_t* name,PIDLIST_RELATIVE* result,ULONG* eaten,Kind& lastKind){
    *result=nullptr;if(eaten)*eaten=0;if(!name)return false;
    constexpr size_t MaxChars=MaxPayload*2+64*18+64;
    const size_t length=wcsnlen_s(name,MaxChars+1);if(!length||length>MaxChars)return false;
    std::wstring_view remaining(name,length);
    constexpr std::wstring_view root=product::ParsingRoot;
    if(remaining.size()>root.size()&&remaining.substr(0,root.size())==root&&remaining[root.size()]==L'\\')
        remaining.remove_prefix(root.size()+1);
    constexpr std::wstring_view prefix=L"snapshot-pidl-v1:";
    std::vector<BYTE> bytes;UINT count=0;
    while(!remaining.empty()){
        if(++count>64||remaining.substr(0,prefix.size())!=prefix)return false;
        remaining.remove_prefix(prefix.size());auto end=remaining.find(L'\\');
        const auto segment=remaining.substr(0,end);if(segment.size()<FixedBytes*2||segment.size()>(FixedBytes+510)*2||segment.size()%2)return false;
        const auto at=bytes.size(),size=segment.size()/2;if(at+size+2>MaxPayload)return false;
        bytes.resize(at+size+2,0);
        auto digit=[](wchar_t c)->int {return c>=L'0'&&c<=L'9'?c-L'0':c>=L'a'&&c<=L'f'?c-L'a'+10:c>=L'A'&&c<=L'F'?c-L'A'+10:-1;};
        for(size_t i=0;i<size;++i){const auto hi=digit(segment[i*2]),lo=digit(segment[i*2+1]);if(hi<0||lo<0)return false;bytes[at+i]=static_cast<BYTE>(hi*16+lo);}
        const auto item=reinterpret_cast<PCUIDLIST_RELATIVE>(bytes.data()+at);Entry entry;
        if(item->mkid.cb!=size||!ReadPidl(item,entry)||entry.kind==Kind::StatusRow)return false;
        lastKind=entry.kind;
        if(end==std::wstring_view::npos)break;
        if(!Navigable(entry.kind))return false;
        bytes.resize(at+size);remaining.remove_prefix(end+1);if(remaining.empty())return false;
    }
    if(!count)return false;
    auto pidl=static_cast<PIDLIST_RELATIVE>(CoTaskMemAlloc(bytes.size()));if(!pidl)throw std::bad_alloc();
    std::memcpy(pidl,bytes.data(),bytes.size());*result=pidl;if(eaten)*eaten=static_cast<ULONG>(length);return true;
}
}
