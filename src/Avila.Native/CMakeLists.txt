cmake_minimum_required(VERSION 3.20)
project(AvilaNative LANGUAGES CXX)

add_library(AvilaNative SHARED AvilaNative.cpp)
target_compile_features(AvilaNative PRIVATE cxx_std_17)

if (MSVC)
  target_compile_options(AvilaNative PRIVATE /W4 /permissive-)
else()
  target_compile_options(AvilaNative PRIVATE -Wall -Wextra -Wpedantic)
endif()
