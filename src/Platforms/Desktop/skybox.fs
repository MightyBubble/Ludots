#version 330

in vec3 fragDirection;

// ludo:include sun_disk.glsl.inc
uniform vec3 uZenithColor;
uniform vec3 uHorizonColor;
uniform vec3 uGroundHazeColor;
uniform float uTime;

out vec4 finalColor;

void main()
{
    vec3 direction = normalize(fragDirection);
    float skyBand = smoothstep(-0.10, 0.85, direction.y);
    vec3 sky = mix(uHorizonColor, uZenithColor, skyBand);
    float groundBand = 1.0 - smoothstep(-0.22, 0.12, direction.y);
    sky = mix(sky, uGroundHazeColor, groundBand * 0.42);

    float airShimmer = sin((direction.x + direction.z + uTime * 0.012) * 18.0) * 0.006;
    vec3 color = sky + SunHalo(direction, uSunColor) + vec3(airShimmer);
    finalColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}
