#include "Snapshot.h"
#include <cstring>
#include <set>
#include <stdexcept>

namespace snapshot {
namespace {
USHORT U16(const BYTE* p) noexcept { return static_cast<USHORT>(p[0] | (static_cast<USHORT>(p[1]) << 8)); }
ULONG U32(const BYTE* p) noexcept { return static_cast<ULONG>(p[0]) | (static_cast<ULONG>(p[1]) << 8) | (static_cast<ULONG>(p[2]) << 16) | (static_cast<ULONG>(p[3]) << 24); }
void Put16(BYTE* p, USHORT v) noexcept { p[0]=static_cast<BYTE>(v);p[1]=static_cast<BYTE>(v>>8); }
void Put32(BYTE* p, ULONG v) noexcept { for(unsigned i=0;i<4;++i)p[i]=static_cast<BYTE>(v>>(8*i)); }
}
bool Zero(REFGUID value) noexcept { return value == GUID{}; }
bool Navigable(Kind kind) noexcept { return kind==Kind::Library || kind==Kind::Directory || kind==Kind::NextPage; }
bool ValidName(const std::wstring& value) noexcept {
    if(value.empty()||value.size()>255)return false;
    for(size_t i=0;i<value.size();++i){
        const unsigned c=value[i];
        if(c<0x20||(c>=0x7f&&c<=0x9f))return false;
        if(c>=0xd800&&c<=0xdbff){if(++i==value.size()||value[i]<0xdc00||value[i]>0xdfff)return false;}
        else if(c>=0xdc00&&c<=0xdfff)return false;
    }
    return true;
}
std::array<BYTE,48> Request(const Location& location, ULONG requestId) {
    if(!requestId||Zero(location.epoch)!=Zero(location.node))throw std::invalid_argument("invalid request identity");
    std::array<BYTE,48> result{};
    Put32(result.data(),Magic);Put16(result.data()+4,1);Put16(result.data()+6,1);Put32(result.data()+8,32);Put32(result.data()+12,requestId);
    std::memcpy(result.data()+16,&location.epoch,16);std::memcpy(result.data()+32,&location.node,16);return result;
}
bool ResponseHeader(const BYTE* header, ULONG requestId, ULONG& payloadBytes) noexcept {
    payloadBytes=0;if(!header||!requestId||U32(header)!=Magic||U16(header+4)!=1||U16(header+6)!=2||U32(header+12)!=requestId)return false;
    const ULONG size=U32(header+8);if(size<24||size>MaxPayload)return false;payloadBytes=size;return true;
}
bool Decode(const BYTE* payload, size_t bytes, Page& page) {
    page=Page{};page.status=Status::InvalidResponse;
    if(!payload||bytes<24||bytes>MaxPayload)return false;
    Page parsed;const ULONG status=U32(payload),count=U32(payload+20);
    if(status>static_cast<ULONG>(Status::Busy)||count>MaxItems||(status!=0&&count!=0))return false;
    parsed.status=static_cast<Status>(status);std::memcpy(&parsed.epoch,payload+4,16);if(Zero(parsed.epoch))return false;
    size_t at=24;unsigned normalCount=0,nextCount=0;std::set<std::array<BYTE,16>> seen;
    parsed.entries.reserve(count);
    for(ULONG i=0;i<count;++i){
        if(bytes-at<20)return false;
        Entry entry;entry.epoch=parsed.epoch;std::memcpy(&entry.node,payload+at,16);
        std::array<BYTE,16> token{};std::memcpy(token.data(),payload+at,16);
        const USHORT kind=U16(payload+at+16),length=U16(payload+at+18);at+=20;
        if(Zero(entry.node)||!seen.insert(token).second||kind<1||kind>5||!length||length>255||bytes-at<static_cast<size_t>(length)*2)return false;
        entry.kind=static_cast<Kind>(kind);entry.name.resize(length);
        for(size_t n=0;n<length;++n)entry.name[n]=static_cast<wchar_t>(U16(payload+at+n*2));
        at+=static_cast<size_t>(length)*2;if(!ValidName(entry.name))return false;
        if(entry.kind==Kind::NextPage){if(++nextCount>1)return false;entry.name=L"下一页（导航）";}
        else if(++normalCount>100)return false;
        parsed.entries.push_back(std::move(entry));
    }
    if(at!=bytes)return false;page=std::move(parsed);return true;
}
const wchar_t* StatusText(Status status) noexcept {
    switch(status){
    case Status::Loading:return L"正在加载；若未更新，请重新打开资产库";
    case Status::AccessDenied:return L"无权访问；请在连接设置中登录，再重新打开资产库";
    case Status::Expired:return L"位置已过期；请重新打开资产库";
    case Status::InvalidResponse:return L"响应无效；请检查连接设置，再重新打开资产库";
    case Status::Busy:return L"服务繁忙；请稍后重新打开资产库";
    default:return L"后台连接服务不可用；请打开连接设置，登录后重新打开资产库";
    }
}
const wchar_t* TypeText(Kind kind) noexcept {
    switch(kind){case Kind::Library:return L"资源库";case Kind::Directory:return L"文件夹";case Kind::File:return L"文件（本版不打开内容）";case Kind::Reparse:return L"链接项目（不可进入）";case Kind::NextPage:return L"下一页导航";default:return L"状态（重新打开资产库）";}
}
}
