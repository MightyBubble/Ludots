#version 330

in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragWorldPos;

uniform vec4 colDiffuse;
uniform vec4 tint;
uniform float uTime;

out vec4 finalColor;

void main()
{
    vec2 centered = fragTexCoord - vec2(0.5);
    float radial = 1.0 - clamp(length(centered) * 2.0, 0.0, 1.0);
    float pulse = 0.55 + 0.45 * sin(uTime * 5.0 + fragWorldPos.x * 1.7 + fragWorldPos.z * 1.3);
    float soft = pow(max(radial, 0.0), 1.35);
    vec3 rgb = tint.rgb * colDiffuse.rgb * fragColor.rgb * (0.65 + 0.35 * pulse);
    float alpha = tint.a * colDiffuse.a * fragColor.a * max(soft, 0.18) * pulse;
    finalColor = vec4(rgb, alpha);
}
