cmake_minimum_required(VERSION 3.20)
project(SensorOverlay LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

add_executable(sensor_overlay WIN32 src/main.cpp)

target_compile_definitions(sensor_overlay PRIVATE UNICODE _UNICODE)
target_link_libraries(sensor_overlay PRIVATE wbemuuid windowscodecs)