#version 330

in vec3 fragDirection;

uniform sampler2D texture0;
uniform float uDayPhase;
uniform vec3 uSunDirection;
uniform vec3 uSunColor;

out vec4 finalColor;

void main()
{
    vec3 direction = normalize(fragDirection);
    float gradientV = clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    float phase = clamp(uDayPhase, 0.0, 1.0);
    vec3 color = texture(texture0, vec2(phase, gradientV)).rgb;
    vec3 sunDirection = normalize(uSunDirection);
    float sunDot = max(dot(direction, sunDirection), 0.0);
    float sunDisk = pow(sunDot, 680.0);
    float sunGlow = pow(sunDot, 18.0) * 0.28;
    color += uSunColor * (sunGlow + sunDisk * 2.3);
    finalColor = vec4(color, 1.0);
}
