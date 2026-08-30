cmake_minimum_required(VERSION 3.20)
project(AssetLibraryM0002Shell LANGUAGES CXX)

if(NOT WIN32)
  message(WARNING "M0-002 is a Windows-only build; CMake configure is provided for inspection on non-Windows hosts.")
endif()

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_library(AssetShellExtension SHARED
  src/AssetShellExtension.cpp
)
target_compile_definitions(AssetShellExtension PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX WINRT_LEAN_AND_MEAN)
target_compile_options(AssetShellExtension PRIVATE /W4 /permissive-)
target_link_libraries(AssetShellExtension PRIVATE ole32 shell32 user32 advapi32)
set_target_properties(AssetShellExtension PROPERTIES OUTPUT_NAME "AssetShellExtension")

add_executable(AssetHostStub src/AssetHostStub.cpp)
target_compile_definitions(AssetHostStub PRIVATE UNICODE _UNICODE WIN32_LEAN_AND_MEAN NOMINMAX)
target_compile_options(AssetHostStub PRIVATE /W4 /permissive-)
target_link_libraries(AssetHostStub PRIVATE kernel32)

install(TARGETS AssetShellExtension AssetHostStub RUNTIME DESTINATION bin LIBRARY DESTINATION bin)
