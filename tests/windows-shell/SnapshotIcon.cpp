#include "SnapshotIcon.h"
#include "SnapshotPidl.h"
#include <shellapi.h>
#include <new>

HRESULT CreateSnapshotIcon(UINT count,PCUITEMID_CHILD_ARRAY items,REFIID iid,void** result) noexcept {
    if(!result)return E_POINTER;*result=nullptr;
    if(count!=1||(iid!=IID_IExtractIconW&&iid!=IID_IExtractIconA))return E_NOINTERFACE;
    if(!items||!items[0])return E_INVALIDARG;
    try {
        UINT bytes=0,segments=0;snapshot::Entry entry;
        if(!snapshot::BoundedList(items[0],bytes,segments)||segments!=1||!snapshot::ReadPidl(items[0],entry))return E_INVALIDARG;
        SHSTOCKICONINFO stock{sizeof(stock)};
        // Only Windows stock resources: never an asset path or its associated handler.
        auto hr=SHGetStockIconInfo(snapshot::Navigable(entry.kind)?SIID_FOLDER:SIID_DOCNOASSOC,SHGSI_ICONLOCATION,&stock);
        if(FAILED(hr))return hr;
        IDefaultExtractIconInit* icon=nullptr;hr=SHCreateDefaultExtractIcon(IID_PPV_ARGS(&icon));
        if(FAILED(hr))return hr;
        if(!icon)return E_UNEXPECTED;
        hr=icon->SetNormalIcon(stock.szPath,stock.iIcon);
        if(SUCCEEDED(hr))hr=icon->SetOpenIcon(stock.szPath,stock.iIcon);
        if(SUCCEEDED(hr))hr=icon->QueryInterface(iid,result);
        icon->Release();return hr;
    }catch(const std::bad_alloc&){return E_OUTOFMEMORY;}
}
