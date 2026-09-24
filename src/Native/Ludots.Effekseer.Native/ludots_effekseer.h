#pragma once

#include <stdint.h>

#if defined(_WIN32)
#if defined(LUDOTS_EFFEKSEER_NATIVE_EXPORTS)
#define LUDOTS_EFK_API __declspec(dllexport)
#else
#define LUDOTS_EFK_API __declspec(dllimport)
#endif
#define LUDOTS_EFK_CALL __cdecl
#else
#define LUDOTS_EFK_API __attribute__((visibility("default")))
#define LUDOTS_EFK_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

enum LudotsEffekseerNodeType
{
    LUDOTS_EFK_NODE_SPRITE = 2,
    LUDOTS_EFK_NODE_RIBBON = 3,
    LUDOTS_EFK_NODE_RING = 4,
    LUDOTS_EFK_NODE_MODEL = 5,
    LUDOTS_EFK_NODE_TRACK = 6,
};

LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_get_api_version(void);
LUDOTS_EFK_API const char* LUDOTS_EFK_CALL ludots_effekseer_get_last_error(void* context);

LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_validate_asset(
    const char* path_utf8,
    int32_t expected_node_type);

LUDOTS_EFK_API void* LUDOTS_EFK_CALL ludots_effekseer_create(int32_t max_instances, int32_t max_squares);
LUDOTS_EFK_API void LUDOTS_EFK_CALL ludots_effekseer_destroy(void* context);

LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_load_asset(
    void* context,
    int32_t asset_id,
    const char* path_utf8,
    int32_t expected_node_type);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_unload_asset(void* context, int32_t asset_id);

LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_play(
    void* context,
    int32_t asset_id,
    float x,
    float y,
    float z);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_exists(void* context, int32_t handle);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_stop(void* context, int32_t handle);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_set_transform(
    void* context,
    int32_t handle,
    float x,
    float y,
    float z,
    float rotation_x,
    float rotation_y,
    float rotation_z,
    float scale_x,
    float scale_y,
    float scale_z);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_set_target(
    void* context,
    int32_t handle,
    float x,
    float y,
    float z);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_set_color(
    void* context,
    int32_t handle,
    uint8_t red,
    uint8_t green,
    uint8_t blue,
    uint8_t alpha);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_set_dynamic_input(
    void* context,
    int32_t handle,
    int32_t index,
    float value);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_set_shown(
    void* context,
    int32_t handle,
    int32_t shown);

LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_update(void* context, float delta_seconds);
LUDOTS_EFK_API int32_t LUDOTS_EFK_CALL ludots_effekseer_draw_perspective(
    void* context,
    float camera_x,
    float camera_y,
    float camera_z,
    float target_x,
    float target_y,
    float target_z,
    float up_x,
    float up_y,
    float up_z,
    float vertical_fov_radians,
    float aspect,
    float near_plane,
    float far_plane);

#ifdef __cplusplus
}
#endif

