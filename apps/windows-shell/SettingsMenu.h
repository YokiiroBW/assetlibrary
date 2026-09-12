#pragma once
#include <shlobj.h>

HRESULT CreateSettingsMenu(IUnknown* owner, REFIID iid, void** result) noexcept;
