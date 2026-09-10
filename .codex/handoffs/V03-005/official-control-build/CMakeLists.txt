cmake_minimum_required(VERSION 3.20)
set(CMAKE_SYSTEM_VERSION "10.0.26100.0")
project(OfficialExplorerDataProviderControl LANGUAGES CXX RC)
set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
add_library(ExplorerDataProvider SHARED
  source/Category.cpp
  source/ContextMenu.cpp
  source/Dll.cpp
  source/ExplorerDataProvider.cpp
  source/FVCommands.cpp
  source/Utils.cpp
  source/ExplorerDataProvider.rc
  source/ExplorerDataProvider.def)
set_property(TARGET ExplorerDataProvider PROPERTY MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
target_include_directories(ExplorerDataProvider PRIVATE source)
target_compile_definitions(ExplorerDataProvider PRIVATE UNICODE _UNICODE WIN32 _WINDOWS _USRDLL EXPLORERDATAPROVIDER_EXPORTS)
target_compile_options(ExplorerDataProvider PRIVATE $<$<COMPILE_LANGUAGE:CXX>:/W4> $<$<COMPILE_LANGUAGE:CXX>:/WX> $<$<COMPILE_LANGUAGE:CXX>:/utf-8>)
target_link_libraries(ExplorerDataProvider PRIVATE propsys shlwapi ole32 oleaut32 shell32 user32 advapi32 uuid)
target_link_options(ExplorerDataProvider PRIVATE /SUBSYSTEM:WINDOWS)
add_custom_command(TARGET ExplorerDataProvider POST_BUILD
  COMMAND ${CMAKE_COMMAND} -E copy_if_different
    "${CMAKE_CURRENT_SOURCE_DIR}/source/ExplorerDataProvider.propdesc"
    "$<TARGET_FILE_DIR:ExplorerDataProvider>/ExplorerDataProvider.propdesc")
