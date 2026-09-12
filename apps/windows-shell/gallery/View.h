#pragma once
#include "ViewRequests.h"
#include <shlobj.h>

namespace gallery {
HRESULT CreateView(IShellFolder2* folder,PCIDLIST_ABSOLUTE absolute,snapshot::Location location,
    IShellView** result,Sources sources={},PCIDLIST_ABSOLUTE notificationRoot=nullptr) noexcept;
}
