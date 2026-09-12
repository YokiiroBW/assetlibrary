#pragma once
#include <windows.h>
#include <atomic>

// Test-only fixed aggregates, shared by the snapshot, callback and COM adapters.
// Reads are non-transactional; no reset, trace, allocation, lock or I/O is added.
namespace g3 {
ULONGLONG Now() noexcept;
ULONGLONG Frequency() noexcept;
void Maximum(std::atomic<ULONGLONG>& maximum,ULONGLONG value) noexcept;
struct Timing {
    std::atomic<ULONGLONG> calls{0},start{0},end{0},last{0},maximum{0};
    std::atomic_ulong thread{0},active{0};
};
struct Measurements {
    Timing enumeration,query,refresh;
    std::atomic<ULONGLONG> cancels{0},deferred{0},reaped{0},cancelLast{0},cancelMaximum{0};
    std::atomic<ULONGLONG> pinSync{0},pinPool{0};
    std::atomic_ulong cancelLive{0},pins{0};
};
extern Measurements measurements;
class Call final {
    Timing& timing_;
    const ULONGLONG start_=Now();
    const DWORD thread_=GetCurrentThreadId();
public:
    explicit Call(Timing& timing) noexcept:timing_(timing){++timing_.calls;++timing_.active;}
    ~Call(){
        const auto end=Now(),elapsed=end-start_;
        timing_.start=start_;timing_.end=end;timing_.last=elapsed;timing_.thread=thread_;
        Maximum(timing_.maximum,elapsed);--timing_.active;
    }
    Call(const Call&)=delete;Call& operator=(const Call&)=delete;
};
}
