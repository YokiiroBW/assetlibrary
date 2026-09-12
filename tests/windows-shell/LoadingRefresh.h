#pragma once
#include "Snapshot.h"
#include <shlobj.h>
#include <atomic>
#include <memory>

namespace loading {
constexpr ULONGLONG IntervalMs=500, DurationMs=10000;
constexpr unsigned MaxAttempts=20, MaxViews=4;

// One window's initial observation. Only reopening may start another episode.
struct Budget {
    ULONGLONG deadline=0,next=0,episode=0;
    unsigned attempts=0;
    bool active=false;
    void Start(ULONGLONG now) noexcept;
    bool Take(ULONGLONG now) noexcept;
    void Stop() noexcept {active=false;}
};

// Enumeration can publish from a worker; only the owner UI thread touches the view.
// Fixed last-values/counters only: no trace buffer, pointers, paths or UI call-outs.
struct DiagnosticState {
    std::atomic<ULONGLONG> observationStart{0},renderedReady{0};
    std::atomic_ulong readyThread{0};
    std::atomic<HRESULT> createResult{E_PENDING},siteResult{E_PENDING},windowResult{E_PENDING};
    std::atomic<HRESULT> serviceResult{E_PENDING},activeResult{E_PENDING},getWindowResult{E_PENDING},refreshResult{E_PENDING};
    std::atomic<HRESULT> folderViewResult{E_PENDING},countResult{E_PENDING},itemResult{E_PENDING};
    std::atomic_ulong itemCount{0},pidlValid{0},renderedKind{0},renderedStatus{0};
    std::atomic_ulong createSlots{0},constructorThread{0},siteCalls{0},siteThread{0},sitePresent{0};
    std::atomic_ulong windowCalls{0},windowThread{0},windowOwnerThread{0},posts{0},postError{0};
    std::atomic_ulong arms{0},armThread{0},armError{0},timerActive{0},ticks{0},attempts{0},refreshes{0},detaches{0},windowMatch{0},skip{0};
    std::atomic<UINT_PTR> reportedWindow{0},activeWindow{0};
};
struct Signal {
    const UINT notification;
    const UINT_PTR cookie;
    std::atomic<ULONGLONG> started{0},published{static_cast<ULONGLONG>(snapshot::Status::Unavailable)};
    std::atomic<HWND> window{nullptr};
    std::atomic_bool queued{false};
    DiagnosticState diagnostic;
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
