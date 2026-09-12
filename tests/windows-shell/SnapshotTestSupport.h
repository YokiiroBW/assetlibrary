#pragma once
// Registration-free fixture/probe support, never linked into the Shell DLL.
#include "SnapshotPidl.h"
#include <shlwapi.h>
#include <cstdio>
#include <stdexcept>
#include <utility>

namespace proof {
inline void Check(bool condition,const char* label){if(!condition)throw std::runtime_error(label);}
template<class T> struct Com {
    T* value=nullptr;
    Com()=default;~Com(){if(value)value->Release();}
    Com(const Com&)=delete;Com& operator=(const Com&)=delete;
    Com(Com&& other)noexcept:value(std::exchange(other.value,nullptr)){}
};
struct Item {
    PITEMID_CHILD value=nullptr;
    explicit Item(PITEMID_CHILD item=nullptr):value(item){}
    ~Item(){CoTaskMemFree(value);}
    Item(const Item&)=delete;Item& operator=(const Item&)=delete;
    Item(Item&& other)noexcept:value(std::exchange(other.value,nullptr)){}
};
class Library {
    HMODULE module_=nullptr;
    CLSID classId_;
public:
    inline static constexpr CLSID ProofClassId={0x4ff8301d,0x2e73,0x4d49,{0x9f,0xe5,0x86,0x8d,0x5f,0x1e,0xa3,0x02}};
    using CanUnload=HRESULT(STDAPICALLTYPE*)();
    CanUnload canUnload=nullptr;
    explicit Library(const wchar_t* path,const CLSID& classId=ProofClassId):classId_(classId){
        module_=LoadLibraryExW(path,nullptr,LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR|LOAD_LIBRARY_SEARCH_SYSTEM32);
        if(!module_)throw std::runtime_error("LoadLibrary");
        canUnload=reinterpret_cast<CanUnload>(GetProcAddress(module_,"DllCanUnloadNow"));
        if(!canUnload){FreeLibrary(module_);module_=nullptr;Check(false,"DllCanUnloadNow export");}
    }
    ~Library(){if(module_)FreeLibrary(module_);}
    Library(const Library&)=delete;Library& operator=(const Library&)=delete;
    Com<IShellFolder2> Root(){
        using GetFactory=HRESULT(STDAPICALLTYPE*)(REFCLSID,REFIID,void**);
        auto get=reinterpret_cast<GetFactory>(GetProcAddress(module_,"DllGetClassObject"));if(!get)throw std::runtime_error("factory export");
        Com<IClassFactory> factory;Check(SUCCEEDED(get(classId_,IID_PPV_ARGS(&factory.value))),"factory");
        Com<IShellFolder2> root;Check(SUCCEEDED(factory.value->CreateInstance(nullptr,IID_PPV_ARGS(&root.value))),"CreateInstance");
        Com<IPersistFolder2> persist;Check(SUCCEEDED(root.value->QueryInterface(IID_PPV_ARGS(&persist.value))),"persist");
        // Fixed empty Desktop-relative root. No shell parsing, registry or GUI involved.
        const USHORT empty=0;
        Check(SUCCEEDED(persist.value->Initialize(reinterpret_cast<PCIDLIST_ABSOLUTE>(&empty))),"Initialize root");return root;
    }
};
inline std::vector<Item> Enumerate(IShellFolder2* folder){
    Com<IEnumIDList> enumerator;
    Check(SUCCEEDED(folder->EnumObjects(nullptr,SHCONTF_FOLDERS|SHCONTF_NONFOLDERS,&enumerator.value))&&enumerator.value,"EnumObjects");
    std::vector<Item> entries;
    for(size_t i=0;i<=snapshot::MaxItems;++i){
        PITEMID_CHILD raw=nullptr;ULONG fetched=0;auto hr=enumerator.value->Next(1,&raw,&fetched);Item item(raw);
        if(hr==S_FALSE){Check(!raw&&!fetched,"enumeration end");return entries;}
        Check(hr==S_OK&&raw&&fetched==1&&i<snapshot::MaxItems,"bounded enumeration");entries.push_back(std::move(item));
    }
    throw std::runtime_error("enumeration bound");
}
inline snapshot::Entry Read(const Item& item){snapshot::Entry entry;Check(snapshot::ReadPidl(item.value,entry),"validated PIDL");return entry;}
inline Com<IShellFolder2> Bind(IShellFolder2* folder,const Item& item){
    Com<IShellFolder2> child;Check(SUCCEEDED(folder->BindToObject(item.value,nullptr,IID_PPV_ARGS(&child.value))),"BindToObject");return child;
}
}
