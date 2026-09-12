#pragma once
#include <windows.h>
#include <array>
#include <string>
#include <vector>

namespace snapshot {
constexpr ULONG Magic = 0x31534C41;
constexpr size_t HeaderBytes = 16, MaxPayload = 65536, MaxItems = 101;
constexpr DWORD WaitBudgetMs = 150;
enum class Status : ULONG { Ready, Loading, Unavailable, AccessDenied, Expired, InvalidResponse, Busy };
enum class Kind : USHORT { Library = 1, Directory, File, Reparse, NextPage, StatusRow = 0x8000 };
struct Location { GUID epoch{}, node{}; };
struct Entry { GUID epoch{}, node{}; Kind kind = Kind::File; std::wstring name; Status status = Status::Ready; };
struct Page { Status status = Status::Unavailable; GUID epoch{}; std::vector<Entry> entries; };
bool Zero(REFGUID value) noexcept;
bool ValidName(const std::wstring& value) noexcept;
bool Navigable(Kind kind) noexcept;
std::array<BYTE,48> Request(const Location& location, ULONG requestId);
bool ResponseHeader(const BYTE* header, ULONG requestId, ULONG& payloadBytes) noexcept;
bool Decode(const BYTE* payload, size_t bytes, Page& page);
Page Query(const Location& location, HANDLE cancel = nullptr) noexcept;
std::wstring PipeName();
ULONG PendingOperations() noexcept;
const wchar_t* StatusText(Status status) noexcept;
const wchar_t* TypeText(Kind kind) noexcept;
}
