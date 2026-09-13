#include "gallery/BrowsePreferences.h"
#include <algorithm>
#include <cstdio>
#include <stdexcept>
#include <vector>

namespace {
namespace preferences=gallery::preferences;
void Check(bool value,const char* message){if(!value)throw std::runtime_error(message);}
bool Same(const preferences::Values& left,const preferences::Values& right){return left.mode==right.mode&&left.densityDip==right.densityDip;}
constexpr preferences::Record DefaultBytes{0x41,0x42,0x50,0x31,1,0,16,0,0,0,0,0,176,0,0,0};
constexpr preferences::Record List256Bytes{0x41,0x42,0x50,0x31,1,0,16,0,1,0,0,0,0,1,0,0};
bool cleanupFailed=false;
struct Key {
    HKEY value=nullptr;
    ~Key(){if(value)RegCloseKey(value);}
};
struct TemporaryKey {
    std::wstring path;
    HKEY value=nullptr;
    TemporaryKey(){
        GUID identity{};wchar_t id[40]{};
        Check(SUCCEEDED(CoCreateGuid(&identity))&&StringFromGUID2(identity,id,40)>0,"temporary key identity");
        path=L"Software\\AssetLibrary.BrowsePreferences.Tests."+std::wstring(id);
        DWORD disposition=0;
        const auto result=RegCreateKeyExW(HKEY_CURRENT_USER,path.c_str(),0,nullptr,REG_OPTION_VOLATILE,
            KEY_ALL_ACCESS|KEY_WOW64_64KEY,nullptr,&value,&disposition);
        if(result!=ERROR_SUCCESS)throw std::runtime_error("create only owned temporary HKCU key");
        if(disposition!=REG_CREATED_NEW_KEY){RegCloseKey(value);value=nullptr;throw std::runtime_error("refuse existing test key");}
    }
    ~TemporaryKey(){
        if(!value)return;RegCloseKey(value);
        if(RegDeleteKeyExW(HKEY_CURRENT_USER,path.c_str(),KEY_WOW64_64KEY,0)!=ERROR_SUCCESS)cleanupFailed=true;
        Key absent;const auto result=RegOpenKeyExW(HKEY_CURRENT_USER,path.c_str(),0,KEY_QUERY_VALUE|KEY_WOW64_64KEY,&absent.value);
        if(result!=ERROR_FILE_NOT_FOUND)cleanupFailed=true;
    }
};
std::vector<BYTE> Raw(HKEY key,DWORD& type){
    std::vector<BYTE> bytes(64);DWORD length=static_cast<DWORD>(bytes.size());
    Check(RegQueryValueExW(key,preferences::ValueName,nullptr,&type,bytes.data(),&length)==ERROR_SUCCESS,"raw readback");
    bytes.resize(length);return bytes;
}
void Codec(){
    preferences::Values value{gallery::Mode::List,96};
    Check(preferences::Decode(DefaultBytes.data(),DefaultBytes.size(),value)&&Same(value,{}),"independent default golden record");
    Check(preferences::Decode(List256Bytes.data(),List256Bytes.size(),value)&&Same(value,{gallery::Mode::List,256}),"independent list boundary golden record");
    preferences::Record bytes{};Check(preferences::Encode({},bytes)&&bytes==DefaultBytes,"encode exact default layout");
    Check(preferences::Encode({gallery::Mode::List,256},bytes)&&bytes==List256Bytes,"encode exact little-endian boundary");
    for(auto mode:{gallery::Mode::Gallery,gallery::Mode::List})for(UINT density=96;density<=256;++density){
        const preferences::Values original{mode,density};Check(preferences::Encode(original,bytes)&&preferences::Decode(bytes.data(),bytes.size(),value)&&Same(value,original),"every allowed density round-trips without step quantization");
    }
    const auto rejects=[&](const BYTE* data,size_t length){value={gallery::Mode::List,256};Check(!preferences::Decode(data,length,value)&&Same(value,{}),"malformed record resets entire output");};
    rejects(nullptr,16);rejects(DefaultBytes.data(),0);rejects(DefaultBytes.data(),15);rejects(DefaultBytes.data(),17);rejects(DefaultBytes.data(),65536);
    for(const UINT offset:{0u,4u,5u,6u,7u,8u,13u}){bytes=DefaultBytes;bytes[offset]=0xff;rejects(bytes.data(),bytes.size());}
    bytes=DefaultBytes;bytes[12]=95;rejects(bytes.data(),bytes.size());
    bytes=List256Bytes;bytes[12]=1;rejects(bytes.data(),bytes.size());
    for(const preferences::Values bad: {preferences::Values{static_cast<gallery::Mode>(2),176},preferences::Values{gallery::Mode::List,95},preferences::Values{gallery::Mode::List,257}}){
        bytes=List256Bytes;Check(!preferences::Encode(bad,bytes)&&bytes==preferences::Record{},"invalid values cannot produce a partial record");
    }
}
void Registry(){
    TemporaryKey key;
    Check(Same(preferences::LoadFromKey(key.value),{}),"missing preference defaults");
    DWORD length=0;Check(RegQueryValueExW(key.value,preferences::ValueName,nullptr,nullptr,nullptr,&length)==ERROR_FILE_NOT_FOUND,"load never creates a missing value");
    Check(preferences::SaveToKey(key.value,{gallery::Mode::List,256})==S_OK,"save valid record");
    DWORD type=0;auto bytes=Raw(key.value,type);Check(type==REG_BINARY&&bytes==std::vector<BYTE>(List256Bytes.begin(),List256Bytes.end()),"written value has exact type and sixteen bytes");
    Key second;Check(RegOpenKeyExW(HKEY_CURRENT_USER,key.path.c_str(),0,KEY_QUERY_VALUE|KEY_WOW64_64KEY,&second.value)==ERROR_SUCCESS,"independent reader handle");
    const auto firstView=preferences::LoadFromKey(second.value);Check(Same(firstView,{gallery::Mode::List,256}),"new view reads saved settings");
    Check(preferences::SaveToKey(key.value,{gallery::Mode::Gallery,96})==S_OK,"new explicit change replaces complete tuple");
    Check(Same(preferences::LoadFromKey(second.value),{gallery::Mode::Gallery,96})&&Same(firstView,{gallery::Mode::List,256}),"new readers see change without mutating existing view snapshot");
    const auto previous=Raw(key.value,type);
    Check(preferences::SaveToKey(second.value,{gallery::Mode::List,176})==HRESULT_FROM_WIN32(ERROR_ACCESS_DENIED),"real read-only handle denies write");
    Check(Raw(key.value,type)==previous,"denied write leaves prior record intact");
    Check(preferences::SaveToKey(key.value,{gallery::Mode::Gallery,257})==E_INVALIDARG&&Raw(key.value,type)==previous,"invalid explicit write leaves prior value intact");
    for(const DWORD badType:{REG_SZ,REG_DWORD,REG_BINARY}){
        const BYTE bad[]{0x41,0x42,0x50,0x31,2,0,16,0,1,0,0,0,0,1,0,0};
        const DWORD byteCount=badType==REG_DWORD?sizeof(DWORD):sizeof(bad);
        Check(RegSetValueExW(key.value,preferences::ValueName,0,badType,bad,byteCount)==ERROR_SUCCESS,"store synthetic wrong type/version");
        const auto original=Raw(key.value,type);Check(Same(preferences::LoadFromKey(key.value),{}),"wrong registry type/version defaults");
        Check(Raw(key.value,type)==original&&type==badType,"load preserves unsupported bytes and type");
    }
    for(DWORD size:{0u,15u,17u,64u}){
        BYTE oversized[64]{};std::copy(DefaultBytes.begin(),DefaultBytes.end(),oversized);
        Check(RegSetValueExW(key.value,preferences::ValueName,0,REG_BINARY,oversized,size)==ERROR_SUCCESS,"store synthetic wrong record size");
        const auto original=Raw(key.value,type);Check(Same(preferences::LoadFromKey(key.value),{})&&Raw(key.value,type)==original,"short/long record defaults without growing buffer or repairing value");
    }
    DWORD neighbor=42;Check(RegSetValueExW(key.value,L"unrelated",0,REG_DWORD,reinterpret_cast<const BYTE*>(&neighbor),sizeof(neighbor))==ERROR_SUCCESS,"neighbor fixture");
    Check(preferences::SaveToKey(key.value,{})==S_OK,"explicit user change can replace corrupt preferences");
    length=sizeof(neighbor);neighbor=0;Check(RegGetValueW(key.value,nullptr,L"unrelated",RRF_RT_REG_DWORD,nullptr,&neighbor,&length)==ERROR_SUCCESS&&neighbor==42,"save changes only the owned value");
    Check(preferences::SaveToKey(nullptr,{})==E_INVALIDARG&&Same(preferences::LoadFromKey(nullptr),{}),"invalid borrowed key is safe");
}
}
int main(){
    try{Codec();Registry();Check(!cleanupFailed,"owned temporary key fully removed");puts("gallery_preferences=passed; record=16B; density=96..256; corrupt=default_unchanged; write_denied=preserved; production_preferences=untouched; GUI=0");return 0;}
    catch(const std::exception& error){fprintf(stderr,"GalleryPreferencesTests: %s\n",error.what());return 1;}
}
