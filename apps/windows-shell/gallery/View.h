#pragma once
#include "ViewRequests.h"
#include "BrowsePreferences.h"
#include <shlobj.h>

namespace gallery {
struct PreferenceStore {
    preferences::Values (*load)() noexcept=preferences::Load;
    HRESULT (*save)(const preferences::Values&) noexcept=preferences::Save;
};
HRESULT CreateView(IShellFolder2* folder,PCIDLIST_ABSOLUTE absolute,snapshot::Location location,
    IShellView** result,Sources sources={},PCIDLIST_ABSOLUTE notificationRoot=nullptr,PreferenceStore preferences={}) noexcept;
}
