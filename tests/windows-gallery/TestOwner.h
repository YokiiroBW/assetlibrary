#pragma once
#include <windows.h>
#include <unknwn.h>
#include <atomic>
namespace gallery_test {
class Owner final : public IUnknown {
public:
    const DWORD thread = GetCurrentThreadId();
    std::atomic_ulong references{1};
    std::atomic_bool wrongThread{false};
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** result) override {
        if (!result) return E_POINTER; *result = nullptr; if (iid != IID_IUnknown) return E_NOINTERFACE;
        *result = static_cast<IUnknown*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { if (GetCurrentThreadId() != thread) wrongThread = true; return ++references; }
    ULONG STDMETHODCALLTYPE Release() override { if (GetCurrentThreadId() != thread) wrongThread = true; return --references; }
};
} // namespace gallery_test
