#version 330

in vec3 fragPos;
in vec3 fragNormal;
in vec2 fragTexCoord;
in vec4 fragColor;

uniform sampler2D texture0;
uniform int uUseTexture;
uniform vec3 uLightPos;
uniform vec3 uViewPos;
uniform float uAmbient;
uniform float uLightIntensity;

out vec4 finalColor;

void main()
{
    vec3 N = normalize(fragNormal);
    vec3 L = normalize(uLightPos - fragPos);
    float ndl = abs(dot(N, L));
    vec4 albedo = uUseTexture != 0 ? texture(texture0, fragTexCoord) : fragColor;
    float light = uUseTexture != 0 ? 1.0 : (uAmbient + uLightIntensity * ndl);
    vec3 lit = albedo.rgb * light;
    finalColor = vec4(clamp(lit, 0.0, 1.0), albedo.a);
}

