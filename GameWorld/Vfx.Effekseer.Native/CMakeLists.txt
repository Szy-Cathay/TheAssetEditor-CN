cmake_minimum_required(VERSION 3.21)
# Embedded Debug symbols avoid the machine-wide MSVC PDB server.
set(CMAKE_C_FLAGS_DEBUG "/Z7 /Ob0 /Od /RTC1" CACHE STRING "Debug C flags" FORCE)
set(CMAKE_CXX_FLAGS_DEBUG "/Z7 /Ob0 /Od /RTC1" CACHE STRING "Debug C++ flags" FORCE)
project(AssetEditorCNEffekseer LANGUAGES C CXX)
option(AECN_VFX_NATIVE_TIMINGS "Compile native profiling scopes for an isolated diagnostic artifact" OFF)
set(AECN_VFX_DIAGNOSTIC_OUTPUT_DIRECTORY "" CACHE PATH "Explicit artifact directory for timing-enabled native binaries")
set(CMAKE_C_FLAGS_DEBUG "/Z7 /Ob0 /Od /RTC1" CACHE STRING "Debug C flags" FORCE)
set(CMAKE_CXX_FLAGS_DEBUG "/Z7 /Ob0 /Od /RTC1" CACHE STRING "Debug C++ flags" FORCE)

# The resource container and renderer ABI are pinned, never an arbitrary installed runtime.
set(EFFEKSEER_COMMIT "92fdeb349c0c7eab9dfaf152047f22dec2fa102d")
if(NOT EFFEKSEER_SOURCE_DIR)
    message(FATAL_ERROR "Set EFFEKSEER_SOURCE_DIR to the official Effekseer 170e checkout.")
