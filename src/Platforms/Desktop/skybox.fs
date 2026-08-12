#version 330

in vec3 fragDirection;

uniform sampler2D texture0;
uniform float uDayPhase;

out vec4 finalColor;

void main()
{
    vec3 dir = normalize(fragDirection);
    float height = clamp(dir.y * 0.5 + 0.5, 0.0, 1.0);
    float phase = clamp(uDayPhase, 0.0, 1.0);
    vec3 sky = texture(texture0, vec2(phase, 1.0 - height)).rgb;
    finalColor = vec4(sky, 1.0);
}
