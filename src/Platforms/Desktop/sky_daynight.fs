#version 330

in vec3 fragDirection;

uniform sampler2D texture0;
uniform float uDayPhase;

out vec4 finalColor;

void main()
{
    vec3 direction = normalize(fragDirection);
    // Gradient rows run zenith (v=0) to horizon (v=1); below-horizon directions keep the horizon color.
    float v = 1.0 - clamp(direction.y, 0.0, 1.0);
    finalColor = texture(texture0, vec2(uDayPhase, v));
}
