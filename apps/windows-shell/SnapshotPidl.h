#pragma once
#include "Snapshot.h"
#include <shlobj.h>

namespace snapshot {
PITEMID_CHILD MakePidl(const Entry& entry);
bool ReadPidl(PCUIDLIST_RELATIVE pidl, Entry& entry);
bool IsOurPidl(PCUIDLIST_RELATIVE pidl) noexcept;
bool BoundedList(PCUIDLIST_RELATIVE pidl, UINT& bytes, UINT& count) noexcept;
std::wstring ParsingName(const Entry& entry);
bool ParseName(const wchar_t* name, PIDLIST_RELATIVE* result, ULONG* eaten, Kind& lastKind);
}
