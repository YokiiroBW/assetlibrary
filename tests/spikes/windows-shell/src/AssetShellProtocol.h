#pragma once

// Spike-local protocol. It is deliberately not AssetLink and has no business payload.
#include <cstdint>

namespace assetlibrary::m0002 {

inline constexpr std::uint32_t kMagic = 0x324C5341; // "ASL2" in little endian.
inline constexpr std::uint16_t kVersion = 1;
inline constexpr std::uint16_t kMessageViewPing = 1;
inline constexpr std::uint16_t kMessageViewPong = 2;
inline constexpr std::uint32_t kMaxPayloadBytes = 4096;
inline constexpr std::uint32_t kClientTimeoutMs = 250;
inline constexpr wchar_t kPipeName[] = LR"(\\.\pipe\AssetLibrary.M0-002.v1)";

#pragma pack(push, 1)
struct FrameHeader {
  std::uint32_t magic;
  std::uint16_t version;
  std::uint16_t message_type;
  std::uint32_t payload_length;
  std::uint32_t request_id;
};
#pragma pack(pop)

static_assert(sizeof(FrameHeader) == 16, "IPC header must remain a fixed 16-byte frame prefix");

} // namespace assetlibrary::m0002
