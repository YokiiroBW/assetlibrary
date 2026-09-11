#pragma once
#include "Snapshot.h"
#include <shlobj.h>
#include <atomic>
#include <memory>

namespace loading {
constexpr ULONGLONG IntervalMs=500, DurationMs=10000;
constexpr unsigned MaxAttempts=20, MaxViews=4;

// One view's monotonic Loading episode. Duplicate/late messages cannot extend it.
struct Budget {
    snapshot::Status previous=snapshot::Status::Unavailable;
    ULONGLONG deadline=0,next=0,episode=0;
    unsigned attempts=0;
    bool active=false;
    bool Observe(snapshot::Status status,ULONGLONG now) noexcept;
    bool Take(ULONGLONG now) noexcept;
    void Stop() noexcept {active=false;}
};

// Enumeration can publish from a worker; only the owner UI thread touches the view.
struct Signal {
    const UINT notification;
    const UINT_PTR cookie;
    std::atomic<ULONGLONG> started{0},published{static_cast<ULONGLONG>(snapshot::Status::Unavailable)};
    std::atomic<HWND> window{nullptr};
    std::atomic_bool queued{false};
    Signal();
    ULONGLONG Begin() noexcept {return started.fetch_add(1)+1;}
    void Publish(ULONGLONG generation,snapshot::Status value) noexcept;
    void Publish(snapshot::Status value) noexcept;
    bool Read(snapshot::Status& value,ULONGLONG* generation=nullptr) const noexcept;
};
HRESULT CreateCallback(IUnknown* owner,const std::shared_ptr<Signal>& signal,IShellFolderViewCB** result) noexcept;
HRESULT CreateView(IShellFolder* folder,const std::shared_ptr<Signal>& signal,IShellView** result) noexcept;
ULONG ActiveViews() noexcept;
ULONG LiveCallbacks() noexcept;
}
