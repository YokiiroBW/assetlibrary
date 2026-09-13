#include "BrowsePreferences.h"

namespace gallery::preferences {
namespace {
constexpr ULONG Magic=0x31504241;
struct Key {
    HKEY value=nullptr;
    ~Key(){if(value)RegCloseKey(value);}
};
ULONG Read32(const BYTE* bytes) noexcept {
    return static_cast<ULONG>(bytes[0])|(static_cast<ULONG>(bytes[1])<<8)|
        (static_cast<ULONG>(bytes[2])<<16)|(static_cast<ULONG>(bytes[3])<<24);
}
void Write32(BYTE* bytes,ULONG value) noexcept {
    for(UINT at=0;at<4;++at)bytes[at]=static_cast<BYTE>(value>>(at*8));
}
bool Valid(const Values& value) noexcept {
    return (value.mode==Mode::Gallery||value.mode==Mode::List)&&
        value.densityDip>=MinimumDensityDip&&value.densityDip<=MaximumDensityDip;
}
}
bool Decode(const BYTE* bytes,size_t length,Values& value) noexcept {
    value={};
    if(!bytes||length!=RecordBytes||Read32(bytes)!=Magic||bytes[4]!=1||bytes[5]!=0||bytes[6]!=RecordBytes||bytes[7]!=0)return false;
    const auto mode=Read32(bytes+8),density=Read32(bytes+12);
    if(mode>1||density<MinimumDensityDip||density>MaximumDensityDip)return false;
    value={mode?Mode::List:Mode::Gallery,density};return true;
}
bool Encode(const Values& value,Record& bytes) noexcept {
    bytes={};if(!Valid(value))return false;
    Write32(bytes.data(),Magic);bytes[4]=1;bytes[6]=static_cast<BYTE>(RecordBytes);
    Write32(bytes.data()+8,value.mode==Mode::List?1u:0u);Write32(bytes.data()+12,value.densityDip);return true;
}
Values LoadFromKey(HKEY key) noexcept {
    Values value;if(!key)return value;
    Record bytes{};DWORD length=static_cast<DWORD>(bytes.size());
    const auto result=RegGetValueW(key,nullptr,ValueName,RRF_RT_REG_BINARY|RRF_ZEROONFAILURE,nullptr,bytes.data(),&length);
    if(result==ERROR_SUCCESS)Decode(bytes.data(),length,value);
    return value;
}
HRESULT SaveToKey(HKEY key,const Values& value) noexcept {
    Record bytes;if(!key||!Encode(value,bytes))return E_INVALIDARG;
    // Mode and density use one registry update, never separately writable fields.
    return HRESULT_FROM_WIN32(RegSetValueExW(key,ValueName,0,REG_BINARY,bytes.data(),static_cast<DWORD>(bytes.size())));
}
Values Load() noexcept {
    Key key;
    if(RegOpenKeyExW(HKEY_CURRENT_USER,RegistryPath,0,KEY_QUERY_VALUE|KEY_WOW64_64KEY,&key.value)!=ERROR_SUCCESS)return {};
    return LoadFromKey(key.value);
}
HRESULT Save(const Values& value) noexcept {
    if(!Valid(value))return E_INVALIDARG;
    Key key;
    const auto result=RegCreateKeyExW(HKEY_CURRENT_USER,RegistryPath,0,nullptr,REG_OPTION_NON_VOLATILE,
        KEY_SET_VALUE|KEY_WOW64_64KEY,nullptr,&key.value,nullptr);
    if(result!=ERROR_SUCCESS)return HRESULT_FROM_WIN32(result);
    return SaveToKey(key.value,value);
}
}
