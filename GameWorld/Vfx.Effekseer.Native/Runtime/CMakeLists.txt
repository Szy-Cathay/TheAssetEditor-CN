# Keep Debug runtime identity, symbols and explicit guards while optimizing only the bridge translation unit.
# The parent and all pinned dependencies retain their original Debug flags.
if(MSVC)
    set(CMAKE_CXX_FLAGS_DEBUG "/Z7 /O2 /Ob2 /fp:strict")
endif()
add_library(AssetEditor.CN.Effekseer SHARED "${CMAKE_CURRENT_SOURCE_DIR}/../Bridge.cpp")
