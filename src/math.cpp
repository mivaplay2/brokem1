#include "math.hpp"

#include <cmath>

namespace math {

bool IsFinite(float value) {
    return std::isfinite(value);
}

bool IsFinite(const Vec3& value) {
    return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
}

bool IsFinite(const ViewMatrix& value) {
    for (float component : value.m) {
        if (!IsFinite(component)) {
            return false;
        }
    }
    return true;
}

float Length(const Vec3& value) {
    return std::sqrt((value.x * value.x) + (value.y * value.y) + (value.z * value.z));
}

float Distance(const Vec3& a, const Vec3& b) {
    return Length(a - b);
}

bool WorldToScreen(const ViewMatrix& matrix, const Vec3& world, int screen_width, int screen_height, Vec2& out) {
    if (screen_width <= 0 || screen_height <= 0) {
        return false;
    }
    if (!IsFinite(world) || !IsFinite(matrix)) {
        return false;
    }

    const float w = (matrix.At(3, 0) * world.x) + (matrix.At(3, 1) * world.y) + (matrix.At(3, 2) * world.z) + matrix.At(3, 3);
    if (!IsFinite(w) || w < 0.01F) {
        return false;
    }

    const float clip_x = (matrix.At(0, 0) * world.x) + (matrix.At(0, 1) * world.y) + (matrix.At(0, 2) * world.z) + matrix.At(0, 3);
    const float clip_y = (matrix.At(1, 0) * world.x) + (matrix.At(1, 1) * world.y) + (matrix.At(1, 2) * world.z) + matrix.At(1, 3);
    if (!IsFinite(clip_x) || !IsFinite(clip_y)) {
        return false;
    }

    const float ndc_x = clip_x / w;
    const float ndc_y = clip_y / w;
    if (!IsFinite(ndc_x) || !IsFinite(ndc_y)) {
        return false;
    }

    const float half_width = static_cast<float>(screen_width) * 0.5F;
    const float half_height = static_cast<float>(screen_height) * 0.5F;

    out.x = half_width * (1.0F + ndc_x);
    out.y = half_height * (1.0F - ndc_y);

    return IsFinite(out.x) && IsFinite(out.y);
}

}  // namespace math
