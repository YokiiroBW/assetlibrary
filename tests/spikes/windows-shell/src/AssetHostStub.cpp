#include "AssetShellProtocol.h"

#include <windows.h>

#include <algorithm>
#include <cstdio>
#include <cwchar>
#include <string>
#include <vector>

namespace {
using assetlibrary::m0002::FrameHeader;
using assetlibrary::m0002::kMagic;
using assetlibrary::m0002::kMaxPayloadBytes;
using assetlibrary::m0002::kMessageViewPing;
using assetlibrary::m0002::kMessageViewPong;
using assetlibrary::m0002::kPipeName;
using assetlibrary::m0002::kVersion;

struct Options {
  unsigned delay_ms = 0;
  unsigned crash_after = 0;
  bool invalid_response = false;
  bool once = false;
};

bool ParseUnsigned(const wchar_t* value, unsigned* result) {
  if (value == nullptr || result == nullptr || *value == L'\0') return false;
  wchar_t* end = nullptr;
  const unsigned long parsed = std::wcstoul(value, &end, 10);
  if (*end != L'\0' || parsed > 10000) return false;
  *result = static_cast<unsigned>(parsed);
  return true;
}

bool ParseOptions(int argc, wchar_t** argv, Options* options) {
  for (int i = 1; i < argc; ++i) {
    const std::wstring argument(argv[i]);
    if (argument == L"--invalid-response") {
      options->invalid_response = true;
    } else if (argument == L"--once") {
      options->once = true;
    } else if (argument.rfind(L"--delay-ms=", 0) == 0) {
      if (!ParseUnsigned(argument.c_str() + 11, &options->delay_ms)) return false;
    } else if (argument.rfind(L"--crash-after=", 0) == 0) {
      if (!ParseUnsigned(argument.c_str() + 14, &options->crash_after)) return false;
    } else {
      return false;
    }
  }
  return true;
}

bool ReadExact(HANDLE pipe, void* buffer, DWORD length) {
  auto* bytes = static_cast<unsigned char*>(buffer);
  DWORD total = 0;
  while (total < length) {
    DWORD read = 0;
    if (!ReadFile(pipe, bytes + total, length - total, &read, nullptr) || read == 0) return false;
    total += read;
  }
  return true;
}

bool WriteExact(HANDLE pipe, const void* buffer, DWORD length) {
  const auto* bytes = static_cast<const unsigned char*>(buffer);
  DWORD total = 0;
  while (total < length) {
    DWORD written = 0;
    if (!WriteFile(pipe, bytes + total, length - total, &written, nullptr) || written == 0) return false;
    total += written;
  }
  return true;
}

int Serve(const Options& options) {
  unsigned served = 0;
  for (;;) {
    HANDLE pipe = CreateNamedPipeW(
        kPipeName, PIPE_ACCESS_DUPLEX,
        PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT, 1,
        sizeof(FrameHeader) + kMaxPayloadBytes, sizeof(FrameHeader) + kMaxPayloadBytes,
        1000, nullptr);
    if (pipe == INVALID_HANDLE_VALUE) return 2;

    const BOOL connected = ConnectNamedPipe(pipe, nullptr)
        ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
    if (!connected) {
      CloseHandle(pipe);
      continue;
    }

    FrameHeader request{};
    bool ok = ReadExact(pipe, &request, sizeof(request));
    if (ok && request.magic == kMagic && request.version == kVersion &&
        request.message_type == kMessageViewPing && request.payload_length <= kMaxPayloadBytes) {
      std::vector<unsigned char> payload(request.payload_length);
      ok = payload.empty() || ReadExact(pipe, payload.data(), request.payload_length);
    } else {
      ok = false;
    }

    if (options.crash_after != 0 && ++served >= options.crash_after) {
      TerminateProcess(GetCurrentProcess(), 17);
    }
    if (options.delay_ms != 0) Sleep(options.delay_ms);

    if (ok && options.invalid_response) {
      FrameHeader invalid{0, kVersion, kMessageViewPong, kMaxPayloadBytes + 1, request.request_id};
      WriteExact(pipe, &invalid, sizeof(invalid));
    } else if (ok) {
      constexpr char response[] = "asset-host-ready";
      FrameHeader response_header{kMagic, kVersion, kMessageViewPong,
                                  static_cast<std::uint32_t>(sizeof(response) - 1), request.request_id};
      ok = WriteExact(pipe, &response_header, sizeof(response_header));
      if (ok) ok = WriteExact(pipe, response, sizeof(response) - 1);
    }
    FlushFileBuffers(pipe);
    DisconnectNamedPipe(pipe);
    CloseHandle(pipe);
    if (options.once) return 0;
  }
}
} // namespace

int wmain(int argc, wchar_t** argv) {
  Options options;
  if (!ParseOptions(argc, argv, &options)) {
    std::fwprintf(
        stderr,
        L"usage: AssetHostStub [--once] [--delay-ms=0..10000] "
        L"[--crash-after=N] [--invalid-response]\n");
    return 64;
  }
  return Serve(options);
}
