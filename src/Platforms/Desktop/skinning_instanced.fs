#version 330

in vec2 fragTexCoord;
in vec4 fragColor;
in vec3 fragNormal;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec4 tint;
uniform vec3 uLightDir;
uniform vec4 uAmbient;
uniform vec3 uLightColor;
uniform float uLightIntensity;

out vec4 finalColor;

void main()
{
    vec4 texel = texture(texture0, fragTexCoord);
    vec4 color = fragColor;
    if (color.a <= 0.001)
    {
        color = vec4(1.0);
    }
    vec4 albedo = texel * colDiffuse * tint * color;
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightDir);
    float ndl = max(dot(N, L), 0.0);
    vec3 lighting = (uAmbient.rgb * uAmbient.a) + (uLightColor * uLightIntensity * ndl);
    finalColor = vec4(clamp(albedo.rgb * lighting, 0.0, 1.0), albedo.a);
}
