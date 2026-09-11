#pragma once
#include <shlobj.h>

HRESULT CreateNavigationMenu(IUnknown* owner, PCIDLIST_ABSOLUTE parent,
    UINT count, PCUITEMID_CHILD_ARRAY items, REFIID iid, void** result) noexcept;
