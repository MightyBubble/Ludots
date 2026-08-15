#version 330

in vec3 fragDirection;

uniform vec3 uSunDirection;
uniform vec3 uSunColor;
uniform vec3 uZenithColor;
uniform vec3 uHorizonColor;
uniform vec3 uGroundHazeColor;
uniform float uTime;

out vec4 finalColor;

void main()
{
    vec3 direction = normalize(fragDirection);
    vec3 sunDirection = normalize(uSunDirection);
    float skyBand = smoothstep(-0.10, 0.85, direction.y);
    vec3 sky = mix(uHorizonColor, uZenithColor, skyBand);
    float groundBand = 1.0 - smoothstep(-0.22, 0.12, direction.y);
    sky = mix(sky, uGroundHazeColor, groundBand * 0.42);

    float sunDot = max(dot(direction, sunDirection), 0.0);
    float sunDisk = pow(sunDot, 720.0);
    float sunGlow = pow(sunDot, 22.0) * 0.34;
    float airShimmer = sin((direction.x + direction.z + uTime * 0.012) * 18.0) * 0.006;
    vec3 color = sky + (uSunColor * (sunGlow + sunDisk * 2.4)) + vec3(airShimmer);
    finalColor = vec4(clamp(color, 0.0, 1.0), 1.0);
}