endif()
execute_process(COMMAND git -C "${EFFEKSEER_SOURCE_DIR}" rev-parse HEAD
    OUTPUT_VARIABLE source_commit OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
if(NOT source_commit STREQUAL EFFEKSEER_COMMIT)
    message(FATAL_ERROR "Effekseer must be pinned to ${EFFEKSEER_COMMIT}, got ${source_commit}")
endif()
execute_process(COMMAND git -C "${EFFEKSEER_SOURCE_DIR}" -c core.fsmonitor=false diff --no-ext-diff --name-only HEAD --
    Dev/Cpp/Effekseer Dev/Cpp/EffekseerRendererCommon Dev/Cpp/EffekseerRendererDX11
    Dev/Cpp/EffekseerMaterialCompiler Dev/Cpp/3rdParty/stb_effekseer LICENSE_RUNTIME_DIRECTX
    OUTPUT_VARIABLE source_changes OUTPUT_STRIP_TRAILING_WHITESPACE COMMAND_ERROR_IS_FATAL ANY)
if(source_changes)
    message(FATAL_ERROR "Effekseer source has tracked modifications; use the unchanged pinned checkout.")
endif()
set(CMAKE_CXX_STANDARD 14)
set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>DLL")
set(CMAKE_POLICY_VERSION_MINIMUM 3.5)
set(NETWORK_ENABLED OFF)
set(USE_LIBPNG_LOADER OFF)
set(BUILD_WITH_EASY_PROFILER OFF)
function(FilterFolder)
endfunction()
function(WarnError)
endfunction()
add_subdirectory("${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/Effekseer" effekseer)
add_subdirectory("${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/EffekseerRendererDX11" renderer)
add_executable(AecnVfxMaterialCompiler CompileMaterials.cpp)
target_include_directories(AecnVfxMaterialCompiler PRIVATE
    "${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/Effekseer"
    "${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/EffekseerMaterialCompiler/DirectX11")
target_link_libraries(AecnVfxMaterialCompiler PRIVATE EffekseerRendererDX11 d3dcompiler)
add_executable(AecnVfxSceneShaderCompiler CompileSceneShaders.cpp)
target_link_libraries(AecnVfxSceneShaderCompiler PRIVATE d3dcompiler bcrypt)
set(scene_shader_sources "${CMAKE_CURRENT_SOURCE_DIR}/FloatSceneShaderSources.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/SceneUtilityShaderSources.h")
set(compiled_scene_shaders "${CMAKE_CURRENT_BINARY_DIR}/CompiledFloatSceneShaders.h")
set_source_files_properties(CompileSceneShaders.cpp PROPERTIES OBJECT_DEPENDS "${scene_shader_sources}")
add_custom_command(OUTPUT "${compiled_scene_shaders}"
    COMMAND $<TARGET_FILE:AecnVfxSceneShaderCompiler> "${compiled_scene_shaders}"
    DEPENDS AecnVfxSceneShaderCompiler ${scene_shader_sources}
    VERBATIM)
set(compiled_materials "${CMAKE_CURRENT_BINARY_DIR}/CompiledVfxMaterials.h")
set(material_headers
    "${CMAKE_CURRENT_SOURCE_DIR}/BulkSpriteShaderSources.h" "${CMAKE_CURRENT_SOURCE_DIR}/NativeMboitShaderSources.h" "${CMAKE_CURRENT_SOURCE_DIR}/CoverageMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/ScrollingMeshMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/OneShotMeshMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/BasicMeshMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/GemstoneMeshMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/AnimatedUvSpriteMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/BasicSpriteMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/PremultiplyAnimatedMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/SolidMeshMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/PrelitSpriteMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/SoulPanningMeshMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/GrowingVinesMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/RigidDissolveMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/DistortionMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/TidecallMaterial.h" "${CMAKE_CURRENT_SOURCE_DIR}/VertexAnimatedMeshMaterial.h"
    "${CMAKE_CURRENT_SOURCE_DIR}/RigidBasicDitherMaterial.h")
# Localized MSVC include output must not leave the generator or embedded shaders stale.
set_source_files_properties(CompileMaterials.cpp PROPERTIES OBJECT_DEPENDS "${material_headers}")
add_custom_command(OUTPUT "${compiled_materials}"
    COMMAND $<TARGET_FILE:AecnVfxMaterialCompiler> "${compiled_materials}"
    DEPENDS AecnVfxMaterialCompiler ${material_headers}
    VERBATIM)
add_custom_target(AecnVfxCompiledResources DEPENDS "${compiled_materials}" "${compiled_scene_shaders}")
add_subdirectory(Runtime)
add_dependencies(AssetEditor.CN.Effekseer AecnVfxCompiledResources)
set_source_files_properties("${CMAKE_CURRENT_SOURCE_DIR}/Bridge.cpp" TARGET_DIRECTORY AssetEditor.CN.Effekseer PROPERTIES OBJECT_DEPENDS
    "${compiled_materials};${compiled_scene_shaders};${CMAKE_CURRENT_SOURCE_DIR}/Bridge.h;${CMAKE_CURRENT_SOURCE_DIR}/FloatSceneTarget.h;${CMAKE_CURRENT_SOURCE_DIR}/WeightedDistortionTarget.h;${CMAKE_CURRENT_SOURCE_DIR}/SpriteUploadCache.h;${CMAKE_CURRENT_SOURCE_DIR}/BulkSpriteQueue.h;${CMAKE_CURRENT_SOURCE_DIR}/VfxRenderTiming.h;${CMAKE_CURRENT_SOURCE_DIR}/HalfResolutionScene.h")
target_include_directories(AssetEditor.CN.Effekseer PRIVATE
    "${CMAKE_CURRENT_BINARY_DIR}"
    "${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/Effekseer"
    "${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/EffekseerRendererDX11"
    "${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/EffekseerRendererDX11/EffekseerRenderer"
    "${EFFEKSEER_SOURCE_DIR}/Dev/Cpp/EffekseerRendererCommon")
target_link_libraries(AssetEditor.CN.Effekseer PRIVATE EffekseerRendererDX11 d3d11 d3dcompiler)
set(aecn_runtime_output "${CMAKE_CURRENT_SOURCE_DIR}/bin/${CMAKE_BUILD_TYPE}")
if(AECN_VFX_NATIVE_TIMINGS)
    if(NOT AECN_VFX_DIAGNOSTIC_OUTPUT_DIRECTORY)
        message(FATAL_ERROR "Timing-enabled builds require an explicit artifact output directory")
    endif()
    get_filename_component(aecn_artifacts "${CMAKE_CURRENT_SOURCE_DIR}/../../artifacts" ABSOLUTE)
    get_filename_component(aecn_diagnostic_output "${AECN_VFX_DIAGNOSTIC_OUTPUT_DIRECTORY}" ABSOLUTE)
    file(TO_CMAKE_PATH "${aecn_artifacts}/" aecn_artifacts_prefix)
    file(TO_CMAKE_PATH "${aecn_diagnostic_output}/" aecn_diagnostic_prefix)
    string(TOLOWER "${aecn_artifacts_prefix}" aecn_artifacts_prefix)
    string(TOLOWER "${aecn_diagnostic_prefix}" aecn_diagnostic_prefix)
    string(FIND "${aecn_diagnostic_prefix}" "${aecn_artifacts_prefix}" aecn_prefix_position)
    if(NOT aecn_prefix_position EQUAL 0)
        message(FATAL_ERROR "Timing-enabled output must remain in this repository's artifacts")
    endif()
    set(aecn_runtime_output "${aecn_diagnostic_output}")
    target_compile_definitions(AssetEditor.CN.Effekseer PRIVATE AECN_VFX_NATIVE_TIMINGS=1)
endif()
set_target_properties(AssetEditor.CN.Effekseer PROPERTIES RUNTIME_OUTPUT_DIRECTORY "${aecn_runtime_output}")
configure_file("${EFFEKSEER_SOURCE_DIR}/LICENSE_RUNTIME_DIRECTX"
    "${aecn_runtime_output}/Effekseer.LICENSE.txt" COPYONLY)
configure_file("${CMAKE_CURRENT_SOURCE_DIR}/Effekseer.stb.LICENSE.txt"
    "${aecn_runtime_output}/Effekseer.stb.LICENSE.txt" COPYONLY)
