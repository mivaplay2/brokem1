cmake_minimum_required(VERSION 3.20)
project(cs2_range_esp LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 20)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(NOT WIN32)
    message(FATAL_ERROR "cs2_range_esp targets Windows x64 only.")
endif()

if(CMAKE_SIZEOF_VOID_P LESS 8)
    message(FATAL_ERROR "cs2_range_esp must be built as x64 (configure with -A x64).")
endif()

add_executable(cs2_range_esp
    src/main.cpp
    src/math.cpp
    src/process.cpp
    src/renderer.cpp
    src/config.hpp
    src/math.hpp
    src/offsets.hpp
    src/process.hpp
    src/renderer.hpp
    src/sdk.hpp
)

target_include_directories(cs2_range_esp PRIVATE src)

target_compile_definitions(cs2_range_esp PRIVATE
    UNICODE
    _UNICODE
    NOMINMAX
    WIN32_LEAN_AND_MEAN
)

target_link_libraries(cs2_range_esp PRIVATE user32 gdi32)

if(MSVC)
    target_compile_options(cs2_range_esp PRIVATE /W4 /permissive- /EHsc)
else()
    target_compile_options(cs2_range_esp PRIVATE -Wall -Wextra)
endif()
