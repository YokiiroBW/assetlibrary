#pragma once
#include <UIAutomation.h>
#include <memory>

namespace gallery {
struct AccessibleModel;

// All creation, state access and retirement occur on the owning UI STA.
// Providers keep only the independent providerLifetimeOwner/DLL lease.
HRESULT CreateUiaRoot(const std::shared_ptr<AccessibleModel>& model, IRawElementProviderSimple** result) noexcept;
HRESULT RetireUia(const std::shared_ptr<AccessibleModel>& model) noexcept;

struct UiaDiagnostics {
    UINT providers = 0;
    UINT pendingRetirements = 0;
    UINT disconnected = 0;
    HRESULT lastResult = S_OK;
};
UiaDiagnostics InspectUia() noexcept;
} // namespace gallery
