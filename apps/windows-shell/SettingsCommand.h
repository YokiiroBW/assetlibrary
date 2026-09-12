#pragma once
#include <windows.h>
#include <shlobj.h>
#include <string>

namespace settings {
struct Command {
    std::wstring executable;
    std::wstring commandLine;
    std::wstring directory;
};
// Pure construction: the caller supplies only its own loaded module's path.
HRESULT BuildCommand(const std::wstring& modulePath, Command& command) noexcept;
HRESULT ValidateVerb(const CMINVOKECOMMANDINFO* info) noexcept;
HRESULT Launch() noexcept;
}
