cmake_minimum_required(VERSION 3.16)
project(DashboardProject VERSION 1.0.0 LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_AUTOMOC ON)

# Baixando o Asio Standalone via FetchContent
include(FetchContent)
FetchContent_Declare(
  asio
  GIT_REPOSITORY https://github.com/chriskohlhoff/asio.git
  GIT_TAG        asio-1-28-0
)
FetchContent_MakeAvailable(asio)

# Baixando o WebSocket++ via FetchContent
FetchContent_Declare(
  websocketpp
  GIT_REPOSITORY https://github.com/zaphoyd/websocketpp.git
  GIT_TAG        master
)
FetchContent_MakeAvailable(websocketpp)

# ── Qt6 — inclui Sql para PostgreSQL (QPSQL driver) ─────────
find_package(Qt6 REQUIRED COMPONENTS Core Widgets Network Gui WebSockets Sql)

# ── Dashboard existente ──────────────────────────────────────
add_executable(DashboardProject WIN32
    main.cpp
    src/ui/LinkedInDashboard.cpp
    src/ui/RobotControlPage.cpp
    src/ui/LinkedInJobsPage.h
)
add_executable(cliente_teste cliente_teste.cpp)

# Asio configuration
target_include_directories(DashboardProject PRIVATE
    ${asio_SOURCE_DIR}/asio/include
    ${websocketpp_SOURCE_DIR}
    ${CMAKE_SOURCE_DIR}
)
target_compile_definitions(DashboardProject PRIVATE ASIO_STANDALONE _WEBSOCKETPP_CPP11_STL_ _WEBSOCKETPP_CPP11_THREAD_ _WEBSOCKETPP_CPP11_FUNCTION_ _WEBSOCKETPP_CPP11_SYSTEM_ERROR_ _WEBSOCKETPP_CPP11_RANDOM_ _WEBSOCKETPP_CPP11_MEMORY_ _WIN32_WINNT=0x0601)

target_include_directories(cliente_teste PRIVATE ${asio_SOURCE_DIR}/asio/include ${websocketpp_SOURCE_DIR})
target_compile_definitions(cliente_teste PRIVATE ASIO_STANDALONE _WEBSOCKETPP_CPP11_STL_ _WEBSOCKETPP_CPP11_THREAD_ _WEBSOCKETPP_CPP11_FUNCTION_ _WEBSOCKETPP_CPP11_SYSTEM_ERROR_ _WEBSOCKETPP_CPP11_RANDOM_ _WEBSOCKETPP_CPP11_MEMORY_ _WIN32_WINNT=0x0601)

target_link_libraries(DashboardProject PRIVATE Qt6::Widgets Qt6::Network Qt6::Gui Qt6::WebSockets Qt6::Sql)

if(WIN32)
    target_link_libraries(DashboardProject PRIVATE ws2_32 wsock32)
    target_link_libraries(cliente_teste PRIVATE ws2_32 wsock32)
endif()

# ── LinkDim WebCrawler C++ (Fase 1 — Database) ──────────────
set(CRAWLER_SOURCES
    crawler_main.cpp
    src/database/DatabaseManager.cpp
)

add_executable(LinkdimCrawler ${CRAWLER_SOURCES})

# Include path raiz do projeto (permite #include "src/...")
target_include_directories(LinkdimCrawler PRIVATE
    ${CMAKE_SOURCE_DIR}
)

target_link_libraries(LinkdimCrawler PRIVATE
    Qt6::Core
    Qt6::Network
    Qt6::Sql
)

# Habilita suporte a threads Qt
set_target_properties(LinkdimCrawler PROPERTIES
    WIN32_EXECUTABLE FALSE   # Console app — exibe logs no terminal
)

if(WIN32)
    target_link_libraries(LinkdimCrawler PRIVATE ws2_32)
    target_compile_definitions(LinkdimCrawler PRIVATE _WIN32_WINNT=0x0601)
endif()

