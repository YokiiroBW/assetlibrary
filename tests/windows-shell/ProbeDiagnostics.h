#pragma once
#include "Snapshot.h"
#include <shlobj.h>
#include <oleauto.h>
#include <atomic>

namespace loading { struct Signal; }
namespace diagnostics {
// Test-only, unregistered property. No AssetLink or persisted property schema change.
inline constexpr PROPERTYKEY Key={{0x2f242d38,0xc686,0x4e35,{0x87,0xc3,0x36,0xc9,0xba,0xf4,0x4e,0xfe}},1};
inline constexpr PROPERTYKEY G3Key={Key.fmtid,2};
constexpr size_t MaxCharacters=2048;
struct FolderState {
    const ULONGLONG instance,source;
    const DWORD createdThread=GetCurrentThreadId();
    std::atomic<ULONGLONG> lastClone{0};
    std::atomic_ulong enumerations{0},enumThread{0};
    std::atomic<snapshot::Status> enumStatus{snapshot::Status::Unavailable};
    std::atomic<HRESULT> lastCallbackResult{E_PENDING};
    explicit FolderState(ULONGLONG sourceInstance=0) noexcept;
};
HRESULT Read(const FolderState& folder,const loading::Signal* signal,VARIANT* value) noexcept;
HRESULT ReadG3(const FolderState& folder,const loading::Signal* signal,LONG objects,LONG locks,VARIANT* value) noexcept;
}
