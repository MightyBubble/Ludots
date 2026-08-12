#version 330

in vec2 fragTexCoord;
in vec3 fragNormal;
out vec4 finalColor;

uniform sampler2D texture0;
uniform vec4 colDiffuse;
uniform vec4 tint;
uniform vec3 uLightDir;
uniform vec4 uAmbient;
uniform vec3 uLightColor;
uniform float uLightIntensity;

void main()
{
    vec4 albedo = texture(texture0, fragTexCoord) * colDiffuse * tint;
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightDir);
    float ndl = max(dot(N, L), 0.0);
    vec3 lighting = (uAmbient.rgb * uAmbient.a) + (uLightColor * uLightIntensity * ndl);
    finalColor = vec4(clamp(albedo.rgb * lighting, 0.0, 1.0), albedo.a);
}
