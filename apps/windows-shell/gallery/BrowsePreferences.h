#pragma once
#include "Surface.h"

namespace gallery::preferences {
inline constexpr wchar_t RegistryPath[]=L"Software\\AssetLibrary\\ExplorerPreferences";
inline constexpr wchar_t ValueName[]=L"BrowseState";
inline constexpr size_t RecordBytes=16;
using Record=std::array<BYTE,RecordBytes>;

struct Values {
    Mode mode=Mode::Gallery;
    UINT densityDip=DefaultDensityDip;
};

// Fixed ADR-0022 record. Failure replaces the output with defaults/zero bytes.
bool Decode(const BYTE* bytes,size_t length,Values& value) noexcept;
bool Encode(const Values& value,Record& bytes) noexcept;

// Borrowed key variants enable isolated tests. They neither open nor close keys.
Values LoadFromKey(HKEY key) noexcept;
HRESULT SaveToKey(HKEY key,const Values& value) noexcept;

// Production always uses the fixed product-specific HKCU 64-bit sibling key.
// Load never creates/repairs a value. Save is only for explicit user changes.
Values Load() noexcept;
HRESULT Save(const Values& value) noexcept;
}
