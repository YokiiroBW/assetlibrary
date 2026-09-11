#pragma once
#include <shlobj.h>

HRESULT CreateSnapshotIcon(UINT count, PCUITEMID_CHILD_ARRAY items, REFIID iid, void** result) noexcept;
