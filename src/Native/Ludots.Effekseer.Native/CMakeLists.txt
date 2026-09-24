cmake_minimum_required(VERSION 3.20)

project(LudotsEffekseerNative LANGUAGES C CXX)

foreach(contract_value
    LUDOTS_EFFEKSEER_BRIDGE_ABI_VERSION
    LUDOTS_EFFEKSEER_RUNTIME_FORMAT_VERSION
    LUDOTS_EFFEKSEER_DYNAMIC_INPUT_SLOT_COUNT
    LUDOTS_EFFEKSEER_VERSION)
  if(NOT DEFINED ${contract_value})
    message(FATAL_ERROR "${contract_value} must be supplied from src/Libraries/Effekseer/runtime-contract.json.")
  endif()
endforeach()

if(NOT DEFINED EFFEKSEER_ROOT)
  message(FATAL_ERROR "EFFEKSEER_ROOT must point to the pinned EffekseerForCpp source package.")
endif()

if(NOT EXISTS "${EFFEKSEER_ROOT}/src/Effekseer/Effekseer.h")
  message(FATAL_ERROR "EFFEKSEER_ROOT does not contain the pinned EffekseerForCpp source package: ${EFFEKSEER_ROOT}")
endif()

set(BUILD_GL ON CACHE BOOL "" FORCE)
set(BUILD_DX9 OFF CACHE BOOL "" FORCE)
set(BUILD_DX11 OFF CACHE BOOL "" FORCE)
set(BUILD_DX12 OFF CACHE BOOL "" FORCE)
set(BUILD_VULKAN OFF CACHE BOOL "" FORCE)
set(BUILD_METAL OFF CACHE BOOL "" FORCE)
set(BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
set(USE_OPENGL3 ON CACHE BOOL "" FORCE)
set(USE_OPENGLES2 OFF CACHE BOOL "" FORCE)
set(USE_OPENGLES3 OFF CACHE BOOL "" FORCE)
set(USE_OPENAL OFF CACHE BOOL "" FORCE)
set(USE_XAUDIO2 OFF CACHE BOOL "" FORCE)
set(USE_DSOUND OFF CACHE BOOL "" FORCE)
set(USE_MSVC_RUNTIME_LIBRARY_DLL OFF CACHE BOOL "" FORCE)

list(APPEND CMAKE_MODULE_PATH "${EFFEKSEER_ROOT}/cmake")
include(FilterFolder)

set(EFK_RUNTIME_ROOT "${EFFEKSEER_ROOT}/src")
add_subdirectory("${EFFEKSEER_ROOT}/src" "${CMAKE_CURRENT_BINARY_DIR}/effekseer")

add_library(ludots_effekseer SHARED
  ludots_effekseer.cpp
  ludots_effekseer.h)

target_compile_features(ludots_effekseer PRIVATE cxx_std_17)
target_compile_definitions(ludots_effekseer PRIVATE
  LUDOTS_EFFEKSEER_NATIVE_EXPORTS=1
  LUDOTS_EFFEKSEER_BRIDGE_ABI_VERSION=${LUDOTS_EFFEKSEER_BRIDGE_ABI_VERSION}
  LUDOTS_EFFEKSEER_RUNTIME_FORMAT_VERSION=${LUDOTS_EFFEKSEER_RUNTIME_FORMAT_VERSION}
  LUDOTS_EFFEKSEER_DYNAMIC_INPUT_SLOT_COUNT=${LUDOTS_EFFEKSEER_DYNAMIC_INPUT_SLOT_COUNT}
  LUDOTS_EFFEKSEER_VERSION="${LUDOTS_EFFEKSEER_VERSION}")
target_link_libraries(ludots_effekseer PRIVATE Effekseer EffekseerRendererGL)

if(MSVC)
  set_property(TARGET ludots_effekseer PROPERTY
    MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
endif()

if(WIN32)
  target_link_libraries(ludots_effekseer PRIVATE opengl32)
endif()

set_target_properties(ludots_effekseer PROPERTIES
  OUTPUT_NAME "ludots_effekseer"
  DEBUG_POSTFIX "")
