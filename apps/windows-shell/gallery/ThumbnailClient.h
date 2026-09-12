#pragma once
#include "Surface.h"

namespace thumbnail {
constexpr ULONG Magic=0x31474C41,PrefixBytes=56,MaxPixelBytes=1048576,MaxPayload=PrefixBytes+MaxPixelBytes;
constexpr DWORD WaitBudgetMs=20000;
enum class Status:ULONG {Ready,Loading,Unavailable,AccessDenied,Expired,InvalidResponse,Busy,Unsupported};
struct Result {
    Status status=Status::Unavailable;
    snapshot::Location location;
    std::shared_ptr<const gallery::Pbgra> image;
};
std::array<BYTE,48> Request(const snapshot::Location& location,ULONG requestId);
bool ResponseHeader(const BYTE* header,ULONG requestId,ULONG& bytes) noexcept;
bool Decode(const BYTE* payload,size_t bytes,const snapshot::Location& requested,Result& result);
// Background callers only. Buffers and DLL remain pinned through actual canceled I/O completion.
Result Query(const snapshot::Location& location,HANDLE cancel) noexcept;
std::wstring PipeName();
ULONG PendingOperations() noexcept;
}
