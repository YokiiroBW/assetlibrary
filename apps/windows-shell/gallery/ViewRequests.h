#pragma once
#include "ThumbnailClient.h"

namespace gallery {
struct Sources {
    snapshot::Page (*page)(const snapshot::Location&,HANDLE) noexcept=snapshot::Query;
    thumbnail::Result (*image)(const snapshot::Location&,HANDLE) noexcept=thumbnail::Query;
    preview::Result (*preview)(const snapshot::Location&,HANDLE) noexcept=preview::Query;
};
struct Completion {
    std::uint64_t generation=0;
    std::uint64_t ticket=0;
    bool page=false;
    bool preview=false;
    UINT index=0;
    snapshot::Page snapshot;
    thumbnail::Result thumbnail;
};
struct RequestState;
// UI-owned controller. Background work retains only plain state, never a COM object.
class Requests final {
    std::shared_ptr<RequestState> state_;
    HRESULT Images(const snapshot::Page& page,const VisibleFiles& visible,std::uint64_t generation,bool preview) noexcept;
public:
    explicit Requests(Sources sources={});
    ~Requests();
    Requests(const Requests&)=delete;Requests& operator=(const Requests&)=delete;
    void Attach(HWND target,UINT message) noexcept;
    HRESULT Begin(snapshot::Location location,std::uint64_t generation) noexcept;
    void Cancel(std::uint64_t generation) noexcept;
    void Close() noexcept;
    HRESULT Visible(const snapshot::Page& page,const VisibleFiles& visible,std::uint64_t generation) noexcept;
    HRESULT Preview(const snapshot::Page& page,UINT index,std::uint64_t generation) noexcept;
    std::vector<Completion> Take();
    bool Current(const Completion& completion) const noexcept;
};
ULONG PendingWork() noexcept;
}
