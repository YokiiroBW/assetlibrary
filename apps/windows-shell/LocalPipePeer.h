#pragma once
#include <windows.h>
#include <string>
#include <vector>

namespace local_pipe {
struct Handle {
    HANDLE value=nullptr;
    explicit Handle(HANDLE handle=nullptr):value(handle){}
    ~Handle(){if(value&&value!=INVALID_HANDLE_VALUE)CloseHandle(value);}
    Handle(const Handle&)=delete;Handle& operator=(const Handle&)=delete;
};
bool UserSid(HANDLE process,std::vector<BYTE>& storage);
bool SamePeer(HANDLE pipe,HANDLE& peer,ULONGLONG started);
std::wstring Name(const wchar_t* prefix);
}
