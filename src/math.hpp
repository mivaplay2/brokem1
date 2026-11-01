#pragma once

#include <array>

namespace math {

struct Vec2 {
    float x{0.0F};
    float y{0.0F};
};

struct Vec3 {
    float x{0.0F};
    float y{0.0F};
    float z{0.0F};

    Vec3 operator+(const Vec3& other) const { return {x + other.x, y + other.y, z + other.z}; }
    Vec3 operator-(const Vec3& other) const { return {x - other.x, y - other.y, z - other.z}; }
};

// Row-major 4x4 view-projection matrix, laid out exactly as the client stores it.
struct ViewMatrix {
    std::array<float, 16> m{};

    float At(int row, int col) const { return m[static_cast<std::size_t>(row) * 4U + static_cast<std::size_t>(col)]; }
};

bool IsFinite(float value);
bool IsFinite(const Vec3& value);
bool IsFinite(const ViewMatrix& value);

float Length(const Vec3& value);
float Distance(const Vec3& a, const Vec3& b);

// Projects a world position into client-area pixels.
// Returns false for points behind the camera, for non-finite inputs and for
// non-finite results. Screen bounds are not enforced here; callers clip.
bool WorldToScreen(const ViewMatrix& matrix, const Vec3& world, int screen_width, int screen_height, Vec2& out);

}  // namespace math
