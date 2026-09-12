#pragma once
#include <windows.h>

namespace product {
// Both variants compile the same implementation. No runtime fallback to Proof.
#if defined(ASSETLIBRARY_EXPLORER_PROOF)
inline constexpr bool Production = false;
inline constexpr CLSID ClassId = {0x4ff8301d,0x2e73,0x4d49,{0x9f,0xe5,0x86,0x8d,0x5f,0x1e,0xa3,0x02}};
inline constexpr wchar_t ParsingRoot[] = L"::{4FF8301D-2E73-4D49-9FE5-868D5F1EA302}";
inline constexpr wchar_t PipePrefix[] = L"\\\\.\\pipe\\AssetLibrary.ExplorerProof.v1.";
inline constexpr wchar_t Title[] = L"AssetLibrary 集成验证";
inline constexpr wchar_t LoadingMessage[] = L"AssetLibrary.ExplorerProof.LoadingState.v1";
#else
inline constexpr bool Production = true;
inline constexpr CLSID ClassId = {0xbbc992de,0xce5d,0x48c8,{0xa8,0x6c,0x72,0x30,0xc7,0xd7,0x2b,0x02}};
inline constexpr wchar_t ParsingRoot[] = L"::{BBC992DE-CE5D-48C8-A86C-7230C7D72B02}";
inline constexpr wchar_t PipePrefix[] = L"\\\\.\\pipe\\AssetLibrary.Explorer.v1.";
inline constexpr wchar_t Title[] = L"资产库";
inline constexpr wchar_t LoadingMessage[] = L"AssetLibrary.Explorer.LoadingState.v1";
#endif
inline constexpr wchar_t SettingsExecutable[] = L"AssetLibrary.Settings.exe";
inline constexpr wchar_t SessionMessage[] = L"AssetLibrary.Explorer.SessionChanged.v1";
}
