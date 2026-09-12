#include "G3Measurements.h"

namespace g3 {
Measurements measurements;
ULONGLONG Now() noexcept {LARGE_INTEGER value{};QueryPerformanceCounter(&value);return static_cast<ULONGLONG>(value.QuadPart);}
ULONGLONG Frequency() noexcept {LARGE_INTEGER value{};QueryPerformanceFrequency(&value);return static_cast<ULONGLONG>(value.QuadPart);}
void Maximum(std::atomic<ULONGLONG>& maximum,ULONGLONG value) noexcept {
    auto previous=maximum.load();while(previous<value&&!maximum.compare_exchange_weak(previous,value)){}
}
}
