#version 330

in vec3 fragDirection;

uniform sampler2D texture0;
uniform float uDayPhase;

out vec4 finalColor;

void main()
{
    vec3 direction = normalize(fragDirection);
    float gradientV = clamp(direction.y * 0.5 + 0.5, 0.0, 1.0);
    float phase = clamp(uDayPhase, 0.0, 1.0);
    vec3 color = texture(texture0, vec2(phase, gradientV)).rgb;
    finalColor = vec4(color, 1.0);
}
